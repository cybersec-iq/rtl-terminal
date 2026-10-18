"use strict";

const childProcess = require("child_process");

const PROFILE_COUNT = 5;
const MAX_LINE_BYTES = 1024 * 1024;

function accountHomes(home, fsImpl = require("fs")) {
  return Array.from({ length: PROFILE_COUNT }, (_, i) => {
    const number = i + 1;
    const id = "codex-account" + number;
    const directory = require("path").join(home, "." + id);
    let exists = false;
    try { exists = fsImpl.statSync(directory).isDirectory(); } catch {}
    return { id, number, label: "Account " + number, directory, exists };
  });
}

function accountLaunchSpec(account, executable = "codex") {
  if (!account || !/^codex-account[1-5]$/.test(account.id) || !account.directory)
    throw new Error("A named Codex account profile is required");
  const env = { CODEX_HOME: account.directory };
  for (const name of ["OPENAI_API_KEY", "OPENAI_BASE_URL", "CODEX_API_KEY", "CODEX_AUTH_TOKEN"]) env[name] = null;
  return { cmd: [executable, "-c", 'cli_auth_credentials_store="file"'], env };
}

function normalizeStatus(accountResult, limitsResult, checkedAt = new Date().toISOString()) {
  const account = accountResult && (accountResult.account || accountResult);
  const email = account && typeof account.email === "string" ? account.email : null;
  const planType = account && typeof account.planType === "string" ? account.planType : null;
  const root = limitsResult && (limitsResult.rateLimits || limitsResult);
  const byId = limitsResult && limitsResult.rateLimitsByLimitId;
  const codexLimits = byId && byId.codex || root;
  const windows = [];
  if (codexLimits && typeof codexLimits === "object") {
    for (const key of ["primary", "secondary"]) {
      const bucket = codexLimits[key];
      if (!bucket || typeof bucket !== "object") continue;
      const usedPercent = Number.isFinite(bucket.usedPercent) ? bucket.usedPercent : null;
      const duration = Number.isFinite(bucket.windowDurationMins) ? bucket.windowDurationMins : null;
      const resetsAt = Number.isFinite(bucket.resetsAt) ? new Date(bucket.resetsAt * 1000).toISOString() : null;
      windows.push({ kind: key, usedPercent, remainingPercent: usedPercent == null ? null : Math.max(0, 100 - usedPercent), durationMinutes: duration, resetsAt });
    }
  }
  const ok = !!(accountResult || limitsResult);
  return { ok, email, planType, windows, checkedAt,
    error: !ok ? "Codex app-server did not return account or rate-limit data" : null };
}

/** Read account identity and quota using Codex's local app-server protocol; no credential files are opened. */
function readCodexAccountStatus(home, options = {}) {
  const spawn = options.spawn || childProcess.spawn;
  const platform = options.platform || process.platform;
  const baseEnv = options.env || process.env;
  const timeoutMs = options.timeoutMs || 12000;
  return new Promise((resolve) => {
    let child, settled = false, buffer = "", timer;
    let initialized = false, account = null, limits = null;
    const finish = (value) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      if (child && child.exitCode == null) {
        try {
          if (typeof options.killTree === "function") options.killTree(child.pid);
          else if (platform === "win32" && child.pid) childProcess.spawnSync("taskkill", ["/T", "/F", "/PID", String(child.pid)], { windowsHide: true, stdio: "ignore", timeout: 8000 });
          else child.kill();
        } catch { try { child.kill(); } catch {} }
      }
      resolve(value);
    };
    const send = (message) => {
      try { child.stdin.write(JSON.stringify(message) + "\n"); }
      catch { finish({ ok: false, email: null, planType: null, windows: [], checkedAt: new Date().toISOString(), error: "Codex app-server input unavailable" }); }
    };
    const completeIfReady = () => {
      if (initialized && account && limits) finish(normalizeStatus(account.result, limits.result));
    };
    const onLine = (line) => {
      if (!line.trim()) return;
      let message;
      try { message = JSON.parse(line); } catch { return; }
      if (message.id === 1) {
        if (message.error) { finish({ ok: false, email: null, planType: null, windows: [], checkedAt: new Date().toISOString(), error: "Codex app-server initialization failed" }); return; }
        initialized = true;
        send({ method: "initialized" });
        send({ method: "account/read", id: 2, params: {} });
        send({ method: "account/rateLimits/read", id: 3 });
      } else if (message.id === 2) {
        account = message.error ? { result: null } : { result: message.result };
        completeIfReady();
      } else if (message.id === 3) {
        limits = message.error ? { result: null } : { result: message.result };
        completeIfReady();
      }
    };
    try {
      const env = Object.assign({}, baseEnv, { CODEX_HOME: home });
      for (const name of ["OPENAI_API_KEY", "OPENAI_BASE_URL", "CODEX_API_KEY", "CODEX_AUTH_TOKEN"]) {
        for (const key of Object.keys(env)) if (key.toLowerCase() === name.toLowerCase()) delete env[key];
      }
      // The value is constant; shell mode is needed for the npm-installed Windows codex.cmd shim.
      child = spawn('codex -c cli_auth_credentials_store="file" app-server --listen stdio://', [], {
        shell: platform === "win32", windowsHide: true,
        stdio: ["pipe", "pipe", "ignore"], env,
      });
      child.stdout.on("data", (data) => {
        buffer += data.toString("utf8");
        if (Buffer.byteLength(buffer, "utf8") > MAX_LINE_BYTES) { finish({ ok: false, email: null, planType: null, windows: [], checkedAt: new Date().toISOString(), error: "Codex app-server response exceeded size limit" }); return; }
        let newline;
        while ((newline = buffer.indexOf("\n")) >= 0) { const line = buffer.slice(0, newline); buffer = buffer.slice(newline + 1); onLine(line); }
      });
      child.once("error", () => finish({ ok: false, email: null, planType: null, windows: [], checkedAt: new Date().toISOString(), error: "Could not start Codex app-server" }));
      child.once("close", () => { if (!settled) finish(normalizeStatus(account && account.result, limits && limits.result)); });
      timer = setTimeout(() => finish({ ok: false, email: null, planType: null, windows: [], checkedAt: new Date().toISOString(), error: "Codex account status timed out" }), timeoutMs);
      send({ method: "initialize", id: 1, params: { clientInfo: { name: "rtl_terminal", title: "Rtl Terminal", version: "1" } } });
    } catch {
      finish({ ok: false, email: null, planType: null, windows: [], checkedAt: new Date().toISOString(), error: "Could not start Codex app-server" });
    }
  });
}

function usageLabel(status) {
  if (!status || !status.ok) return "Usage unavailable";
  if (!status.windows.length) return "Usage unknown";
  return status.windows.map((w) => {
    const name = !w.durationMinutes ? w.kind : w.durationMinutes % 1440 === 0
      ? `${w.durationMinutes / 1440}d` : w.durationMinutes % 60 === 0 ? `${w.durationMinutes / 60}h` : `${w.durationMinutes}m`;
    return `${name} ${w.remainingPercent == null ? "unknown" : Math.round(w.remainingPercent) + "% left"}`;
  }).join(" · ");
}

module.exports = { PROFILE_COUNT, accountHomes, accountLaunchSpec, normalizeStatus, readCodexAccountStatus, usageLabel };
