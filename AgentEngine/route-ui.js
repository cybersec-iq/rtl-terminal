"use strict";
// R6 router/account UX. Each profile stores only a label and env-var NAME, never a key or token.
const fs = require("fs");
const path = require("path");
const crypto = require("crypto");
const hub = require("./route-hub");

/** File-backed OpenCode provider selection, shared by the menu and the host PTY adapter.
 * The file contains an env-var REFERENCE, never the API key's value. A host session gets
 * its own overlay so another tab cannot change its provider while it is running.
 */
function prepareOpenCodeRouteLaunch(root, route, account, cmd, cwd, unique = false) {
  if (!hub.routeById(route) || !["omni", "openrouter", "kira", "custom"].includes(route) ||
      !account || account.route !== route || !/^a[0-9a-f]{12}$/i.test(account.id))
    throw new Error("Invalid OpenCode route/account selection");
  const overlay = hub.openCodeOverlay(route, account);
  const dir = path.join(root, "run", "router-overlays");
  fs.mkdirSync(dir, { recursive: true });
  const nonce = crypto.randomBytes(8).toString("hex");
  const cfg = path.join(dir, route + "-" + account.id + (unique ? "-host-" + nonce : "") + ".json");
  const tmp = cfg + "." + process.pid + "." + nonce + ".tmp";
  const minimal = hub.openCodeLaunchConfig(route, account);
  fs.writeFileSync(tmp, JSON.stringify(minimal, null, 2) + "\n", { encoding: "utf8", flag: "wx" });
  try { fs.renameSync(tmp, cfg); } catch (e) { try { fs.unlinkSync(tmp); } catch {} throw e; }
  return { key: "route-" + route + "-" + account.id,
    title: "OpenCode · " + hub.routeById(route).name + " · " + account.email,
    cmd: [cmd, "--model", overlay.model], cwd,
    env: { OPENCODE_CONFIG: cfg, OPENCODE_CONFIG_CONTENT: "" },
    ...(unique ? { overlayFile: cfg } : {}) };
}

function createRouteUi(api) {
  const file = path.join(api.root, "route-accounts.json");
  const state = () => hub.load(file);
  // Private, git-ignored installations; never replace the user's global OpenCode or CLI tools.
  const fccExe = path.join(api.root, "run", "vendor", "fcc", ".venv", "Scripts", "fcc-opencode.exe");
  const opencode2Exe = path.join(api.root, "run", "vendor", "opencode-v2", "node_modules", "@opencode", "cli-windows-x64", "bin", "opencode.exe");
  const fccInstalled = () => fs.existsSync(fccExe) && fs.existsSync(opencode2Exe);
  const omniExe = path.join(api.root, "run", "vendor", "omniroute", "node_modules", ".bin", "omniroute.cmd");
  const omniInstalled = () => fs.existsSync(omniExe);
  // OmniRoute npm ships CHANGEME. Require explicit consent before first run.
  const omniHasDefaultPassword = () => {
    const dataEnv = path.join(api.root, "run", "vendor", "omniroute-data", ".env");
    const bundledEnv = path.join(api.root, "run", "vendor", "omniroute", "node_modules", "omniroute", ".env");
    const config = fs.existsSync(dataEnv) ? dataEnv : bundledEnv;
    try { return /^INITIAL_PASSWORD\s*=\s*['"]?CHANGEME['"]?\s*$/m.test(fs.readFileSync(config, "utf8")); }
    catch { return false; }
  };
  const selectedLabel = (j) => {
    const r = hub.routeById(j.selected.route);
    const a = hub.selectedAccount(j);
    return r.name + (a ? " · " + a.email : "");
  };
  const show = (s) => api.ask(s + " [Enter]");
  const isAPI = (r) => r.mode === "openai";
  const dashboard = { omni: "http://127.0.0.1:20128/dashboard/providers",
    openrouter: "https://openrouter.ai/settings/keys", kira: "https://kiraai.vn/" };

  async function menu() {
    let j;
    try { j = state(); } catch (e) { await show("Route accounts file invalid: " + e.message); return; }
    const f = await api.fcmStatus();
    const items = [
      api.sep("MANUAL ROUTER SWITCH — no automatic account rotation"),
      { label: "Launch selected route → OpenCode", right: selectedLabel(j), rightColor: api.C.neon, action: launchSelected },
      { label: "Selected", right: selectedLabel(j), disabled: true },
      api.sep("ROUTERS / PROVIDERS / NATIVE AGENT"),
    ];
    for (const r of hub.ROUTES) {
      const accts = j.accounts.filter((a) => a.route === r.id).length;
      const ready = r.id === "fcm" ? (f.ok && f.running ? "RUNNING" : "stopped; auto-start on launch")
        : r.id === "fcc" ? (fccInstalled() ? "INSTALLED · PROVIDER NOT VERIFIED" : "requires FCC + isolated OpenCode 2")
        : r.id === "omni" ? (omniInstalled() ? "INSTALLED · UPSTREAM NOT VERIFIED · " + accts + " profile(s)" : "NOT INSTALLED")
        : r.id === "kiro" ? "own Kiro login (not multi-account)"
        : accts + " profile(s) · quota Unknown";
      items.push({ label: r.name, right: ready + (j.selected.route === r.id ? " · SELECTED" : ""),
        rightColor: j.selected.route === r.id ? api.C.neon : api.C.grey,
        action: () => detail(r.id) });
    }
    items.push(api.sep("ACCOUNT STORAGE / LIMITS"));
    items.push({ label: "Account identifiers", right: "local only · no passwords, OAuth tokens or API keys", disabled: true });
    items.push({ label: "Quota per account", right: "Unknown unless official per-account data is available", disabled: true });
    items.push({ label: "Cost policy", right: "OpenRouter :free only; other routes require manual confirmation", disabled: true });
    items.push(api.back());
    api.push({ title: "ROUTER HUB / ACCOUNTS", items,
      footer: "Choose a router + account manually. Selecting metadata does not log in or create extra quota." });
  }

  async function detail(id) {
    const r = hub.routeById(id), j = state();
    const accts = j.accounts.filter((a) => a.route === id);
    const items = [api.sep(r.name.toUpperCase())];
    if (!isAPI(r)) {
      items.push({ label: "Choose as active route", right: "manual selection only", action: async () => {
        hub.select(file, id, null); await show(r.name + " selected; no other routes or credentials changed.");
      }});
      if (r.id === "fcm") items.push({ label: "FCM account note", right: "current FCM router owns its provider credentials", action: () =>
        show("FCM has one existing router configuration. Separate Gmail labels here would NOT create independent FCM logins.") });
      if (r.id === "fcc") {
        items.push({ label: "FCC installation", right: "review official installer before installing", action: () =>
          show("Official: github.com/Alishahryar1/free-claude-code. Installer may change existing agents; not installed automatically.") });
        items.push({ label: "Open FCC Admin UI", right: "localhost:8082 · requires running FCC", action: () =>
          api.openWeb("http://127.0.0.1:8082/admin") });
      }
      if (r.id === "kiro") items.push({ label: "Kiro identity", right: "uses official Kiro CLI session", action: () =>
        show("Kiro runs via its existing login. X Agent does not switch Gmail credentials inside Kiro.") });
    } else {
      if (dashboard[id]) items.push({ label: "Open provider dashboard / login", right: dashboard[id],
        action: () => api.openWeb(dashboard[id]) });
      if (id === "omni") items.push({ label: "Start OmniRoute local server",
        right: omniInstalled() ? "manual only · local 127.0.0.1:20128 · no paid fallback" : "not installed",
        action: async () => {
          if (!omniInstalled()) return show("OmniRoute is not installed. No service started.");
          if (omniHasDefaultPassword()) {
            const ok = await api.ask("OmniRoute has an insecure default initial password. Start on loopback ONLY to change it immediately? (y/N)");
            if (!/^y$/i.test(ok || "")) return;
          }
          return api.startAgent({ key: "route-omni-server", title: "OmniRoute local server",
            cmd: [omniExe, "serve", "--port", "20128", "--no-open"], cwd: api.root,
            env: { DATA_DIR: path.join(api.root, "run", "vendor", "omniroute-data"),
              HOST: "127.0.0.1", API_HOST: "127.0.0.1",
              OMNIROUTE_SERVER_HOST: "127.0.0.1", OMNIROUTE_LOCAL_ENDPOINTS_ENABLED: "0" } });
        } });
      items.push({ label: "+ Add account/profile", right: "one email label, one model and key-variable per account", action: async () => {
        const email = await api.ask("Account email label (no token):");
        if (email === null || !email.trim()) return;
        try { const x = hub.addAccount(file, id, email); api.flash("Profile saved: " + x.email + " · Esc, then reopen this router"); }
        catch (e) { await show("Profile not added: " + e.message); }
      }});
      for (const a of accts) {
        const rd = hub.readiness(id, a, process.env);
        items.push({ label: a.email, right: (rd.ready ? "CONFIGURED, NOT PROBED" : rd.why) + " · " + hub.quotaLabel(a) +
          (j.selected.accountId === a.id ? " · SELECTED" : ""),
          rightColor: rd.ready ? api.C.green : api.C.yellow, action: () => account(id, a.id) });
      }
      items.push({ label: "No silent paid fallback", right: "model / fee policy verified by user per service", disabled: true });
    }
    items.push({ label: "Launch selected router", right: "does not change other Agent defaults", action: launchSelected });
    items.push(api.back());
    api.push({ title: r.name + " · router settings", items,
      footer: "Accounts are separate config labels. Auth independence requires distinct provider-issued keys/sessions." });
  }

  async function account(route, id) {
    const j = state(), a = j.accounts.find((x) => x.id === id && x.route === route);
    if (!a) { await show("Account no longer exists."); return; }
    const rd = hub.readiness(route, a, process.env);
    const opts = [
      { label: "Select this account", right: "switch manually", action: async () => {
        hub.select(file, route, id); api.flash("Selected account: " + a.email + " · Esc and reopen for refreshed label");
      }},
      { label: "Select and launch in new tab", right: "OpenCode · only if configured", action: async () => {
        hub.select(file, route, id); await launchSelected();
      }},
      { label: "Configure model ID", right: a.model || "not set", action: async () => {
        const v = await api.ask("Exact model ID from provider (OpenRouter needs :free):"); if (v === null) return;
        try { hub.updateAccount(file, id, { model: v }); api.flash("Model saved · Esc then reopen the account"); }
        catch (e) { await show(e.message); }
      }},
      { label: "Configure API base URL", right: a.endpoint || "not set", action: async () => {
        const v = await api.ask("Provider API base URL (https; localhost may use http):"); if (v === null) return;
        try { hub.updateAccount(file, id, { endpoint: v }); api.flash("Endpoint saved · Esc then reopen the account"); }
        catch (e) { await show(e.message); }
      }},
      { label: "Configure API key ENV VAR NAME", right: a.keyVar || "not set", action: async () => {
        const v = await api.ask("EXISTING environment variable NAME only (not secret value):"); if (v === null) return;
        try { hub.updateAccount(file, id, { keyVar: v }); api.flash("Key variable saved · Esc then reopen the account"); }
        catch (e) { await show(e.message); }
      }},
      { label: "Connection / quota", right: hub.quotaLabel(a),
        action: () => show("Stored email: " + a.email + " · " + hub.quotaLabel(a) + ". Quota varies by provider and account. No model calls on menu refresh.") },
      ...(route === "openrouter" ? [{ label: "Refresh official OpenRouter quota", right: "GET /api/v1/key · 0 model tokens", action: async () => {
        const q = await hub.readOpenRouterQuota(a, process.env);
        if (!q.ok) return show("Quota lookup: " + q.why);
        hub.saveQuotaSnapshot(file, id, q);
        const updated = hub.load(file).accounts.find(x => x.id === id);
        api.flash("Quota refreshed · Esc then reopen to view it");
      } }] : []),
      { label: "Remove this profile (metadata only)", right: "does not revoke provider credentials", action: async () => {
        const v = await api.ask("Remove metadata label " + a.email + "? (y/N)");
        if (!/^y$/i.test(v || "")) return;
        hub.removeAccount(file, id); api.flash("Profile metadata removed · Esc twice then reopen router");
      }},
      api.back(),
    ];
    api.push({ title: "PROFILE · " + a.email, items: opts,
      footer: "Secrets are never stored by X Agent; separate keys/env names per Gmail if your provider supports them." });
  }

  async function launchSelected() {
    const j = state(), route = j.selected.route, a = hub.selectedAccount(j);
    if (route === "fcm") return api.startFCM();
    if (route === "kiro") return api.startKiro();
    if (route === "fcc") {
      // The FCC launcher requires OpenCode 2; keep the existing global OpenCode 1 / FCM untouched.
      const cmd = fs.existsSync(fccExe) ? fccExe : await api.findExe("fcc-opencode");
      if (!cmd) return show("FCC not installed. Official: github.com/Alishahryar1/free-claude-code.");
      if (!fs.existsSync(opencode2Exe)) return show("FCC needs an isolated OpenCode 2 binary. Do not upgrade the existing global OpenCode.");
      const pk = Object.keys(process.env).find((k) => k.toLowerCase() === "path") || "Path";
      const launchPath = path.dirname(opencode2Exe) + path.delimiter + (process.env[pk] || "");
      return api.startAgent({ key: "route-fcc", title: "OpenCode 2 · Free Claude Code", cmd: [cmd],
        cwd: api.agentCwd(), env: { [pk]: launchPath } });
    }
    const rd = hub.readiness(route, a, process.env);
    if (!rd.ready) return show((hub.routeById(route) || {}).name + ": " + rd.why + ". Select profile and configure before launch.");
    // File-backed process override: OpenCode resolves {env:NAME} in OPENCODE_CONFIG FILES.
    // OPENCODE_CONFIG_CONTENT can skip interpolation, so never put a real key into inline JSON.
    // The overlay contains only endpoint/model and an environment variable NAME (no secret).
    if (route !== "openrouter") {
      const ok = await api.ask("Router may allow PAID models. Confirm this model+route is free-only in provider dashboard? (y/N)");
      if (!/^y$/i.test(ok || "")) return;
    }
    const cmd = await api.findExe("opencode");
    if (!cmd) return show("OpenCode executable not found.");
    return api.startAgent(prepareOpenCodeRouteLaunch(api.root, route, a, cmd, api.agentCwd()));
  }
  function summary() {
    try { return selectedLabel(state()); }
    catch { return "invalid profile file — review without overwriting"; }
  }
  return { menu, detail, summary, launchSelected, state };
}
module.exports = { createRouteUi, prepareOpenCodeRouteLaunch };
