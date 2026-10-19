const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { test } = require("node:test");

test("X Agents host plan validates and passes authorized project roots to Codex", () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "rtl-xagent-host-test-"));
  try {
    const bin = path.join(temp, "bin");
    const first = path.join(temp, "project-a");
    const second = path.join(temp, "project-b");
    const home = path.join(temp, "home");
    for (const dir of [bin, first, second, home]) fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(bin, "codex.exe"), "test executable placeholder");

    const engine = require("../../AgentEngine/xagent.js");
    const context = {
      root: path.resolve("AgentEngine"),
      entry: path.resolve("AgentEngine/xagent.js"),
      node: process.execPath,
      home,
      platform: "win32",
      env: { PATH: bin, APPDATA: temp, LOCALAPPDATA: temp },
      registry: { agents: [{ id: "codex", name: "Codex", cmd: ["codex"], host: { addDirFlag: "--add-dir" } }] },
    };
    const request = {
      agentId: "codex", cwd: first, projectRoots: [first, second],
      action: "launch", launchId: "test-launch-1",
    };
    const plan = engine.buildHostSpec(request, context);
    assert.equal(plan.cwd, fs.realpathSync(first));
    assert.deepEqual(plan.projectRoots, [fs.realpathSync(first), fs.realpathSync(second)]);
    assert.deepEqual(plan.args, ["--add-dir", fs.realpathSync(second)]);
    assert.equal(plan.multiRootStatus, "CLI_FLAG_CONFIGURED_NOT_AUTHORIZATION_PROOF");
    assert.throws(() => engine.buildHostSpec({ ...request, cwd: home }, context), /outside project scope/);
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
});

test("running X Agent manager reads atomically saved project roots for its next launch", () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "rtl-xagent-live-roots-"));
  const previousFile = process.env.XAGENT_PROJECT_PATHS_FILE;
  try {
    const original = path.join(temp, "original");
    const added = path.join(temp, "added");
    for (const dir of [original, added]) fs.mkdirSync(dir, { recursive: true });
    const manifestFile = path.join(temp, "run", "project-paths.json");
    fs.mkdirSync(path.dirname(manifestFile), { recursive: true });
    process.env.XAGENT_PROJECT_PATHS_FILE = manifestFile;
    fs.writeFileSync(manifestFile, JSON.stringify({ schemaVersion: 1, paths: [original] }));
    const engine = require("../../AgentEngine/xagent.js");
    assert.deepEqual(engine.currentProjectPaths(), [original]);
    const replacement = manifestFile + ".tmp";
    fs.writeFileSync(replacement, JSON.stringify({ schemaVersion: 1, paths: [original, added] }));
    fs.renameSync(replacement, manifestFile);
    assert.deepEqual(engine.currentProjectPaths(), [original, added]);
    assert.equal(engine.agentWorkingDirectory(), original);
  } finally {
    if (previousFile === undefined) delete process.env.XAGENT_PROJECT_PATHS_FILE;
    else process.env.XAGENT_PROJECT_PATHS_FILE = previousFile;
    fs.rmSync(temp, { recursive: true, force: true });
  }
});

test("X Agents host catalog includes the fifth existing Codex profile without opening credentials", () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "rtl-xagent-codex-profiles-"));
  try {
    const bin = path.join(temp, "bin"), home = path.join(temp, "home"), root = path.join(temp, "runtime");
    for (const dir of [bin, home, root]) fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(bin, "codex.exe"), "test executable placeholder");
    for (let n = 1; n <= 5; n++) fs.mkdirSync(path.join(home, `.codex-account${n}`));
    const engine = require("../../AgentEngine/xagent.js");
    const catalog = engine.hostCatalog({ root, home, platform: "win32", env: { PATH: bin, APPDATA: temp, LOCALAPPDATA: temp },
      registry: { agents: [{ id: "codex", name: "Codex", cmd: ["codex"], host: { profileMode: "codex-home", authEnv: ["OPENAI_API_KEY"] } }] } });
    const profiles = catalog.agents[0].profiles;
    assert.deepEqual(profiles.filter(x => x.id !== "default").map(x => x.id), ["codex-account1", "codex-account2", "codex-account3", "codex-account4", "codex-account5"]);
    assert.equal(profiles.find(x => x.id === "codex-account5").exists, true);
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
});

test("Coding Agents menu orders Codex, Claude Code, and Antigravity before other agents", () => {
  const engine = require("../../AgentEngine/xagent.js");
  const ordered = engine.orderCodingAgents([{ id: "agy" }, { id: "opencode" }, { id: "claude" }, { id: "codex" }]);
  assert.deepEqual(ordered.map(x => x.id), ["codex", "claude", "agy", "opencode"]);
});

test("embedded Rtl Terminal launches Coding Agents in the same terminal, never a Windows Terminal tab", () => {
  const engine = require("../../AgentEngine/xagent.js");
  assert.equal(engine.resolveLaunchMode("auto", { inRtlTerminal: true }), "same");
  assert.equal(engine.resolveLaunchMode("tab", { inRtlTerminal: true, inWindowsTerminal: true }), "same");
  assert.equal(engine.resolveLaunchMode("tab", { inWindowsTerminal: true }), "tab");
  assert.equal(engine.resolveLaunchMode("auto", {}), "same");
});

test("named CLI profiles clear inherited credential variables instead of passing the string null", () => {
  const engine = require("../../AgentEngine/xagent.js");
  const env = engine.mergeEnvironment({ Path: "base-path", OPENAI_API_KEY: "inherited-secret", Keep: "yes" },
    { CODEX_HOME: "C:/Users/test/.codex-account1", openai_api_key: null, Path: "profile-path" });
  assert.equal(env.CODEX_HOME, "C:/Users/test/.codex-account1");
  assert.equal(env.OPENAI_API_KEY, undefined);
  assert.equal(env.openai_api_key, undefined);
  assert.equal(env.Path, "profile-path");
  assert.equal(env.Keep, "yes");
});

test("per-session X Agents runtime uses the first saved folder as its working directory", () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "rtl-xagent-path-test-"));
  const folders = [path.join(temp, "one"), path.join(temp, "two")];
  for (const folder of folders) fs.mkdirSync(folder, { recursive: true });
  const previous = process.env.XAGENT_PROJECT_PATHS_B64;
  try {
    process.env.XAGENT_PROJECT_PATHS_B64 = Buffer.from(JSON.stringify(folders)).toString("base64");
    delete require.cache[require.resolve("../../AgentEngine/xagent.js")];
    const engine = require("../../AgentEngine/xagent.js");
    assert.equal(engine.agentWorkingDirectory(), folders[0]);
    assert.deepEqual(engine.projectRootsForAgent({ name: "Codex", host: { addDirFlag: "--add-dir" } }), ["--add-dir", folders[1]]);
  } finally {
    if (previous === undefined) delete process.env.XAGENT_PROJECT_PATHS_B64;
    else process.env.XAGENT_PROJECT_PATHS_B64 = previous;
    fs.rmSync(temp, { recursive: true, force: true });
  }
});

test("native provider sessions are durably keyed per project session, CLI, provider and profile", () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "rtl-xagent-session-state-"));
  try {
    const bin = path.join(temp, "bin"), cwd = path.join(temp, "project"), home = path.join(temp, "home"), root = path.join(temp, "runtime");
    for (const dir of [bin, cwd, home, root]) fs.mkdirSync(dir, { recursive: true });
    fs.mkdirSync(path.join(home, ".codex"));
    fs.writeFileSync(path.join(bin, "codex.exe"), "test executable placeholder");
    const engine = require("../../AgentEngine/xagent.js");
    const context = { root, entry: path.resolve("AgentEngine/xagent.js"), node: process.execPath, home, platform: "win32",
      env: { PATH: bin, APPDATA: temp, LOCALAPPDATA: temp },
      registry: { agents: [{ id: "codex", name: "Codex", cmd: ["codex"], host: { resumeArgs: ["resume", "{SESSION}"] } }] } };
    const common = { projectSessionId: "session-12345678", agentId: "codex", providerId: "native", profileId: "default" };
    const saved = engine.recordHostNativeSession({ ...common, nativeSessionId: "provider-chat-42" }, context);
    assert.equal(saved.provenance, "EXPLICIT_NATIVE_ADAPTER_RECORD");
    assert.equal(saved.existenceStatus, "NOT_VERIFIED_BY_LAUNCHER");
    assert.equal(engine.resolveHostNativeSession(common, context).nativeSessionId, "provider-chat-42");
    const resumed = engine.buildHostSpec({ ...common, cwd, action: "resume", launchId: "resume-1" }, context);
    assert.deepEqual(resumed.args, ["resume", "provider-chat-42"]);
    assert.throws(() => engine.buildHostSpec({ ...common, projectSessionId: "another-session-123", cwd, action: "resume" }, context), /No native session recorded/);
    assert.throws(() => engine.recordHostNativeSession({ ...common, nativeSessionId: "not valid" }, context), /Invalid provider-issued/);
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
});

test("switching providers stays on one project session and resumes each provider's own conversation", () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "rtl-xagent-provider-switch-"));
  try {
    const bin = path.join(temp, "bin"), cwd = path.join(temp, "project"), home = path.join(temp, "home"), root = path.join(temp, "runtime");
    for (const dir of [bin, cwd, home, root]) fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(bin, "codex.exe"), "codex placeholder");
    fs.writeFileSync(path.join(bin, "claude.exe"), "claude placeholder");
    const engine = require("../../AgentEngine/xagent.js");
    const context = { root, entry: path.resolve("AgentEngine/xagent.js"), node: process.execPath, home, platform: "win32",
      env: { PATH: bin, APPDATA: temp, LOCALAPPDATA: temp },
      registry: { agents: [
        { id: "codex", name: "Codex", cmd: ["codex"], host: { resumeArgs: ["resume", "{SESSION}"] } },
        { id: "claude", name: "Claude Code", cmd: ["claude"], host: { resumeArgs: ["--resume", "{SESSION}"] } },
      ] } };
    const shared = { projectSessionId: "same-project-session", providerId: "native", profileId: "default" };
    engine.recordHostNativeSession({ ...shared, agentId: "codex", nativeSessionId: "codex-chat-01" }, context);
    engine.recordHostNativeSession({ ...shared, agentId: "claude", nativeSessionId: "claude-chat-02" }, context);

    const codex = engine.buildHostSpec({ ...shared, agentId: "codex", cwd, projectRoots: [cwd], action: "resume", launchId: "switch-codex" }, context);
    const claude = engine.buildHostSpec({ ...shared, agentId: "claude", cwd, projectRoots: [cwd], action: "resume", launchId: "switch-claude" }, context);
    assert.equal(codex.projectSessionId, claude.projectSessionId);
    assert.equal(codex.cwd, claude.cwd);
    assert.deepEqual(codex.args, ["resume", "codex-chat-01"]);
    assert.deepEqual(claude.args, ["--resume", "claude-chat-02"]);
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
});

test("fallback handoff is explicit transcript text and never claims native continuation", () => {
  const engine = require("../../AgentEngine/xagent.js");
  const handoff = engine.buildContextHandoff([
    { role: "user", content: "Keep the original task." },
    { role: "assistant", content: "The provider-native session ID is unavailable." },
  ], { projectSessionName: "Feature work" });
  assert.match(handoff, /Session: Feature work/);
  assert.match(handoff, /This is an explicit transcript handoff, not a native provider session/);
  assert.match(handoff, /Keep the original task\./);
  assert.throws(() => engine.buildContextHandoff([{ role: "system", content: "not accepted" }]), /Invalid transcript message/);
});

test("interactive Claude launch creates once by stable project-session ID and resumes afterward", () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "rtl-xagent-interactive-session-"));
  try {
    const engine = require("../../AgentEngine/xagent.js");
    const options = { root: temp, projectSessionId: "project-session-1234" };
    const first = engine.projectSessionLaunchArgs("claude", "claude", ["--add-dir", path.join(temp, "extra")], options);
    assert.equal(first.session.state, "NATIVE_CREATE");
    assert.match(first.args[1], /^--session-id$/);
    assert.match(first.args[2], /^[0-9a-f-]{36}$/i);
    const second = engine.projectSessionLaunchArgs("claude", "claude", [], options);
    assert.equal(second.session.state, "NATIVE_RESUME");
    assert.deepEqual(second.args.slice(0, 2), ["claude", "--resume"]);
    assert.equal(second.args[2], first.args[2]);
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
});

test("CLIs without deterministic create adapters are marked handoff-only and expose the saved handoff file", () => {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "rtl-xagent-handoff-menu-"));
  try {
    const engine = require("../../AgentEngine/xagent.js");
    const projectSessionId = "project-session-5678";
    assert.equal(engine.projectSessionStatus("opencode", { root: temp, projectSessionId }).state, "HANDOFF_ONLY");
    const handoffFile = path.join(temp, "saved-transcript.md");
    fs.writeFileSync(handoffFile, "Conversation transcript\n");
    assert.deepEqual(engine.transcriptHandoffStatus({ root: temp, projectSessionId, handoffFile }),
      { available: true, path: handoffFile, reason: "TEXT_HANDOFF_ONLY" });
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
});
