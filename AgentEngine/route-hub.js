"use strict";
// X Agent R6: router catalog and account/profile selection.
// Metadata only. Credentials remain in each provider's own login or environment.
const fs = require("fs");
const crypto = require("crypto");
const path = require("path");

const ROUTES = Object.freeze([
  { id: "fcm", name: "FCM", kind: "router", mode: "native", note: "existing free router" },
  { id: "fcc", name: "Free Claude Code", kind: "router", mode: "wrapper", note: "official fcc-opencode launcher" },
  { id: "omni", name: "OmniRoute", kind: "router", mode: "openai", note: "local proxy (default 127.0.0.1:20128/v1); configure a free-only route upstream" },
  { id: "openrouter", name: "OpenRouter", kind: "provider", mode: "openai", note: "select a :free model" },
  { id: "kira", name: "Kira AI", kind: "provider", mode: "openai", note: "configure model and API endpoint" },
  { id: "kiro", name: "Kiro", kind: "agent", mode: "native-agent", note: "uses its existing login" },
  { id: "custom", name: "Other OpenAI-compatible", kind: "provider", mode: "openai", note: "configure model and API endpoint" },
]);
const routeById = (id) => ROUTES.find((x) => x.id === id);
const fresh = () => ({ version: 1, selected: { route: "fcm", accountId: null }, accounts: [] });
const text = (v, max) => String(v == null ? "" : v).trim().slice(0, max || 180);
function load(file) {
  if (!fs.existsSync(file)) return fresh();
  const j = JSON.parse(fs.readFileSync(file, "utf8"));
  if (!j || j.version !== 1 || !Array.isArray(j.accounts) || !j.selected || !routeById(j.selected.route))
    throw new Error("Route profile file is invalid; original preserved");
  return j;
}
function save(file, j) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const tmp = file + "." + process.pid + ".tmp";
  fs.writeFileSync(tmp, JSON.stringify(j, null, 2) + "\n", { encoding: "utf8", flag: "wx" });
  try { fs.renameSync(tmp, file); } catch (e) { try { fs.unlinkSync(tmp); } catch {} throw e; }
}
function edit(file, mutator) { const j = load(file); mutator(j); save(file, j); return j; }
function addAccount(file, route, email) {
  if (!routeById(route)) throw new Error("Unknown route");
  email = text(email, 120);
  if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) throw new Error("Enter a valid account email");
  let added;
  edit(file, (j) => {
    if (j.accounts.some((a) => a.route === route && a.email.toLowerCase() === email.toLowerCase()))
      throw new Error("Email already registered for this route");
    added = { id: "a" + crypto.randomBytes(6).toString("hex"), route, email,
      model: "", endpoint: route === "openrouter" ? "https://openrouter.ai/api/v1" : route === "omni" ? "http://127.0.0.1:20128/v1" : route === "kira" ? "https://kiraai.vn/api/v1" : "",
      keyVar: "", createdAt: new Date().toISOString() };
    j.accounts.push(added);
  });
  return added;
}
function updateAccount(file, id, changes) {
  return edit(file, (j) => {
    const a = j.accounts.find((x) => x.id === id);
    if (!a) throw new Error("Account not found");
    for (const k of Object.keys(changes)) {
      if (!["model", "endpoint", "keyVar"].includes(k)) throw new Error("Unsupported account field");
      const v = text(changes[k], 300);
      if (k === "model" && v && !/^[A-Za-z0-9][A-Za-z0-9_.:/+@-]*$/.test(v)) throw new Error("Invalid model ID");
      if (k === "keyVar" && v && !/^[A-Z_][A-Z0-9_]*$/.test(v)) throw new Error("Key variable must look like EXAMPLE_API_KEY");
      if (k === "endpoint" && v) {
        let url; try { url = new URL(v); } catch { throw new Error("Invalid URL"); }
        if (!["https:", "http:"].includes(url.protocol) || url.username || url.password || url.search || url.hash)
          throw new Error("Endpoint must be a plain http(s) API URL without embedded credentials");
        if (url.protocol === "http:" && !["127.0.0.1", "localhost", "[::1]"].includes(url.hostname))
          throw new Error("Remote endpoint needs https");
      }
      if (k === "endpoint" && a.route === "openrouter" && v !== "https://openrouter.ai/api/v1")
        throw new Error("OpenRouter endpoint is fixed");
      a[k] = v;
    }
  });
}
function select(file, route, accountId) {
  if (!routeById(route)) throw new Error("Unknown route");
  return edit(file, (j) => {
    if (accountId && !j.accounts.some((a) => a.id === accountId && a.route === route))
      throw new Error("Selected account does not belong to this route");
    j.selected = { route, accountId: accountId || null };
  });
}
function removeAccount(file, id) {
  return edit(file, (j) => {
    if (!j.accounts.some((a) => a.id === id)) throw new Error("Account not found");
    j.accounts = j.accounts.filter((a) => a.id !== id);
    if (j.selected.accountId === id) j.selected = { route: "fcm", accountId: null };
  });
}
function selectedAccount(j) { return j.accounts.find((a) => a.id === j.selected.accountId && a.route === j.selected.route) || null; }
function quotaLabel(a) {
  const q = a && a.lastQuota;
  if (!q || !q.at) return "quota Unknown";
  const age = Date.now() - Date.parse(q.at);
  const stale = !Number.isFinite(age) || age > 3600000 ? " (STALE)" : "";
  const free = q.freeRemaining == null ? "free requests Unknown" : "free remaining " + q.freeRemaining +
    (q.freeLimit == null ? "" : "/" + q.freeLimit);
  const cap = q.creditRemaining == null ? "" : " · key spending limit left $" + q.creditRemaining.toFixed(2);
  const used = q.usedDaily == null ? "" : " · key used today $" + q.usedDaily.toFixed(2);
  return free + cap + used + " · " + q.provider + stale;
}
function saveQuotaSnapshot(file,id,q) {
  return edit(file,j=>{
    const a=j.accounts.find(x=>x.id===id); if (!a) throw new Error("Account not found");
    const num=v=>typeof v==="number"&&Number.isFinite(v)&&v>=0?v:null;
    a.lastQuota={at:new Date().toISOString(),provider:"OpenRouter",freeRemaining:num(q.freeRemaining),
      freeLimit:num(q.freeLimit),creditRemaining:num(q.creditRemaining),usedDaily:num(q.usedDaily)};
  });
}
// Official OpenRouter API, explicitly user-invoked, GET only, no LLM inference.
// Each API key may share account-level limits; do not assume extra Gmail = extra quota.
async function readOpenRouterQuota(a,env,fetchFn) {
  if (!a || a.route!=="openrouter") return {ok:false,why:"Only OpenRouter has an integrated quota readout"};
  const secret=a.keyVar&&(env||{})[a.keyVar];
  if (!secret) return {ok:false,why:"Key environment variable unavailable"};
  try {
    const res=await (fetchFn||fetch)("https://openrouter.ai/api/v1/key",{
      method:"GET",headers:{Authorization:"Bearer "+secret},signal:AbortSignal.timeout(8000)
    });
    if (!res.ok) return {ok:false,why:"OpenRouter usage HTTP "+res.status};
    const data=(await res.json()).data||{};
    const fr=data.free_model_daily_requests||{};
    const num=v=>typeof v==="number"&&Number.isFinite(v)&&v>=0?v:null;
    return {ok:true,freeRemaining:num(fr.remaining),freeLimit:num(fr.limit),
      creditRemaining:num(data.limit_remaining),usedDaily:num(data.usage_daily)};
  }catch {return{ok:false,why:"Official quota lookup timed out or was unavailable"};}
}
function readiness(route, a, env) {
  const r = routeById(route);
  if (!r) return { ready: false, why: "Unknown route" };
  if (r.mode === "native") return { ready: true, why: "FCM state is checked at launch" };
  if (r.mode === "wrapper") return { ready: true, why: "requires installed fcc-opencode" };
  if (r.mode === "native-agent") return { ready: true, why: "uses current Kiro login, not an isolated account" };
  if (!a) return { ready: false, why: "add/select an account first" };
  if (!a.endpoint || !a.model) return { ready: false, why: "API endpoint/model not configured" };
  if (route === "openrouter" && !a.model.endsWith(":free")) return { ready: false, why: "choose an OpenRouter :free model" };
  if (!a.keyVar) return { ready: false, why: "set an environment-variable name for this account key" };
  if (!Object.hasOwn(env || {}, a.keyVar) || !(env || {})[a.keyVar])
    return { ready: false, why: "key environment variable absent (value is never stored)" };
  return { ready: true, why: "API settings present; connection not yet verified" };
}
function openCodeOverlay(route, a) {
  if (!["omni", "openrouter", "kira", "custom"].includes(route) || !a)
    throw new Error("This route does not use the OpenCode API adapter");
  const providerId = "xag" + route + a.id;
  return {
    provider: {
      [providerId]: {
        npm: "@ai-sdk/openai-compatible",
        name: "X Agent " + (routeById(route) || {}).name,
        options: { baseURL: a.endpoint, apiKey: "{env:" + a.keyVar + "}" },
        models: { [a.model]: { name: a.model } }
      }
    },
    model: providerId + "/" + a.model
  };
}
/** Same process-local OpenCode adapter for the standalone TUI and the embedded terminal. */
function openCodeLaunchConfig(route, account) {
  const overlay = openCodeOverlay(route, account);
  return { $schema: "https://opencode.ai/config.json", provider: overlay.provider,
    model: overlay.model, small_model: overlay.model, permission: { edit: "ask", bash: "ask" } };
}
module.exports = { ROUTES, routeById, load, addAccount, updateAccount, select, removeAccount, selectedAccount, readiness, openCodeOverlay, openCodeLaunchConfig, quotaLabel, saveQuotaSnapshot, readOpenRouterQuota };
