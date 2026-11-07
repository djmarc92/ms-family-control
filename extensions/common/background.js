/* MS Family Control – Browser-Erweiterung (Chrome, Edge, Firefox)
 * Spricht nur mit der Windows-App auf diesem PC (127.0.0.1:47613). Keine Daten an Dritte.
 * Erfasst: Domain der aktiven Seite + Zeit. NICHT erfasst: Inhalte, Eingaben, vollständige URLs.
 */
const api = typeof browser !== 'undefined' ? browser : chrome;
const LOCAL = 'http://127.0.0.1:47613';
const UA = navigator.userAgent;
const BROWSER = /Edg\//.test(UA) ? 'edge' : /Firefox\//.test(UA) ? 'firefox' : 'chrome';
let policy = null, policyAt = 0;
let active = { domain: '', since: 0 };
let focused = true;

function hostOf(url) {
  try {
    const u = new URL(url);
    if (!/^https?:$/.test(u.protocol)) return '';
    return u.hostname.toLowerCase().replace(/^www\./, '');
  } catch (e) { return ''; }
}
function matches(host, domain) { return host === domain || host.endsWith('.' + domain); }

async function local(path, body) {
  const opt = { method: body ? 'POST' : 'GET', headers: { 'X-MSFC': 'ext' }, cache: 'no-store' };
  if (body) { opt.body = JSON.stringify(body); opt.headers['Content-Type'] = 'application/json'; }
  const r = await fetch(LOCAL + path, opt);
  return r.json();
}

async function loadPolicy(force) {
  if (!force && policy && Date.now() - policyAt < 15000) return policy;
  try {
    policy = await local('/ext/policy?b=' + BROWSER);
    policyAt = Date.now();
    api.storage.local.set({ policy, policyAt });
  } catch (e) {
    if (!policy) {
      const s = await api.storage.local.get(['policy']);
      policy = s.policy || null; // letzte bekannte Sperrliste gilt weiter
    }
  }
  return policy;
}

function isBlocked(host, p) {
  if (!host || !p || p.paused) return null;
  const now = Date.now();
  for (const g of (p.grants || [])) if (matches(host, g.domain) && new Date(g.until).getTime() > now) return null;
  for (const d of (p.blocked || [])) if (matches(host, d)) return d;
  return null;
}

const cache = new Map(); // host -> { t, r }
async function filterCheck(host) {
  const c = cache.get(host);
  if (c && Date.now() - c.t < 120000) return c.r;
  let r = null;
  try {
    const ctl = new AbortController(); const to = setTimeout(() => ctl.abort(), 2500);
    const res = await fetch(LOCAL + '/ext/check?b=' + BROWSER + '&h=' + encodeURIComponent(host), { headers: { 'X-MSFC': 'ext' }, cache: 'no-store', signal: ctl.signal });
    clearTimeout(to); r = await res.json();
  } catch (e) { return null; }
  if (cache.size > 2000) cache.clear();
  cache.set(host, { t: Date.now(), r });
  return r;
}

async function check(tabId, url) {
  const host = hostOf(url);
  if (!host) return;
  const p = await loadPolicy(false);
  let d = isBlocked(host, p), cat = '';
  if (!d && !(p && p.paused)) {
    const f = await filterCheck(host);
    if (f && f.blocked) { d = f.domain || host; cat = f.label || f.category || ''; }
  }
  if (d) {
    const target = api.runtime.getURL('blocked.html') + '?d=' + encodeURIComponent(d) + '&h=' + encodeURIComponent(host) + (cat ? '&c=' + encodeURIComponent(cat) : '');
    try { await api.tabs.update(tabId, { url: target }); } catch (e) {}
    try { await local('/ext/visit', { b: BROWSER, domain: host, sec: 0, blocked: d, category: cat }); } catch (e) {}
  }
}

/* Zeitmessung der aktiven Seite */
async function flush() {
  const d = active.domain, sec = active.since ? Math.round((Date.now() - active.since) / 1000) : 0;
  active.since = Date.now();
  let idle = 'active';
  try { idle = await api.idle.queryState(120); } catch (e) {}
  const count = d && focused && idle === 'active' ? Math.min(60, sec) : 0;
  try { await local('/ext/visit', { b: BROWSER, domain: d, sec: count }); } catch (e) {}
}
async function setActiveFromTab(tab) {
  await flush();
  active.domain = tab && tab.url ? hostOf(tab.url) : '';
  active.since = Date.now();
}
async function refreshActive() {
  try {
    const [tab] = await api.tabs.query({ active: true, lastFocusedWindow: true });
    await setActiveFromTab(tab);
  } catch (e) {}
}

api.webNavigation.onBeforeNavigate.addListener(d => { if (d.frameId === 0) check(d.tabId, d.url); });
api.tabs.onUpdated.addListener((id, info, tab) => { if (info.url) { check(id, info.url); if (tab.active) setActiveFromTab(tab); } });
api.tabs.onActivated.addListener(async ({ tabId }) => { try { const t = await api.tabs.get(tabId); check(tabId, t.url || ''); setActiveFromTab(t); } catch (e) {} });
api.windows.onFocusChanged.addListener(async w => { await flush(); focused = w !== api.windows.WINDOW_ID_NONE; if (focused) refreshActive(); });

api.alarms.create('tick', { periodInMinutes: 0.5 });
api.alarms.onAlarm.addListener(async () => {
  await loadPolicy(true);
  await flush();
  // Bereits offene, inzwischen gesperrte Tabs umleiten
  try { for (const t of await api.tabs.query({})) if (t.url && hostOf(t.url)) check(t.id, t.url); } catch (e) {}
});

/* Nachrichten von Sperrseite / Popup */
api.runtime.onMessage.addListener((msg, sender, reply) => {
  (async () => {
    try {
      if (msg.type === 'policy') { reply(await loadPolicy(true)); return; }
      if (msg.type === 'request') {
        const r = await local('/ext/request', { kind: 'site', domain: msg.domain, minutes: msg.minutes, reason: msg.reason, message: msg.message });
        reply(r); return;
      }
      reply({ ok: false });
    } catch (e) { reply({ ok: false, error: 'Die Familien-App auf diesem PC ist nicht erreichbar.' }); }
  })();
  return true;
});

loadPolicy(true).then(refreshActive);
