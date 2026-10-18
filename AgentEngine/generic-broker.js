"use strict";

// Isolated broker core. A host must provide a trusted caller verifier, ACL, endpoint
// registry, text policy, and the official delivery adapter before this can be integrated.
const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const HASH_RE = /^[0-9a-f]{64}$/;
const STATUS = new Set(["QUEUED", "RUNNING", "COMPLETE", "FAILED", "UNKNOWN"]);
const EVENT_TYPES = new Set(["submitted", "dispatch-started", "dispatch-acknowledged", "dispatch-unknown", "dispatch-failed", "completed"]);
const ZERO_HASH = "0".repeat(64);
const MAX_MESSAGE_BYTES = 64 * 1024;
const MAX_REPORT_BYTES = 8 * 1024 * 1024;
const MAX_ENDPOINTS = 10000;

function brokerError(code, message) {
  const error = new Error(message);
  error.code = code;
  return error;
}

function requireUuid(value, field) {
  if (typeof value !== "string" || !UUID_RE.test(value)) throw brokerError("INVALID_ID", field + " must be a UUID");
  return value.toLowerCase();
}

function requireText(value, field, maxBytes) {
  if (typeof value !== "string") throw brokerError("INVALID_TEXT", field + " must be text");
  if (Buffer.byteLength(value, "utf8") > maxBytes) throw brokerError("TEXT_TOO_LARGE", field + " exceeds its byte limit");
  return value;
}

function sha256(bytes) {
  return crypto.createHash("sha256").update(bytes).digest("hex");
}

function endpointFingerprint(endpoint) {
  return sha256(Buffer.from(JSON.stringify({
    endpointId: endpoint.endpointId, projectId: endpoint.projectId, sessionId: endpoint.sessionId,
    agentId: endpoint.agentId, cliId: endpoint.cliId, profileId: endpoint.profileId,
    transportId: endpoint.transportId, capabilities: endpoint.capabilities,
  }), "utf8"));
}

function copy(value) {
  return JSON.parse(JSON.stringify(value));
}

function sessionScope(receipt) {
  const scope = {};
  if (receipt.senderSessionId !== undefined) scope.senderSessionId = receipt.senderSessionId;
  if (receipt.recipientSessionId !== undefined) scope.recipientSessionId = receipt.recipientSessionId;
  return scope;
}

function endpointFrom(value) {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw brokerError("INVALID_ENDPOINT", "Endpoint registry entry must be an object");
  const endpoint = {
    endpointId: requireUuid(value.endpointId, "endpointId"),
    projectId: requireUuid(value.projectId, "projectId"),
    sessionId: requireText(value.sessionId, "sessionId", 256),
    agentId: requireText(value.agentId, "agentId", 128),
    cliId: requireText(value.cliId, "cliId", 128),
    profileId: requireText(value.profileId, "profileId", 128),
    transportId: requireText(value.transportId, "transportId", 128),
    capabilities: {
      receiveTask: value.capabilities && value.capabilities.receiveTask === true,
      readReport: value.capabilities && value.capabilities.readReport === true,
    },
  };
  if (!endpoint.sessionId || !endpoint.agentId || !endpoint.cliId || !endpoint.profileId || !endpoint.transportId)
    throw brokerError("INVALID_ENDPOINT", "Endpoint identity fields must be non-empty");
  return Object.freeze(endpoint);
}

class GenericEndpointBroker {
  constructor(options) {
    if (!options || typeof options !== "object") throw new TypeError("Broker options are required");
    for (const name of ["verifyCaller", "authorize", "loadEndpoints", "sanitizeText", "dispatch"])
      if (typeof options[name] !== "function") throw new TypeError(name + " callback is required");
    if (typeof options.storageDir !== "string" || !path.isAbsolute(options.storageDir))
      throw new TypeError("storageDir must be an absolute path");

    this.verifyCaller = options.verifyCaller;
    this.authorize = options.authorize;
    this.loadEndpoints = options.loadEndpoints;
    this.sanitizeText = options.sanitizeText;
    this.dispatch = options.dispatch;
    this.storageDir = path.resolve(options.storageDir);
    this.reportDir = path.join(this.storageDir, "reports");
    this.journalPath = path.join(this.storageDir, "broker.journal.ndjson");
    this.lockPath = path.join(this.storageDir, "broker.lock");
    fs.mkdirSync(this.reportDir, { recursive: true });

    this.receipts = new Map();
    this.reportRefs = new Map();
    this.sequence = 0;
    this.previousHash = ZERO_HASH;
    this.readOnly = false;
    this.degradedReason = null;
    this.lockFd = null;
    this.lockToken = crypto.randomUUID();
    this.serial = Promise.resolve();
    this.closed = false;

    try {
      this.lockFd = fs.openSync(this.lockPath, "wx", 0o600);
      fs.writeSync(this.lockFd, JSON.stringify({ pid: process.pid, token: this.lockToken }) + "\n", null, "utf8");
      fs.fsyncSync(this.lockFd);
    } catch (error) {
      if (this.lockFd !== null) {
        try { fs.closeSync(this.lockFd); } catch {}
        this.lockFd = null;
      }
      if (error.code === "EEXIST") this.readOnly = true;
      else throw error;
    }

    this._loadJournal();
  }

  _queue(fn) {
    const result = this.serial.then(fn, fn);
    this.serial = result.catch(() => {});
    return result;
  }

  _assertOpen() {
    if (this.closed) throw brokerError("BROKER_CLOSED", "Broker is closed");
  }

  _assertWritable() {
    this._assertOpen();
    if (this.readOnly) throw brokerError("BROKER_READ_ONLY", this.degradedReason || "Another broker writer owns this store; mutations are blocked");
  }

  _loadJournal() {
    let bytes;
    try { bytes = fs.readFileSync(this.journalPath); }
    catch (error) { if (error.code === "ENOENT") return; throw error; }
    const lastNewline = bytes.lastIndexOf(0x0a);
    const completeLength = lastNewline < 0 ? 0 : lastNewline + 1;
    if (completeLength !== bytes.length) {
      this.readOnly = true;
      this.degradedReason = "Journal has a torn final record; it is preserved for manual recovery";
    }
    const completeText = bytes.subarray(0, completeLength).toString("utf8");
    const lines = completeText.split("\n");
    lines.pop();
    for (const line of lines) {
      let row;
      try { row = JSON.parse(line); }
      catch { this._degrade("Journal contains invalid JSON; writes are blocked"); break; }
      if (!this._validJournalRow(row)) { this._degrade("Journal integrity check failed; writes are blocked"); break; }
      this.sequence = row.sequence;
      this.previousHash = row.eventHash;
      try { this._apply(row.type, row.receipt); }
      catch { this._degrade("Journal state is inconsistent; writes are blocked"); break; }
    }
  }

  _degrade(reason) {
    this.readOnly = true;
    this.degradedReason = reason;
  }

  _journalBody(type, receipt, sequence, previousHash) {
    return { schemaVersion: 1, sequence, previousHash, type, receipt };
  }

  _validJournalRow(row) {
    if (!row || row.schemaVersion !== 1 || !Number.isSafeInteger(row.sequence) || row.sequence !== this.sequence + 1 ||
        row.previousHash !== this.previousHash || !EVENT_TYPES.has(row.type) || !row.receipt ||
        typeof row.eventHash !== "string" || !HASH_RE.test(row.eventHash)) return false;
    const body = this._journalBody(row.type, row.receipt, row.sequence, row.previousHash);
    return sha256(Buffer.from(JSON.stringify(body), "utf8")) === row.eventHash;
  }

  _apply(type, receipt) {
    requireUuid(receipt.requestId, "requestId");
    requireUuid(receipt.receiptId, "receiptId");
    requireUuid(receipt.projectId, "projectId");
    requireUuid(receipt.taskId, "taskId");
    requireUuid(receipt.senderEndpointId, "senderEndpointId");
    requireUuid(receipt.recipientEndpointId, "recipientEndpointId");
    if (receipt.senderSessionId !== undefined || receipt.recipientSessionId !== undefined) {
      if (!requireText(receipt.senderSessionId, "senderSessionId", 256) ||
          !requireText(receipt.recipientSessionId, "recipientSessionId", 256))
        throw brokerError("JOURNAL_STATE", "Receipt session identities must be non-empty");
    }
    if (!STATUS.has(receipt.status) || !HASH_RE.test(receipt.fingerprint)) throw brokerError("JOURNAL_STATE", "Invalid receipt state");
    if (!HASH_RE.test(receipt.senderEndpointFingerprint || "") || !HASH_RE.test(receipt.recipientEndpointFingerprint || ""))
      throw brokerError("JOURNAL_STATE", "Invalid endpoint identity fingerprint");
    const prior = this.receipts.get(receipt.requestId);
    if (type === "submitted") {
      if (prior || receipt.status !== "QUEUED") throw brokerError("JOURNAL_STATE", "Duplicate or invalid submitted event");
    } else {
      if (!prior || prior.receiptId !== receipt.receiptId || prior.fingerprint !== receipt.fingerprint ||
          prior.projectId !== receipt.projectId || prior.senderEndpointId !== receipt.senderEndpointId ||
          prior.recipientEndpointId !== receipt.recipientEndpointId || prior.taskId !== receipt.taskId ||
          prior.senderSessionId !== receipt.senderSessionId || prior.recipientSessionId !== receipt.recipientSessionId ||
          prior.senderEndpointFingerprint !== receipt.senderEndpointFingerprint ||
          prior.recipientEndpointFingerprint !== receipt.recipientEndpointFingerprint ||
          prior.message !== receipt.message) throw brokerError("JOURNAL_STATE", "Receipt transition does not match its original request");
    }
    if (type === "dispatch-started" && receipt.status !== "RUNNING") throw brokerError("JOURNAL_STATE", "Invalid dispatch-started status");
    if (type === "dispatch-acknowledged" && (receipt.status !== "RUNNING" || !receipt.dispatchAcknowledgedAt)) throw brokerError("JOURNAL_STATE", "Invalid dispatch acknowledgment");
    if (type === "dispatch-unknown" && (receipt.status !== "UNKNOWN" || receipt.failureCode !== "DISPATCH_OUTCOME_UNKNOWN")) throw brokerError("JOURNAL_STATE", "Invalid unknown dispatch state");
    if (type === "dispatch-failed" && (receipt.status !== "FAILED" || !receipt.failureCode)) throw brokerError("JOURNAL_STATE", "Invalid failed dispatch state");
    if (type === "dispatch-started" && prior.status !== "QUEUED") throw brokerError("JOURNAL_STATE", "Dispatch cannot restart from a prior receipt state");
    if (["dispatch-acknowledged", "dispatch-unknown"].includes(type) && prior.status !== "RUNNING")
      throw brokerError("JOURNAL_STATE", "Dispatch result does not follow a running receipt");
    if (type === "dispatch-failed" && !(
      prior.status === "QUEUED" && receipt.failureCode === "PRE_DISPATCH_AUTHORIZATION_FAILED" ||
      prior.status === "RUNNING" && receipt.failureCode === "DISPATCH_REJECTED"
    )) throw brokerError("JOURNAL_STATE", "Dispatch failure does not follow a valid prior state");
    if (type === "completed" && (receipt.status !== "COMPLETE" || !["RUNNING", "UNKNOWN"].includes(prior.status) ||
        !UUID_RE.test(receipt.reportRef || "") || !HASH_RE.test(receipt.reportSha256 || "") ||
        !Number.isSafeInteger(receipt.reportByteLength) || receipt.reportByteLength < 1 ||
        !Number.isSafeInteger(receipt.reportCharacterLength) || receipt.reportCharacterLength < 1 ||
        receipt.reportIntegrity !== "VERIFIED"))
      throw brokerError("JOURNAL_STATE", "Invalid completion state");
    const snapshot = copy(receipt);
    this.receipts.set(receipt.requestId, snapshot);
    if (snapshot.reportRef) this.reportRefs.set(snapshot.reportRef, snapshot.requestId);
  }

  _append(type, receipt) {
    this._assertWritable();
    const sequence = this.sequence + 1;
    const body = this._journalBody(type, copy(receipt), sequence, this.previousHash);
    const eventHash = sha256(Buffer.from(JSON.stringify(body), "utf8"));
    const bytes = Buffer.from(JSON.stringify({ ...body, eventHash }) + "\n", "utf8");
    const fd = fs.openSync(this.journalPath, "a", 0o600);
    try {
      let offset = 0;
      while (offset < bytes.length) offset += fs.writeSync(fd, bytes, offset, bytes.length - offset);
      fs.fsyncSync(fd);
    } finally { fs.closeSync(fd); }
    this.sequence = sequence;
    this.previousHash = eventHash;
    this._apply(type, receipt);
  }

  async _verifiedCaller(credential) {
    const raw = await this.verifyCaller(credential);
    if (!raw || typeof raw !== "object") throw brokerError("UNAUTHENTICATED", "Transport did not provide a verified caller");
    const principalId = requireText(raw.principalId, "principalId", 256);
    const endpointId = requireUuid(raw.endpointId, "caller.endpointId");
    if (!principalId) throw brokerError("UNAUTHENTICATED", "Verified caller has no principal identity");
    return Object.freeze({ principalId, endpointId });
  }

  async _endpointMap() {
    const raw = await this.loadEndpoints();
    if (!Array.isArray(raw) || raw.length > MAX_ENDPOINTS) throw brokerError("ENDPOINT_REGISTRY", "Endpoint registry is unavailable or exceeds its limit");
    const map = new Map();
    for (const value of raw) {
      const endpoint = endpointFrom(value);
      if (map.has(endpoint.endpointId)) throw brokerError("ENDPOINT_REGISTRY", "Endpoint UUID is duplicated");
      map.set(endpoint.endpointId, endpoint);
    }
    return map;
  }

  async _authorized(caller, operation, scope) {
    const allowed = await this.authorize(caller, operation, Object.freeze(copy(scope)));
    if (allowed !== true) throw brokerError("FORBIDDEN", "Caller ACL denied " + operation);
  }

  _callerEndpoint(caller, endpointMap, projectId) {
    const endpoint = endpointMap.get(caller.endpointId);
    if (!endpoint || endpoint.projectId !== projectId) throw brokerError("PROJECT_SCOPE", "Caller endpoint is not registered in the requested project");
    return endpoint;
  }

  async listEndpoints(credential, projectId) {
    this._assertOpen();
    projectId = requireUuid(projectId, "projectId");
    const caller = await this._verifiedCaller(credential);
    const endpoints = await this._endpointMap();
    this._callerEndpoint(caller, endpoints, projectId);
    await this._authorized(caller, "listEndpoints", { projectId });
    return [...endpoints.values()].filter(endpoint => endpoint.projectId === projectId).map(copy);
  }

  async submit(credential, input) {
    this._assertOpen();
    if (!input || typeof input !== "object" || Array.isArray(input)) throw brokerError("INVALID_REQUEST", "Task request must be an object");
    const allowedKeys = new Set(["requestId", "taskId", "projectId", "recipientEndpointId", "message"]);
    if (Object.keys(input).some(key => !allowedKeys.has(key))) throw brokerError("INVALID_REQUEST", "Task request contains unsupported fields");
    const requestId = requireUuid(input.requestId, "requestId");
    const taskId = requireUuid(input.taskId, "taskId");
    const projectId = requireUuid(input.projectId, "projectId");
    const recipientEndpointId = requireUuid(input.recipientEndpointId, "recipientEndpointId");
    const rawMessage = requireText(input.message, "message", MAX_MESSAGE_BYTES);
    const message = await this.sanitizeText(rawMessage, { kind: "task", projectId, taskId });
    requireText(message, "sanitized message", MAX_MESSAGE_BYTES);
    if (!message) throw brokerError("INVALID_TEXT", "Task message is empty after content policy");
    const caller = await this._verifiedCaller(credential);
    const endpoints = await this._endpointMap();
    const sender = this._callerEndpoint(caller, endpoints, projectId);
    const recipient = endpoints.get(recipientEndpointId);
    if (!recipient || recipient.projectId !== projectId) throw brokerError("PROJECT_SCOPE", "Recipient endpoint is not registered in the same project");
    if (!recipient.capabilities.receiveTask) throw brokerError("CAPABILITY_DENIED", "Recipient endpoint cannot receive tasks");
    await this._authorized(caller, "submit", {
      projectId, taskId, senderEndpointId: sender.endpointId, recipientEndpointId,
      senderSessionId: sender.sessionId, recipientSessionId: recipient.sessionId,
    });
    const senderEndpointFingerprint = endpointFingerprint(sender);
    const recipientEndpointFingerprint = endpointFingerprint(recipient);
    const fingerprint = sha256(Buffer.from(JSON.stringify({
      projectId, taskId, senderEndpointId: sender.endpointId, senderEndpointFingerprint,
      recipientEndpointId, recipientEndpointFingerprint, message,
    }), "utf8"));

    const prepared = await this._queue(async () => {
      this._assertWritable();
      const existing = this.receipts.get(requestId);
      if (existing) {
        if (existing.fingerprint !== fingerprint) throw brokerError("IDEMPOTENCY_CONFLICT", "requestId already belongs to a different task fingerprint");
        return { receipt: copy(existing), duplicate: true };
      }
      const receipt = {
        receiptId: crypto.randomUUID(), requestId, taskId, projectId,
        senderEndpointId: sender.endpointId, recipientEndpointId, message, fingerprint,
        senderSessionId: sender.sessionId, recipientSessionId: recipient.sessionId,
        senderEndpointFingerprint, recipientEndpointFingerprint,
        timestamp: new Date().toISOString(), status: "QUEUED",
      };
      this._append("submitted", receipt);
      return { receipt: copy(receipt), duplicate: false };
    });
    if (prepared.duplicate) return prepared.receipt;

    const dispatchRecord = await this._queue(async () => {
      this._assertWritable();
      let receipt = copy(this.receipts.get(requestId));
      let currentRecipient;
      try {
        const currentCaller = await this._verifiedCaller(credential);
        if (currentCaller.endpointId !== receipt.senderEndpointId) throw brokerError("FORBIDDEN", "Verified sender identity changed before dispatch");
        const currentEndpoints = await this._endpointMap();
        const currentSender = this._callerEndpoint(currentCaller, currentEndpoints, receipt.projectId);
        currentRecipient = currentEndpoints.get(receipt.recipientEndpointId);
        if (endpointFingerprint(currentSender) !== receipt.senderEndpointFingerprint || !currentRecipient ||
            currentRecipient.projectId !== receipt.projectId || !currentRecipient.capabilities.receiveTask ||
            endpointFingerprint(currentRecipient) !== receipt.recipientEndpointFingerprint)
          throw brokerError("PROJECT_SCOPE", "Recipient identity or capability changed before dispatch");
        await this._authorized(currentCaller, "dispatch", {
          projectId: receipt.projectId, taskId: receipt.taskId,
          senderEndpointId: currentSender.endpointId, recipientEndpointId: currentRecipient.endpointId,
          senderSessionId: currentSender.sessionId, recipientSessionId: currentRecipient.sessionId,
        });
      } catch {
        receipt.status = "FAILED";
        receipt.failureCode = "PRE_DISPATCH_AUTHORIZATION_FAILED";
        this._append("dispatch-failed", receipt);
        return { receipt: copy(receipt), denied: true };
      }
      receipt.status = "RUNNING";
      receipt.dispatchStartedAt = new Date().toISOString();
      this._append("dispatch-started", receipt);
      return { receipt: copy(receipt), recipient: copy(currentRecipient) };
    });
    if (dispatchRecord.denied) return dispatchRecord.receipt;

    let dispatchResult;
    let thrown = false;
    try {
      dispatchResult = await this.dispatch(Object.freeze(dispatchRecord.recipient), Object.freeze({
        requestId, taskId, projectId, senderEndpointId: dispatchRecord.receipt.senderEndpointId,
        recipientEndpointId, message,
      }));
    } catch { thrown = true; }

    return this._queue(async () => {
      this._assertWritable();
      const receipt = copy(this.receipts.get(requestId));
      if (receipt.status !== "RUNNING") return receipt;
      if (!thrown && dispatchResult && dispatchResult.accepted === true) {
        receipt.dispatchAcknowledgedAt = new Date().toISOString();
        this._append("dispatch-acknowledged", receipt);
      } else if (!thrown && dispatchResult && dispatchResult.accepted === false && dispatchResult.definitive === true) {
        receipt.status = "FAILED";
        receipt.failureCode = "DISPATCH_REJECTED";
        this._append("dispatch-failed", receipt);
      } else {
        receipt.status = "UNKNOWN";
        receipt.failureCode = "DISPATCH_OUTCOME_UNKNOWN";
        this._append("dispatch-unknown", receipt);
      }
      return copy(this.receipts.get(requestId));
    });
  }

  async getReceipt(credential, requestId) {
    this._assertOpen();
    requestId = requireUuid(requestId, "requestId");
    const caller = await this._verifiedCaller(credential);
    const receipt = this.receipts.get(requestId);
    if (!receipt) throw brokerError("NOT_FOUND", "Receipt was not found");
    const endpoints = await this._endpointMap();
    const endpoint = this._callerEndpoint(caller, endpoints, receipt.projectId);
    const expectedFingerprint = caller.endpointId === receipt.senderEndpointId
      ? receipt.senderEndpointFingerprint
      : caller.endpointId === receipt.recipientEndpointId ? receipt.recipientEndpointFingerprint : null;
    if (!expectedFingerprint || endpointFingerprint(endpoint) !== expectedFingerprint)
      throw brokerError("FORBIDDEN", "Caller is outside the receipt participants or its endpoint identity changed");
    await this._authorized(caller, "getReceipt", {
      projectId: receipt.projectId, taskId: receipt.taskId,
      senderEndpointId: receipt.senderEndpointId, recipientEndpointId: receipt.recipientEndpointId,
      ...sessionScope(receipt),
    });
    return copy(receipt);
  }

  async complete(credential, receiptId, reportText) {
    this._assertOpen();
    receiptId = requireUuid(receiptId, "receiptId");
    const rawReport = requireText(reportText, "report", MAX_REPORT_BYTES);
    const caller = await this._verifiedCaller(credential);
    const initialReceipt = [...this.receipts.values()].find(receipt => receipt.receiptId === receiptId);
    if (!initialReceipt) throw brokerError("NOT_FOUND", "Receipt was not found");
    const report = await this.sanitizeText(rawReport, {
      kind: "report", projectId: initialReceipt.projectId, taskId: initialReceipt.taskId,
      receiptId,
    });
    requireText(report, "sanitized report", MAX_REPORT_BYTES);
    if (!report) throw brokerError("INVALID_TEXT", "Report is empty after content policy");
    const reportBytes = Buffer.from(report, "utf8");
    const decodedReport = reportBytes.toString("utf8");
    if (decodedReport !== report) throw brokerError("INVALID_UTF8", "Report cannot be represented as valid UTF-8 without loss");
    const reportSha256 = sha256(reportBytes);
    const reportCharacterLength = Array.from(report).length;

    return this._queue(async () => {
      this._assertWritable();
      let receipt = copy(this.receipts.get(initialReceipt.requestId));
      if (!receipt || receipt.receiptId !== receiptId) throw brokerError("NOT_FOUND", "Receipt was not found");
      const currentCaller = await this._verifiedCaller(credential);
      const endpoints = await this._endpointMap();
      const recipient = this._callerEndpoint(currentCaller, endpoints, receipt.projectId);
      if (recipient.endpointId !== receipt.recipientEndpointId ||
          endpointFingerprint(recipient) !== receipt.recipientEndpointFingerprint ||
          !recipient.capabilities.receiveTask || !recipient.capabilities.readReport)
        throw brokerError("FORBIDDEN", "Only the authorized receiving endpoint may complete a report");
      await this._authorized(currentCaller, "complete", {
        projectId: receipt.projectId, taskId: receipt.taskId,
        senderEndpointId: receipt.senderEndpointId, recipientEndpointId: receipt.recipientEndpointId,
        ...sessionScope(receipt),
      });
      if (receipt.status === "COMPLETE") {
        if (receipt.reportSha256 === reportSha256) return receipt;
        throw brokerError("COMPLETION_CONFLICT", "Receipt already has a different complete report");
      }
      if (!["RUNNING", "UNKNOWN"].includes(receipt.status)) throw brokerError("INVALID_STATE", "Only running or unknown deliveries can complete");
      const reportRef = crypto.randomUUID();
      this._storeReport(reportSha256, reportBytes);
      const finalCaller = await this._verifiedCaller(credential);
      const finalEndpoints = await this._endpointMap();
      const finalRecipient = this._callerEndpoint(finalCaller, finalEndpoints, receipt.projectId);
      if (finalRecipient.endpointId !== receipt.recipientEndpointId ||
          endpointFingerprint(finalRecipient) !== receipt.recipientEndpointFingerprint || !finalRecipient.capabilities.readReport)
        throw brokerError("FORBIDDEN", "Receiving endpoint changed before report commit");
      await this._authorized(finalCaller, "complete", {
        projectId: receipt.projectId, taskId: receipt.taskId,
        senderEndpointId: receipt.senderEndpointId, recipientEndpointId: receipt.recipientEndpointId,
        ...sessionScope(receipt),
      });
      receipt.status = "COMPLETE";
      delete receipt.failureCode;
      receipt.reportRef = reportRef;
      receipt.reportSha256 = reportSha256;
      receipt.reportByteLength = reportBytes.length;
      receipt.reportCharacterLength = reportCharacterLength;
      receipt.reportIntegrity = "VERIFIED";
      receipt.completedAt = new Date().toISOString();
      this._append("completed", receipt);
      return copy(receipt);
    });
  }

  _storeReport(hash, bytes) {
    this._assertWritable();
    const finalPath = path.join(this.reportDir, hash + ".bin");
    if (fs.existsSync(finalPath)) {
      const existing = fs.readFileSync(finalPath);
      if (sha256(existing) !== hash || !existing.equals(bytes)) throw brokerError("REPORT_STORAGE", "Existing content-addressed report failed integrity check");
      return;
    }
    const tempPath = path.join(this.reportDir, "." + crypto.randomUUID() + ".tmp");
    const fd = fs.openSync(tempPath, "wx", 0o600);
    try {
      let offset = 0;
      while (offset < bytes.length) offset += fs.writeSync(fd, bytes, offset, bytes.length - offset);
      fs.fsyncSync(fd);
    } finally { fs.closeSync(fd); }
    try { fs.renameSync(tempPath, finalPath); }
    catch (error) { try { fs.unlinkSync(tempPath); } catch {} throw error; }
  }

  async readReport(credential, reportRef) {
    this._assertOpen();
    reportRef = requireUuid(reportRef, "reportRef");
    const caller = await this._verifiedCaller(credential);
    const requestId = this.reportRefs.get(reportRef);
    if (!requestId) throw brokerError("NOT_FOUND", "Report reference was not found");
    const receipt = this.receipts.get(requestId);
    if (!receipt || receipt.status !== "COMPLETE" || receipt.reportRef !== reportRef) throw brokerError("REPORT_STATE", "Report is not complete");
    const endpoints = await this._endpointMap();
    const endpoint = this._callerEndpoint(caller, endpoints, receipt.projectId);
    if (![receipt.senderEndpointId, receipt.recipientEndpointId].includes(caller.endpointId))
      throw brokerError("FORBIDDEN", "Caller is outside the report participants");
    const expectedFingerprint = caller.endpointId === receipt.senderEndpointId
      ? receipt.senderEndpointFingerprint : receipt.recipientEndpointFingerprint;
    if (!endpoint.capabilities.readReport || endpointFingerprint(endpoint) !== expectedFingerprint)
      throw brokerError("FORBIDDEN", "Caller endpoint identity or report capability changed");
    await this._authorized(caller, "readReport", {
      projectId: receipt.projectId, taskId: receipt.taskId,
      senderEndpointId: receipt.senderEndpointId, recipientEndpointId: receipt.recipientEndpointId,
      ...sessionScope(receipt),
    });
    const bytes = fs.readFileSync(path.join(this.reportDir, receipt.reportSha256 + ".bin"));
    if (bytes.length !== receipt.reportByteLength || sha256(bytes) !== receipt.reportSha256)
      throw brokerError("REPORT_INTEGRITY", "Stored report SHA256 or byte length does not match its receipt");
    const report = bytes.toString("utf8");
    if (!Buffer.from(report, "utf8").equals(bytes) || Array.from(report).length !== receipt.reportCharacterLength)
      throw brokerError("REPORT_INTEGRITY", "Stored report is not the complete recorded UTF-8 body");
    return {
      receiptId: receipt.receiptId, reportRef, report,
      sha256: receipt.reportSha256, byteLength: bytes.length,
      characterLength: Array.from(report).length, integrity: "VERIFIED",
    };
  }

  async close() {
    await this.serial.catch(() => {});
    if (this.closed) return;
    this.closed = true;
    if (this.lockFd !== null) {
      try { fs.closeSync(this.lockFd); } catch {}
      this.lockFd = null;
      try {
        const lock = JSON.parse(fs.readFileSync(this.lockPath, "utf8"));
        if (lock.token === this.lockToken) fs.unlinkSync(this.lockPath);
      } catch {}
    }
  }
}

module.exports = { GenericEndpointBroker, UUID_RE, STATUS, MAX_MESSAGE_BYTES, MAX_REPORT_BYTES };
