"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const { EventEmitter } = require("node:events");
const { accountHomes, accountLaunchSpec, normalizeStatus, readCodexAccountStatus, usageLabel } = require("../../AgentEngine/codex-account-status");

test("Codex account homes enumerate exactly five named isolated directories", () => {
  const result = accountHomes("C:/Users/test", { statSync: (directory) => {
    if (directory.endsWith(".codex-account2") || directory.endsWith(".codex-account5")) return { isDirectory: () => true };
    throw new Error("missing");
  } });
  assert.deepEqual(result.map(x => x.id), ["codex-account1", "codex-account2", "codex-account3", "codex-account4", "codex-account5"]);
  assert.deepEqual(result.filter(x => x.exists).map(x => x.number), [2, 5]);
});

test("each Codex account launches with its own home and file-backed CLI auth without a login command", () => {
  const account = { id: "codex-account5", directory: "C:/Users/test/.codex-account5" };
  const spec = accountLaunchSpec(account, "codex.exe");
  assert.deepEqual(spec.cmd, ["codex.exe", "-c", 'cli_auth_credentials_store="file"']);
  assert.equal(spec.env.CODEX_HOME, account.directory);
  assert.equal(spec.env.OPENAI_API_KEY, null);
  assert.equal(spec.cmd.includes("login"), false);
  assert.throws(() => accountLaunchSpec({ ...account, id: "default" }), /named Codex account/);
});

test("Codex account metadata and rate limits normalize without token data", () => {
  const status = normalizeStatus(
    { account: { type: "chatgpt", email: "one@example.test", planType: "pro" } },
    { rateLimitsByLimitId: { codex: {
      primary: { usedPercent: 20, windowDurationMins: 300, resetsAt: 1900000000 },
      secondary: { usedPercent: 65, windowDurationMins: 10080 },
    } } },
    "2026-10-18T20:00:00.000Z",
  );
  assert.equal(status.email, "one@example.test");
  assert.equal(status.planType, "pro");
  assert.equal(status.windows[0].remainingPercent, 80);
  assert.equal(status.windows[1].remainingPercent, 35);
  assert.equal(usageLabel(status), "5h 80% left · 7d 35% left");
  assert.equal(JSON.stringify(status).includes("access_token"), false);
});

test("read-only app-server query uses only the selected CODEX_HOME", async () => {
  let observedEnv, sent = [];
  const spawn = (command, args, options) => {
    assert.match(command, /codex .*app-server/);
    assert.deepEqual(args, []);
    assert.equal(options.shell, true);
    observedEnv = options.env;
    const child = new EventEmitter(); child.exitCode = null;
    child.stdout = new EventEmitter(); child.stderr = new EventEmitter();
    child.stdin = { write(line) {
      const request = JSON.parse(line); sent.push(request);
      queueMicrotask(() => {
        if (request.id === 1) child.stdout.emit("data", Buffer.from('{"id":1,"result":{}}\n'));
        if (request.id === 2) child.stdout.emit("data", Buffer.from('{"id":2,"result":{"account":{"email":"two@example.test","planType":"plus"}}}\n'));
        if (request.id === 3) child.stdout.emit("data", Buffer.from('{"id":3,"result":{"rateLimits":{"primary":{"usedPercent":40,"windowDurationMins":300}}}}\n'));
      });
      return true;
    } };
    child.kill = () => { child.exitCode = 0; return true; };
    return child;
  };
  const result = await readCodexAccountStatus("C:/Users/test/.codex-account2", { spawn, platform: "win32", env: { PATH: "test" } });
  assert.equal(observedEnv.CODEX_HOME, "C:/Users/test/.codex-account2");
  assert.deepEqual(sent.map(x => x.method), ["initialize", "initialized", "account/read", "account/rateLimits/read"]);
  assert.equal(result.email, "two@example.test");
  assert.equal(result.windows[0].remainingPercent, 60);
});
