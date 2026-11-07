/* MS Family Control – Control Center (Vanilla JS, keine Abhängigkeiten) */
(() => {
'use strict';
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const COLORS = { cyan: '#00e9ff', pink: '#f23ee9', violet: '#a36cff', gold: '#ffd36b', green: '#1cff72', orange: '#ff8a3d' };
const DT = { school: 'Schultag', weekend: 'Wochenende', holiday: 'Ferien / Feiertag' };
const REASONS = { school: 'Schule / Hausaufgaben', learn: 'Lernen', play: 'Spielen', friends: 'Mit Freunden', other: 'Sonstiges' };
const LOCKR = { time: 'Zeit ist um', bedtime: 'Bettzeit', early: 'Noch zu früh', manual: 'Von Eltern gesperrt', school: 'Schulzeit' };
const S = { limType: '', me: null, csrf: '', family: '', copyright: '', https: true, range: 'today', actFilter: 0, appChild: 0, dash: null, kids: [], timer: null, statsDays: 7, statsChild: 0 };

/* ---------- Icons (Inline-SVG) ---------- */
const sv = p => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${p}</svg>`;
const I = {
  home: sv('<path d="M3 11l9-8 9 8"/><path d="M5 10v10h5v-6h4v6h5V10"/>'),
  kids: sv('<circle cx="9" cy="8" r="3"/><circle cx="17" cy="9" r="2.4"/><path d="M3 20c0-3.3 2.7-6 6-6s6 2.7 6 6"/><path d="M15 15.5c.6-.3 1.3-.5 2-.5 2.5 0 4 1.8 4 4.5"/>'),
  stats: sv('<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>'),
  globe: sv('<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c3 3.5 3 14.5 0 18M12 3c-3 3.5-3 14.5 0 18"/>'),
  apps: sv('<rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/>'),
  clock: sv('<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>'),
  school: sv('<path d="M2 9l10-5 10 5-10 5z"/><path d="M6 11v5c3 2 9 2 12 0v-5"/>'),
  warn: sv('<path d="M12 3l10 18H2z"/><path d="M12 10v5M12 18v.5"/>'),
  gear: sv('<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>'),
  bell: sv('<path d="M6 8a6 6 0 1 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.9 1.9 0 0 0 3.4 0"/>'),
  pc: sv('<rect x="3" y="4" width="18" height="12" rx="2"/><path d="M8 20h8M12 16v4"/>'),
  shield: sv('<path d="M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z"/><path d="M9 12l2 2 4-4"/>'),
  hand: sv('<path d="M18 11V6a2 2 0 0 0-4 0v5M14 10V4a2 2 0 0 0-4 0v6M10 10.5V6a2 2 0 0 0-4 0v8"/><path d="M18 8a2 2 0 1 1 4 0v6a8 8 0 0 1-8 8h-2c-2.8 0-4.5-.9-6-2.4l-3.6-3.6a2 2 0 0 1 2.8-2.8L7 15"/>'),
  more: sv('<circle cx="5" cy="12" r="1.5"/><circle cx="12" cy="12" r="1.5"/><circle cx="19" cy="12" r="1.5"/>'),
  lock: sv('<rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>'),
  plus: sv('<path d="M12 5v14M5 12h14"/>'),
  trash: sv('<path d="M3 6h18M8 6V4h8v2M6 6l1 15h10l1-15"/>'),
  signal: sv('<path d="M5 12a7 7 0 0 1 14 0M8.5 12a3.5 3.5 0 0 1 7 0"/><circle cx="12" cy="12" r="1"/><path d="M2 12a10 10 0 0 1 20 0"/>'),
  logout: sv('<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9"/>'),
  mail: sv('<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M3 7l9 6 9-6"/>'),
  palette: sv('<path d="M12 3a9 9 0 1 0 0 18c1.1 0 1.6-.9 1.2-1.8-.5-1 .2-2.2 1.3-2.2H17a4 4 0 0 0 4-4c0-5.5-4-10-9-10z"/><circle cx="7.5" cy="11" r="1.2"/><circle cx="10" cy="7" r="1.2"/><circle cx="15" cy="7.5" r="1.2"/>'),
};
const NAV = [
  ['dashboard', 'Dashboard', 'home'], ['kids', 'Kinder-Profile', 'kids'], ['stats', 'Nutzungsstatistiken', 'stats'],
  ['sites', 'Webseiten-Sperren', 'globe'], ['apps', 'App-Sperren', 'apps'], ['limits', 'Zeitlimits', 'clock'],
  ['mode', 'Schulzeit / Ferienmodus', 'school'], ['requests', 'Anfragen', 'hand'], ['messages', 'Nachrichten', 'mail'], ['alerts', 'Warnungen & Berichte', 'warn'],
  ['designs', 'Designs (Kinder-PCs)', 'palette'],
  ['devices', 'Geräte & Download', 'pc'], ['settings', 'Einstellungen', 'gear'],
];

/* ---------- Helfer ---------- */
async function api(a, data, qs = '') {
  const opt = { credentials: 'same-origin', headers: { 'X-CSRF': S.csrf } };
  if (data instanceof FormData) { opt.method = 'POST'; opt.body = data; }
  else if (data !== undefined) { opt.method = 'POST'; opt.body = JSON.stringify(data); opt.headers['Content-Type'] = 'application/json'; }
  let r;
  try { r = await fetch('api.php?a=' + a + qs, opt); } catch (e) { throw new Error('Server nicht erreichbar'); }
  let j = {};
  try { j = await r.json(); } catch (e) { throw new Error('Ungültige Serverantwort (' + r.status + ')'); }
  if (r.status === 401 && a !== 'me') { S.me = null; renderLogin(); throw new Error('Bitte erneut anmelden'); }
  if (!r.ok || j.ok === false) throw new Error(j.error || ('Fehler ' + r.status));
  return j;
}
let toastT;
function toast(msg, bad) {
  const t = $('#toast'); t.textContent = msg; t.style.borderColor = bad ? 'var(--red)' : 'var(--cyan)';
  t.classList.add('show'); clearTimeout(toastT); toastT = setTimeout(() => t.classList.remove('show'), 3200);
}
const run = fn => async (...a) => { try { await fn(...a); } catch (e) { toast(e.message, true); } };
function dur(sec, short) {
  sec = Math.max(0, Math.round(sec || 0)); const m = Math.floor(sec / 60), h = Math.floor(m / 60);
  if (h) return `${h} h ${String(m % 60).padStart(2, '0')}${short ? '' : ' min'}`;
  return `${m} min`;
}
function minsTxt(m) { if (m >= 1440) return 'Unbegrenzt'; if (m === 0) return 'Kein PC'; return m % 60 ? (m >= 60 ? `${Math.floor(m / 60)} h ${m % 60}` : `${m} min`) : `${m / 60} h`; }
function ago(ts) {
  if (!ts) return '–'; const d = (Date.now() - new Date(ts.replace(' ', 'T')).getTime()) / 1000;
  if (d < 60) return 'Jetzt'; if (d < 3600) return `vor ${Math.floor(d / 60)} min`; if (d < 86400) return `vor ${dur(d)}`;
  return new Date(ts.replace(' ', 'T')).toLocaleString('de-DE', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
}
const fmtDT = ts => ts ? new Date(ts.replace(' ', 'T')).toLocaleString('de-DE', { day: '2-digit', month: '2-digit', year: '2-digit', hour: '2-digit', minute: '2-digit' }) : '–';
const fmtD = d => d ? new Date(d + 'T12:00').toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' }) : '';
const avatar = (c, i = 0) => c && c.avatar ? c.avatar : ['assets/img/avatars/anime-1.png', 'assets/img/avatars/anime-2.png', 'assets/img/avatars/anime-3.png', 'assets/img/avatars/emoji-robot.svg'][(c ? c.id : i) % 4];
const appIcon = n => { n = (n || '').toLowerCase(); for (const k of ['fortnite', 'minecraft', 'steam', 'discord', 'chrome', 'msedge', 'firefox', 'roblox', 'epic', 'tiktok', 'whatsapp']) if (n.includes(k)) return k === 'msedge' ? 'edge' : k; return ''; };
const appLetter = n => esc((n || '?').replace(/\.exe$/i, '').charAt(0).toUpperCase());
function debounce(fn, ms) { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); }; }

/* ---------- Modal ---------- */
function modal(html, onOpen, cls) {
  $('#modalBody').innerHTML = html; const m = $('#modal'); $('.modal-box', m).className = 'modal-box panel' + (cls ? ' ' + cls : ''); m.classList.add('open'); m.setAttribute('aria-hidden', 'false');
  if (onOpen) onOpen($('#modalBody'));
  const f = $('#modalBody input:not([type=hidden]),#modalBody textarea'); if (f && window.innerWidth > 850) f.focus();
}
function closeModal() { const m = $('#modal'); m.classList.remove('open'); $('.modal-box', m).className = 'modal-box panel'; m.setAttribute('aria-hidden', 'true'); $('#modalBody').innerHTML = ''; }
document.addEventListener('click', e => { if (e.target.id === 'modal' || e.target.closest('[data-close]')) closeModal(); });
document.addEventListener('keydown', e => { if (e.key === 'Escape') closeModal(); });
function confirmBox(text, okLabel = 'Ja', danger = true) {
  return new Promise(res => {
    modal(`<h2>Bitte bestätigen</h2><p>${text}</p><div class="btn-row"><button class="btn ${danger ? 'danger' : 'primary'}" id="cOk">${esc(okLabel)}</button><button class="btn" data-close>Abbrechen</button></div>`);
    $('#cOk').onclick = () => { closeModal(); res(true); };
  });
}

/* ---------- Login ---------- */
function renderLogin() {
  stopTimer();
  $('#root').innerHTML = `<div class="login-wrap"><div class="login-art"></div><form class="login-box panel" id="loginForm">
    <img src="assets/img/icon-192.png" alt="" width="78" style="filter:drop-shadow(0 0 14px #00e9ff)">
    <h1>Family Control Center</h1><p class="muted">PC-Nutzung &amp; Jugendschutz · ${esc(S.family)}</p>
    <label>E-Mail<input type="email" name="email" autocomplete="username" required></label>
    <label>Passwort<input type="password" name="password" autocomplete="current-password" required></label>
    <button class="btn primary wide" type="submit">Anmelden</button>
    <p class="copyright">${esc(S.copyright)}</p></form></div>`;
  $('#loginForm').onsubmit = run(async e => {
    e.preventDefault(); const f = new FormData(e.target);
    await api('login', { email: f.get('email'), password: f.get('password') });
    await boot();
  });
}

/* ---------- Grundgerüst ---------- */
function shell() {
  const navBtns = NAV.map(([k, l, ic]) => `<button class="nav-item" data-nav="${k}"><span class="nav-icon">${I[ic]}</span>${esc(l)}${k === 'requests' ? '<span class="nav-badge hidden" data-badge></span>' : k === 'apps' ? '<span class="nav-badge hidden" data-appbadge></span>' : ''}</button>`).join('');
  $('#root').innerHTML = `<div class="app-shell">
  <aside class="sidebar"><div class="hero-art"><img src="assets/img/anime-hero.png" alt=""></div>
    <div class="side-brand"><span class="brand-mark">◇</span><span>MSFC <small>NEON SYSTEM</small></span></div>
    <nav class="nav" aria-label="Hauptnavigation">${navBtns}</nav>
    <div class="side-city"></div><div class="sidebar-bottom">▰▰▱ <small>FAMILY OS / V${esc(S.version)}</small></div></aside>
  <main class="workspace">
    <header class="header"><div class="header-title"><span class="family-icon"><img src="assets/img/icon-192.png" alt=""></span><div><h1>Family Control Center</h1><p>PC-Nutzung &amp; Jugendschutz <span class="slashes">/////</span></p></div></div>
      <div class="header-actions"><button class="icon-btn" id="bellBtn" aria-label="Anfragen &amp; Warnungen">${I.bell}<i></i></button><button class="icon-btn" data-nav="settings" aria-label="Einstellungen">${I.gear}</button>
      <button class="account" id="accountBtn">◉ <span>${esc(S.me.name)}</span>⌄</button></div></header>
    <div id="view"></div>
    <footer class="footer"><span>MS FAMILY CONTROL ${esc(S.version)} · ${esc(S.family.toUpperCase())}</span><span class="copy">${esc(S.copyright)}</span></footer>
  </main></div>
  <nav class="tabbar" aria-label="Navigation">
    <button data-nav="dashboard">${I.home}<span>Übersicht</span></button><button data-nav="kids">${I.kids}<span>Kinder</span></button>
    <button data-nav="requests">${I.hand}<span>Anfragen</span><span class="nav-badge hidden" data-badge></span></button>
    <button data-nav="alerts">${I.warn}<span>Warnungen</span></button><button data-nav="more">${I.more}<span>Mehr</span></button></nav>`;
  $$('[data-nav]').forEach(b => b.addEventListener('click', () => { location.hash = b.dataset.nav; }));
  $('#bellBtn').onclick = () => { location.hash = (S.dash && S.dash.stats.pending) ? 'requests' : 'alerts'; };
  $('#accountBtn').onclick = () => modal(`<h2>${esc(S.me.name)}</h2><p class="muted">${esc(S.me.email)}</p><div class="btn-row">
    <button class="btn" id="pwBtn">${I.lock} Passwort ändern</button><button class="btn danger" id="logoutBtn">${I.logout} Abmelden</button></div>`, () => {
    $('#logoutBtn').onclick = run(async () => { await api('logout', {}); closeModal(); S.me = null; await boot(); });
    $('#pwBtn').onclick = () => modal(`<h2>Passwort ändern</h2><form class="modal-form" id="pwF"><label>Aktuelles Passwort<input type="password" name="old" required></label><label>Neues Passwort (mind. 8 Zeichen)<input type="password" name="new" minlength="8" required></label><button class="btn primary">Speichern</button></form>`, () => {
      $('#pwF').onsubmit = run(async e => { e.preventDefault(); const f = new FormData(e.target); await api('password', { old: f.get('old'), new: f.get('new') }); closeModal(); toast('Passwort geändert'); });
    });
  });
}
function setBadges(n, alerts) {
  $$('[data-badge]').forEach(b => { b.textContent = n; b.classList.toggle('hidden', !n); });
  const bell = $('#bellBtn'); if (bell) bell.classList.toggle('has', !!(n || alerts));
  if ('setAppBadge' in navigator) { try { n ? navigator.setAppBadge(n) : navigator.clearAppBadge(); } catch (e) {} }
}
function setAppBadge(n) { $$('[data-appbadge]').forEach(b => { b.textContent = n; b.classList.toggle('hidden', !n); }); }
function stopTimer() { if (S.timer) clearInterval(S.timer); S.timer = null; }
const VIEWS = {};
function route() {
  if (!S.me) return;
  const na = /^#newapp\/(\d+)\/(allow|block)$/.exec(location.hash);
  if (na) { run(async () => { const r = await api('app_decide', { app_id: +na[1], decision: na[2] }); toast(`${r.name}: ${na[2] === 'allow' ? 'erlaubt ✓' : 'gesperrt ⛔'}`); })().finally(() => { location.hash = 'apps'; }); return; }
  const v = (location.hash || '#dashboard').slice(1).split('?')[0] || 'dashboard';
  const view = VIEWS[v] ? v : 'dashboard';
  $$('[data-nav]').forEach(b => { b.classList.toggle('active', b.dataset.nav === view); });
  stopTimer(); closeModal();
  const el = $('#view'); el.innerHTML = '<div class="view"><div class="empty">Lädt …</div></div>';
  window.scrollTo(0, 0);
  run(VIEWS[view])(el);
}
window.addEventListener('hashchange', route);
function every(sec, fn) { stopTimer(); S.timer = setInterval(() => { if (!document.hidden && !$('#modal').classList.contains('open')) run(fn)(); }, sec * 1000); }

/* ---------- Diagramm ---------- */
function smooth(pts) {
  if (pts.length < 2) return '';
  let d = `M${pts[0][0]},${pts[0][1]}`;
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[i - 1] || pts[i], p1 = pts[i], p2 = pts[i + 1], p3 = pts[i + 2] || p2;
    const c1 = [p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6], c2 = [p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6];
    d += ` C${c1[0].toFixed(1)},${Math.min(200, c1[1]).toFixed(1)} ${c2[0].toFixed(1)},${Math.min(200, c2[1]).toFixed(1)} ${p2[0].toFixed(1)},${p2[1].toFixed(1)}`;
  }
  return d;
}
function chartSVG(chart, today) {
  let labels = chart.labels, series = chart.series.map(s => ({ ...s, values: [...s.values] }));
  if (today) { // nur sinnvollen Tagesbereich zeigen
    let first = 24, last = 0;
    series.forEach(s => s.values.forEach((v, i) => { if (v > 0) { first = Math.min(first, i); last = Math.max(last, i); } }));
    const from = Math.min(first, 7), to = Math.max(last, Math.min(23, Math.max(new Date().getHours(), 21)));
    labels = labels.slice(from, to + 1); series.forEach(s => s.values = s.values.slice(from, to + 1));
  }
  const W = 700, H = 200, L = 38, n = labels.length;
  const maxV = Math.max(1, ...series.flatMap(s => s.values));
  const unit = today ? 60 : 3600; // Min. pro Stunde bzw. Stunden pro Tag
  let top = Math.ceil(maxV / unit); if (today) top = Math.min(60, Math.max(15, Math.ceil(maxV / 60 / 15) * 15)); else top = Math.max(1, top);
  const max = top * unit;
  const x = i => L + (n === 1 ? (W - L) / 2 : i * (W - L - 6) / (n - 1)), y = v => H - (v / max) * (H - 12);
  let g = '';
  const steps = today ? 4 : Math.min(top, 5);
  for (let k = 0; k <= steps; k++) {
    const v = max * k / steps, yy = y(v);
    g += `<line x1="${L}" x2="${W}" y1="${yy}" y2="${yy}" stroke="#2a3f6e" stroke-width="1" stroke-dasharray="${k ? '3 4' : ''}"/><text x="${L - 6}" y="${yy + 4}" text-anchor="end">${today ? Math.round(v / 60) + 'm' : (Math.round(v / 360) / 10) + 'h'}</text>`;
  }
  const every_ = Math.ceil(n / (window.innerWidth < 600 ? 5 : 9));
  labels.forEach((l, i) => { if (i % every_ === 0 || i === n - 1) g += `<text x="${x(i)}" y="${H + 18}" text-anchor="middle">${esc(l)}</text>`; });
  let defs = '', paths = '';
  series.forEach((s, si) => {
    const c = COLORS[s.color] || COLORS.cyan, pts = s.values.map((v, i) => [x(i), y(v)]);
    defs += `<linearGradient id="gr${si}" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="${c}" stop-opacity=".45"/><stop offset="1" stop-color="${c}" stop-opacity="0"/></linearGradient>`;
    const d = smooth(pts);
    if (d) paths += `<path d="${d} L${x(n - 1)},${H} L${x(0)},${H}Z" fill="url(#gr${si})"/><path d="${d}" fill="none" stroke="${c}" stroke-width="3" style="filter:drop-shadow(0 0 5px ${c})"/>`;
    else if (pts.length) paths += `<circle cx="${pts[0][0]}" cy="${pts[0][1]}" r="4" fill="${c}"/>`;
  });
  let hits = '';
  labels.forEach((l, i) => {
    const tip = series.map(s => `${s.name}: ${dur(s.values[i])}`).join(' · ');
    hits += `<rect x="${x(i) - (W - L) / n / 2}" y="0" width="${(W - L) / n}" height="${H}" fill="transparent" data-tip="${esc(l + ' – ' + tip)}"/>`;
  });
  return `<svg class="chart-svg" viewBox="0 0 ${W} ${H + 24}" role="img" aria-label="Nutzungsdiagramm"><defs>${defs}</defs>${g}${paths}${hits}</svg>`;
}
const tipEl = document.createElement('div'); tipEl.className = 'chart-tip'; document.body.appendChild(tipEl);
document.addEventListener('pointermove', e => {
  const t = e.target.closest && e.target.closest('[data-tip]');
  if (!t) { tipEl.style.display = 'none'; return; }
  tipEl.textContent = t.dataset.tip; tipEl.style.display = 'block';
  tipEl.style.left = Math.min(e.clientX + 12, window.innerWidth - tipEl.offsetWidth - 8) + 'px'; tipEl.style.top = (e.clientY + 14) + 'px';
});
function legend(chart, today) {
  const total = chart.series.reduce((a, s) => a + s.total, 0);
  return chart.series.map(s => `<div><b class="dot" style="background:${COLORS[s.color]};box-shadow:0 0 7px ${COLORS[s.color]}"></b>${esc(s.name)} <strong>${dur(s.total)}</strong></div>`).join('') +
    `<div class="total">Gesamt (${today ? 'heute' : 'Zeitraum'})<strong>${dur(total)}</strong></div>`;
}

/* ---------- Kind-Bausteine ---------- */
function profileCard(c) {
  const st = c.state, remain = st.unlimited ? 'Unbegrenzt' : `noch ${dur(st.remaining_sec)}`;
  const status = c.online ? `<b class="online"></b>Aktiv (${esc(c.live && c.live.device || 'PC')})` : `<b></b>Offline${c.last_seen ? ' (' + ago(c.last_seen) + ')' : ''}`;
  return `<button class="profile ${esc(c.color)}" data-child="${c.id}"><img class="avatar" src="${esc(avatar(c))}" alt="">
    <div class="profile-data"><strong>${esc(c.name)}</strong><span class="status">${status}</span>
    <span class="pc-symbol">${st.lock_reason ? `<span class="lockchip">🔒 ${LOCKR[st.lock_reason]}</span>` : `<span class="remain">${remain}</span>`}</span></div>
    ${c.pending ? `<span class="pendchip">${c.pending} 🙋</span>` : ''}<span class="msgchip" role="button" tabindex="0" data-msgkid="${c.id}" title="Nachricht an ${esc(c.name)}" aria-label="Nachricht an ${esc(c.name)}">💬</span><span class="chevron">›</span></button>`;
}
function bindProfiles(root) {
  $$('[data-child]', root).forEach(b => b.onclick = () => childModal(+b.dataset.child));
  $$('[data-msgkid]', root).forEach(b => {
    b.onclick = e => { e.stopPropagation(); run(() => msgModal(+b.dataset.msgkid))(); };
    b.onkeydown = e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); e.stopPropagation(); b.click(); } };
  });
}
async function loadKids() { const j = await api('children'); S.kids = j.children; S.avatars = j.avatars; return j; }
async function childModal(id) {
  if (!S.kids.find(k => k.id === id)) await loadKids();
  const c = S.kids.find(k => k.id === id) || (S.dash && S.dash.children.find(k => k.id === id)); if (!c) return;
  const st = c.state, live = c.live || {};
  modal(`<div style="display:flex;gap:12px;align-items:center"><img src="${esc(avatar(c))}" alt="" style="width:64px;height:64px;border-radius:14px;object-fit:cover;border:2px solid ${COLORS[c.color]}">
    <div><h2 style="margin:0">${esc(c.name)}</h2><span class="muted">${esc(st.day_label)} · ${st.start}–${st.end} Uhr${nightNote(st)} · Limit ${minsTxt(st.limit_min)}${st.bonus_min ? ` (+${st.bonus_min} Bonus)` : ''}</span></div></div>
    <div class="grid3" style="margin:14px 0">
      <div class="stat cyan" style="min-height:0"><div><small>Heute genutzt</small><strong style="font-size:20px">${dur(st.used_sec)}</strong></div></div>
      <div class="stat violet" style="min-height:0"><div><small>Restzeit</small><strong style="font-size:20px">${st.unlimited ? '∞' : dur(st.remaining_sec)}</strong></div></div>
      <div class="stat ${st.lock_reason ? 'pink' : 'cyan'}" style="min-height:0"><div><small>Status</small><strong style="font-size:16px">${st.lock_reason ? '🔒 ' + LOCKR[st.lock_reason] : (c.online ? '🟢 Online' : '⚪ Offline')}</strong></div></div></div>
    <button class="btn primary wide msg-cta" id="msgKid">💬 Nachricht an ${esc(c.name)} schreiben</button>
    ${c.online && live.app_label ? `<p class="muted">Gerade: <b style="color:#fff">${esc(live.app_label)}</b>${live.domain ? ` · ${esc(live.domain)}` : ''}</p>` : ''}
    ${st.free_until && new Date(st.free_until.replace(' ', 'T')) > new Date() ? `<div class="note">Freigegeben ohne Einschränkung bis ${fmtDT(st.free_until)}</div>` : ''}
    ${st.lock_until && new Date(st.lock_until.replace(' ', 'T')) > new Date() ? `<div class="alert">Gesperrt bis ${fmtDT(st.lock_until)}</div>` : ''}
    <h3 style="font-size:14px;margin:14px 0 6px">Mehr Zeit für heute</h3><div class="btn-row">
      <button class="btn ok small" data-act="bonus" data-m="15">+15 min</button><button class="btn ok small" data-act="bonus" data-m="30">+30 min</button>
      <button class="btn ok small" data-act="bonus" data-m="60">+1 h</button><button class="btn small" data-act="bonus" data-m="-15">−15 min</button></div>
    <h3 style="font-size:14px;margin:14px 0 6px">Sperren / Freigeben</h3><div class="btn-row">
      <button class="btn danger small" data-act="lock" data-m="30">🔒 30 min</button><button class="btn danger small" data-act="lock" data-m="60">🔒 1 h</button>
      <button class="btn danger small" data-act="lock" data-tomorrow="1">🔒 Bis morgen</button><button class="btn small" data-act="unlock">🔓 Sperre aufheben</button>
      <button class="btn small" data-act="free" data-m="60">∞ 1 h ohne Limit</button><button class="btn small" data-act="unfree">Freigabe beenden</button></div>
    <div class="btn-row" style="margin-top:16px"><button class="btn" id="editKid">✎ Profil</button><a class="btn" href="#limits">${I.clock} Zeitregeln</a>
      <button class="btn" id="pairKid">${I.pc} Gerät koppeln</button></div>`, root => {
    $$('[data-act]', root).forEach(b => b.onclick = run(async () => {
      const r = await api('child_action', { child_id: c.id, action: b.dataset.act, minutes: +b.dataset.m || 30, until_tomorrow: !!b.dataset.tomorrow });
      toast(`${c.name}: ${r.message}`); closeModal(); route();
    }));
    $('#editKid').onclick = () => kidForm(c);
    $('#pairKid').onclick = () => pairModal(c.id);
    $('#msgKid').onclick = run(() => msgModal(c.id));
  });
}

/* ---------- Nachricht an ein Kind (Fenster im Dashboard-Design) ---------- */
const QUICK_MSG = ['Essen ist fertig! 🍝', 'Kommst du bitte kurz runter? 👋', 'Noch 10 Minuten, dann ist Schluss ⏰', 'Erst die Hausaufgaben 📚', 'Zeit fürs Bett – gute Nacht 🌙', 'Hab dich lieb ❤️'];
async function msgModal(id) {
  if (!S.kids.find(k => k.id === id)) await loadKids();
  const c = (S.dash && S.dash.children.find(k => k.id === id)) || S.kids.find(k => k.id === id); if (!c) return;
  const [th, ms] = await Promise.all([api('themes').catch(() => null), api('messages').catch(() => ({ messages: [] }))]);
  const cfg = th && (th.children.find(k => k.id === id) || {}).config;
  const theme = cfg ? (cfg.events.parent_message || cfg.theme) : 'anime';
  const ttxt = (cfg && cfg.texts.parent_message) || {};
  const title = ttxt.title || (th ? th.defaults.parent_message.title : 'Nachricht von Mama & Papa');
  const tname = th && th.catalog[theme] ? th.catalog[theme].name : theme;
  const seen = new Set(), hist = ms.messages.filter(m => +m.child_id === id && !seen.has(m.text + m.created_at) && seen.add(m.text + m.created_at)).slice(0, 4).reverse();
  const tick = m => m.read_at ? `<i class="rd">✓✓ gelesen ${fmtDT(m.read_at).split(', ').pop()}</i>` : m.delivered_at ? '<i>✓ zugestellt</i>' : '<i>⏳ wartet auf PC</i>';
  const online = !!c.online, me = (S.me && S.me.name) || 'Mama & Papa';
  modal(`<div class="msgc" style="--kc:${COLORS[c.color] || 'var(--cyan)'}">
    <div class="msgc-head"><div class="msgc-av"><img src="${esc(avatar(c))}" alt=""></div>
      <div><small>Nachricht an</small><h2>${esc(c.name)}</h2><span class="msgc-st ${online ? 'on' : ''}"><b></b>${online ? 'Online – kommt sofort an, auch im Spiel' : 'Offline – wird beim nächsten Start zugestellt'}</span></div>
      <span class="msgc-deco" aria-hidden="true">✉</span></div>
    ${hist.length ? `<div class="msgc-hist">${hist.map(m => `<div class="msgc-b ${m.priority === 'high' ? 'hi' : ''}"><p>${esc(m.text)}</p><small>${esc(m.sender)} · ${fmtDT(m.created_at)} ${tick(m)}</small></div>`).join('')}</div>` : ''}
    <form id="mcF" class="msgc-form">
      <div class="msgc-quick">${QUICK_MSG.map(q => `<button type="button" class="msgc-q">${esc(q)}</button>`).join('')}</div>
      <div class="msgc-input"><textarea name="t" rows="3" maxlength="600" required placeholder="Schreib ${esc(c.name)} etwas …"></textarea><span class="msgc-count">0 / 600</span></div>
      <div class="msgc-row"><label class="msgc-hi"><input type="checkbox" name="hi"><span>❗ Wichtig</span></label><span class="msgc-tip">Strg + Enter = senden</span>
        <button class="btn primary msgc-send" type="submit"><span>✉</span> Senden</button></div>
      <details class="msgc-pv" ${window.innerWidth > 850 ? 'open' : ''}><summary>👁 So sieht ${esc(c.name)} es auf dem PC <em>Design „${esc(tname)}“</em></summary><div class="msgc-frame"></div></details>
    </form></div>`, root => {
    const ta = $('textarea', root), cnt = $('.msgc-count', root), hi = $('[name=hi]', root), fr = $('.msgc-frame', root), pv = $('.msgc-pv', root);
    const now = new Date().toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' });
    const draw = () => {
      if (!pv.open) return;
      fr.innerHTML = pvHTML(theme, 'parent_message', title, '', { msg: ta.value.trim() || 'Hier erscheint deine Nachricht …', high: hi.checked, accent: cfg && cfg.accent, scale: cfg && cfg.scale, custom: !!(ttxt.title || ttxt.text), child: c.name, from: me, time: 'Heute · ' + now + ' Uhr' });
      const p = $('.pv', fr); if (!p) return; p.style.transform = `translate(-50%,-50%) scale(${fr.clientWidth / 1600})`;
      const ttl = $('.pv-title', p); if (ttl && !p.classList.contains('pv-anime')) { const neon = p.classList.contains('pv-neon'), max = neon ? 136 : hi.checked ? 215 : 270; if (!neon) ttl.style.minHeight = max + 'px'; let fs = parseFloat(getComputedStyle(ttl).fontSize); while (ttl.scrollHeight > max + 4 && fs > 30) { fs *= 0.92; ttl.style.fontSize = fs + 'px'; } }
    };
    let raf = 0; const upd = () => { cnt.textContent = `${ta.value.length} / 600`; cnt.classList.toggle('warn', ta.value.length > 520); cancelAnimationFrame(raf); raf = requestAnimationFrame(draw); };
    ta.oninput = upd; hi.onchange = () => { root.querySelector('.msgc').classList.toggle('hi', hi.checked); upd(); };
    pv.ontoggle = draw;
    $$('.msgc-q', root).forEach(b => b.onclick = () => { ta.value = ta.value.trim() ? ta.value.trim() + ' ' + b.textContent : b.textContent; ta.focus(); upd(); });
    ta.onkeydown = e => { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); $('#mcF').requestSubmit(); } };
    const hl = $('.msgc-hist', root); if (hl) hl.scrollTop = hl.scrollHeight;
    $('#mcF').onsubmit = run(async e => {
      e.preventDefault(); const btn = $('.msgc-send', root); if (btn.disabled) return; btn.disabled = true;
      try {
        const r = await api('message_send', { text: ta.value, priority: hi.checked ? 'high' : 'normal', child_ids: [c.id] });
        if (r.no_device && r.no_device.length) { toast(`${c.name} hat noch keinen gekoppelten PC`, true); btn.disabled = false; return; }
        $('.msgc', root).classList.add('sent'); btn.innerHTML = '✓ Gesendet';
        toast(`Nachricht an ${c.name} gesendet ✓`); setTimeout(closeModal, 1100);
      } catch (err) { btn.disabled = false; throw err; }
    });
    upd();
  }, 'wide msg-box');
}
function kidForm(c) {
  c = c || { id: 0, name: '', color: 'cyan', avatar: '' };
  let sel = c.avatar || '', col = c.color;
  const avs = S.avatars || [];
  modal(`<h2>${c.id ? 'Profil bearbeiten' : 'Kind hinzufügen'}</h2><form class="modal-form" id="kidF">
    <label>Name<input name="name" required maxlength="60" value="${esc(c.name)}"></label>
    <label>Farbe</label><div class="color-pick">${Object.keys(COLORS).map(k => `<button type="button" class="${k === col ? 'on' : ''}" style="--c:${COLORS[k]}" data-col="${k}" aria-label="${k}"></button>`).join('')}</div>
    <label>Profilbild</label><div class="av-pick">${avs.map(a => `<button type="button" data-av="${esc(a)}" class="${a === sel ? 'on' : ''}"><img src="${esc(a)}" alt=""></button>`).join('')}
      ${sel && !avs.includes(sel) ? `<button type="button" data-av="${esc(sel)}" class="on"><img src="${esc(sel)}" alt=""></button>` : ''}</div>
    <label>Eigenes Bild hochladen<input type="file" accept="image/*" id="avUp"></label>
    <div class="btn-row"><button class="btn primary">Speichern</button>${c.id ? '<button type="button" class="btn danger" id="kidDel">Profil löschen</button>' : ''}</div></form>`, root => {
    $$('[data-col]', root).forEach(b => b.onclick = () => { col = b.dataset.col; $$('[data-col]', root).forEach(x => x.classList.toggle('on', x === b)); });
    const bindAv = () => $$('[data-av]', root).forEach(b => b.onclick = () => { sel = b.dataset.av; $$('[data-av]', root).forEach(x => x.classList.toggle('on', x === b)); });
    bindAv();
    $('#avUp').onchange = run(async e => {
      const fd = new FormData(); fd.append('file', e.target.files[0]); const r = await api('avatar_upload', fd);
      sel = r.path; $('.av-pick', root).insertAdjacentHTML('beforeend', `<button type="button" data-av="${esc(r.path)}"><img src="${esc(r.path)}" alt=""></button>`); bindAv();
      $(`[data-av="${r.path}"]`, root).click(); toast('Bild hochgeladen');
    });
    $('#kidF').onsubmit = run(async e => { e.preventDefault(); await api('child_save', { id: c.id, name: new FormData(e.target).get('name'), color: col, avatar: sel }); closeModal(); toast('Gespeichert'); route(); });
    if (c.id) $('#kidDel').onclick = run(async () => {
      if (!await confirmBox(`Profil <b>${esc(c.name)}</b> mit allen Statistiken löschen? Gekoppelte Geräte werden entkoppelt.`, 'Endgültig löschen')) return;
      await api('child_delete', { id: c.id }); toast('Profil gelöscht'); route();
    });
  });
}
async function pairModal(childId) {
  if (!S.kids.length) await loadKids();
  if (!S.kids.length) { toast('Bitte zuerst ein Kinderprofil anlegen', true); return; }
  childId = childId || S.kids[0].id;
  const r = await api('pair_code', { child_id: childId });
  const kid = S.kids.find(k => k.id === childId);
  modal(`<h2>${I.pc} Gerät koppeln</h2><p class="muted">für <b style="color:var(--t-title,#fff)">${esc(kid ? kid.name : '')}</b> · gültig bis ${esc(r.expires)} Uhr</p>
    <div class="pair-code">${esc(r.code.slice(0, 4))}-${esc(r.code.slice(4))}</div>
    <div class="pair-tabs"><button class="btn small active" data-pt="win">🖥 Windows-PC</button><button class="btn small" data-pt="and">📱 Android-Handy</button></div>
    <div data-pp="win"><ol class="steps"><li><a href="downloads/MSFamilyControl-Setup.exe" download>MSFamilyControl-Setup.exe</a> auf dem Kinder-PC als Administrator starten.</li>
    <li>Server-Adresse eintragen: <code>${esc(r.server_url)}</code></li><li>Diesen Kopplungscode eingeben.</li>
    <li>Masterpasswort festlegen – damit lässt sich die App am PC beenden, entsperren oder deinstallieren.</li>
    <li>Browser-Erweiterung in Chrome, Edge und Firefox aktivieren (Assistent zeigt die Schritte).</li></ol>
    <p class="hint">Wichtig: Das Kind sollte ein <b>Standard-Benutzerkonto</b> ohne Administratorrechte nutzen.</p></div>
    <div data-pp="and" class="hidden"><ol class="steps"><li>Auf dem Kinder-Handy <a href="downloads/MSFamilyControl.apk" download>MSFamilyControl.apk</a> laden und installieren („Installation aus unbekannten Quellen“ einmal erlauben).</li>
    <li>App öffnen: Server-Adresse <code>${esc(r.server_url)}</code>, Masterpasswort und diesen Code eingeben.</li>
    <li>Der Assistent führt durch die Berechtigungen: Nutzungszugriff, Bedienungshilfe, Über anderen Apps, Geräteadministrator, Webfilter, Akku.</li>
    <li><b>Android 13+:</b> Die Bedienungshilfe ist bei selbst installierten Apps erst gesperrt – App-Info → ⋮ → <b>„Eingeschränkte Einstellungen zulassen“</b>. Der Assistent bringt dich direkt hin.</li></ol></div>`, root => {
    $$('[data-pt]', root).forEach(b => b.onclick = () => { $$('[data-pt]', root).forEach(x => x.classList.toggle('active', x === b)); $$('[data-pp]', root).forEach(p => p.classList.toggle('hidden', p.dataset.pp !== b.dataset.pt)); });
  });
}

/* ---------- Dashboard ---------- */
VIEWS.dashboard = async el => {
  const d = await api('dashboard', undefined, '&range=' + S.range); S.dash = d; S.kids = d.children;
  if (!S.avatars) loadKids().catch(() => {});
  setBadges(d.stats.pending, d.unseen); setAppBadge((d.new_apps || []).length);
  const k = d.children, st = d.stats;
  const trend = st.trend === null ? 'heute' : `${st.trend <= 0 ? '▼' : '▲'} ${st.trend > 0 ? '+' : ''}${st.trend} % zu gestern`;
  const spark = d.chart.series.length ? (() => { const sum = d.chart.labels.map((_, i) => d.chart.series.reduce((a, s) => a + s.values[i], 0)); const mx = Math.max(1, ...sum); return sum.map((v, i) => `${(i / Math.max(1, sum.length - 1) * 90).toFixed(1)},${(42 - v / mx * 40).toFixed(1)}`).join(' '); })() : '';
  el.innerHTML = `<div class="content-grid"><section class="center-column">
    ${(d.new_apps || []).length ? '<section class="panel newapps-panel" id="newApps"></section>' : ''}
    <div class="profiles">${k.length ? k.map(profileCard).join('') : `<div class="empty panel"><b>Noch keine Kinder angelegt</b>Lege zuerst ein Kinderprofil an und kopple danach den PC.<br><br><button class="btn primary" id="firstKid">${I.plus} Kind hinzufügen</button></div>`}</div>
    <div class="stats">
      <div class="stat cyan"><div class="stat-icon">${I.clock}</div><div><small>Heutige Nutzungszeit</small><strong>${dur(st.today_sec).replace(' min', ' <em>min</em>')}</strong><span class="${st.trend !== null && st.trend <= 0 ? 'success' : ''}">${trend}</span></div>${spark ? `<svg viewBox="0 0 95 46" class="spark"><polyline points="${spark}"/></svg>` : ''}</div>
      <div class="stat violet clickable" data-go="devices"><div class="stat-icon">${I.pc}</div><div><small>Aktive Geräte</small><strong>${st.devices_online}</strong><span>von ${st.devices_total} Geräten online</span></div></div>
      <div class="stat pink clickable" data-go="sites"><div class="stat-icon">${I.shield}</div><div><small>Gesperrte Seiten</small><strong>${st.sites}</strong><span>${st.sites_today ? '+' + st.sites_today + ' heute' : 'Sperrliste'}</span></div></div>
      <div class="stat gold clickable" data-go="${st.pending ? 'requests' : 'alerts'}"><div class="stat-icon">${I.warn}</div><div><small>${st.pending ? 'Offene Anfragen' : 'Warnungen'}</small><strong>${st.pending || st.warnings}</strong><span>${st.pending ? 'Jetzt entscheiden' : '(letzte 24 h)'}</span></div></div>
    </div>
    <section class="panel usage-panel"><div class="panel-heading"><h2><span class="accent">▥</span> PC-Nutzung pro Kind</h2><div class="segmented" id="rangeTabs">
      ${[['today', 'Heute'], ['7', '7 Tage'], ['30', '30 Tage']].map(([v, l]) => `<button data-r="${v}" class="${S.range === v ? 'selected' : ''}">${l}</button>`).join('')}</div></div>
      <div class="chart-area"><div class="chart-wrap" id="chartBox">${chartSVG(d.chart, S.range === 'today')}</div><div class="legend" id="legendBox">${legend(d.chart, S.range === 'today')}</div></div></section>
    <section class="panel activity-panel"><div class="panel-heading"><h2><span class="accent">◉</span> Live Aktivität</h2><div class="segmented" id="actTabs">
      <button data-f="0" class="${!S.actFilter ? 'selected' : ''}">Alle</button>${k.map(c => `<button data-f="${c.id}" class="${S.actFilter === c.id ? 'selected' : ''}">${esc(c.name)}</button>`).join('')}</div></div>
      <div class="activity-rows">${k.filter(c => !S.actFilter || c.id === S.actFilter).map(c => {
        const l = c.live || {}; const ic = appIcon(l.app);
        return `<div class="activity-row"><img src="${esc(avatar(c))}" alt=""><div><strong><b class="dot ${c.online ? 'green-bg' : 'muted-bg'}"></b>${esc(c.name)}</strong><small>${c.online ? (l.locked ? '🔒 ' + (LOCKR[l.reason] || 'Gesperrt') : (l.idle > 300 ? 'Inaktiv' : 'Aktiv') + (l.active_since ? ' seit ' + new Date(l.active_since).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' }) : '')) : 'Letzte Aktivität: ' + ago(c.last_seen)}</small></div>
          <div><b class="app-icon ${ic ? 'applogo-' + ic : ''}">${appLetter(l.app_label || l.app)}</b> ${c.online ? 'Aktives' : 'Letztes'} Programm: <strong>${esc(l.app_label || l.app || '–')}</strong></div>
          <div>◉ Webseite: <strong>${esc(l.domain || '–')}</strong></div><span>${c.online ? 'Jetzt' : ago(c.last_seen)}</span></div>`;
      }).join('') || '<div class="empty">Noch keine Aktivität – sobald ein PC gekoppelt ist, erscheint sie hier live.</div>'}</div></section>
  </section>
  <aside class="controls-column">
    <section class="panel control-panel" id="sitesPanel"></section>
    <section class="panel control-panel"><div class="panel-heading"><h2><span class="accent">◷</span> Zeitlimits</h2><span class="tech-mark">///</span></div>
      <div class="segmented lim-tabs" id="limTabs">${Object.entries({ school: 'Schultag', weekend: 'Wochenende', holiday: 'Ferien' }).map(([t, l]) => `<button data-lt="${t}" class="${(S.limType || d.today_key) === t ? 'selected' : ''}">${l}${d.today_key === t ? ' •' : ''}</button>`).join('')}</div>
      <div id="limRows"></div>
      <p class="small-note">• = gilt heute (${esc(d.today_type)}) · Bettzeiten unter <a href="#limits">Zeitlimits</a></p></section>
    <section class="panel control-panel" id="appsPanel"></section>
    <section class="panel control-panel"><div class="panel-heading"><h2><span class="accent">⚑</span> Schulzeit / Ferienmodus</h2><span class="tech-mark">///</span></div>
      <div class="mode-switch"><button data-mode="school" class="${d.mode === 'school' ? 'selected' : ''}">▤ &nbsp;Schulzeit</button><button data-mode="auto" class="${d.mode === 'auto' ? 'selected' : ''}">⟳ &nbsp;Auto</button><button data-mode="holiday" class="${d.mode === 'holiday' ? 'selected' : ''}">☼ &nbsp;Ferien</button></div>
      <p class="mode-info">▦ &nbsp;Heute: <b>${esc(d.today_type)}</b>${d.holiday_now ? ` · ${esc(d.holiday_now.name)} bis ${fmtD(d.holiday_now.end_date)}` : ''} · Unterricht Mo–Fr ${esc(d.school_from)}–${esc(d.school_to)}${d.school_lock ? ' (PC gesperrt)' : ''}</p></section>
  </aside></div>`;
  const fk = $('#firstKid'); if (fk) fk.onclick = () => { loadKids().then(() => kidForm()); };
  bindProfiles(el);
  $$('[data-go]', el).forEach(b => b.onclick = () => location.hash = b.dataset.go);
  $$('#rangeTabs button', el).forEach(b => b.onclick = run(async () => {
    S.range = b.dataset.r; $$('#rangeTabs button').forEach(x => x.classList.toggle('selected', x === b));
    const c = (await api('chart', undefined, '&range=' + S.range)).chart; $('#chartBox').innerHTML = chartSVG(c, S.range === 'today'); $('#legendBox').innerHTML = legend(c, S.range === 'today');
  }));
  $$('#actTabs button', el).forEach(b => b.onclick = () => { S.actFilter = +b.dataset.f; VIEWS.dashboard(el); });
  const limRows = () => {
    const t = S.limType || d.today_key;
    $('#limRows').innerHTML = k.map(c => { const m = c.rules[t].limit_min; return `<div class="limit-row ${esc(c.color)}"><img src="${esc(avatar(c))}" alt=""><label>${esc(c.name)}</label><input type="range" min="0" max="480" step="15" value="${Math.min(480, m)}" data-limit="${c.id}" aria-label="Tageslimit ${esc(c.name)}"><output>${m >= 1440 ? '<span class="unl">Unbegrenzt</span>' : minsTxt(m) + ' / Tag'}</output></div>`; }).join('') || '<p class="muted">Keine Kinder.</p>';
    $$('[data-limit]', el).forEach(r => {
      const out = r.nextElementSibling, c = k.find(x => x.id === +r.dataset.limit);
      const save = debounce(run(async () => { const res = await api('limit_quick', { child_id: c.id, limit_min: +r.value, day_type: t }); c.rules[t].limit_min = +r.value; toast(`${c.name}: ${DT[res.day_type]} → ${minsTxt(+r.value)}`); }), 500);
      r.oninput = () => { out.textContent = minsTxt(+r.value) + ' / Tag'; save(); };
    });
  };
  limRows();
  $$('#limTabs button', el).forEach(b => b.onclick = () => { S.limType = b.dataset.lt; $$('#limTabs button').forEach(x => x.classList.toggle('selected', x === b)); limRows(); });
  $$('[data-mode]', el).forEach(b => b.onclick = run(async () => { await api('mode_set', { mode: b.dataset.mode }); toast('Modus gespeichert'); VIEWS.dashboard(el); }));
  sitesPanel($('#sitesPanel'), 6); appsPanel($('#appsPanel'), true);
  if ($('#newApps')) newAppsPanel($('#newApps'), d.new_apps, () => VIEWS.dashboard(el));
  every(20, async () => { if ((location.hash || '#dashboard') === '#dashboard') await VIEWS.dashboard(el); });
};

async function sitesPanel(box, limit) {
  const j = await api('sites'); const list = limit ? j.sites.slice(0, limit) : j.sites;
  box.innerHTML = `<div class="panel-heading"><h2><span class="accent">◎</span> Webseiten sperren</h2><span class="tech-mark">///</span></div>
    <div class="block-list">${list.map(s => `<div class="block-item"><span class="block-sign">⊘</span><span class="domain">${esc(s.domain)} ${s.child_id > 0 ? `<small>· nur ${esc(s.child)}</small>` : ''}</span><button data-del="${s.id}" aria-label="${esc(s.domain)} entfernen">${I.trash}</button></div>`).join('') || '<p class="muted">Noch keine Seiten gesperrt.</p>'}</div>
    ${limit && j.sites.length > limit ? `<p class="small-note"><a href="#sites">+ ${j.sites.length - limit} weitere anzeigen</a></p>` : ''}
    <form class="block-form" id="siteF"><input name="domain" placeholder="Neue Webseite (z. B. beispiel.com)" aria-label="Domain" required>
    <select name="child" aria-label="Für wen" style="max-width:110px"><option value="0">Alle</option>${S.kids.map(c => `<option value="${c.id}">${esc(c.name)}</option>`).join('')}</select><button type="submit">+ Sperren</button></form>`;
  $$('[data-del]', box).forEach(b => b.onclick = run(async () => { await api('site_del', { id: +b.dataset.del }); toast('Entfernt'); sitesPanel(box, limit); }));
  $('#siteF', box).onsubmit = run(async e => { e.preventDefault(); const f = new FormData(e.target); await api('site_add', { domain: f.get('domain'), child_id: +f.get('child') }); toast('Webseite gesperrt'); sitesPanel(box, limit); });
  return j;
}
async function appsPanel(box, compact) {
  const j = await api('apps'); const cid = S.appChild;
  const isBlocked = a => a.blocked_for.includes(0) || (cid && a.blocked_for.includes(cid));
  const apps = compact ? j.apps.slice(0, 12) : j.apps;
  box.innerHTML = `<div class="panel-heading"><h2><span class="accent">⊞</span> App-Sperren</h2><span class="tech-mark">///</span></div>
    <div class="chips" style="margin-bottom:9px"><button class="chip ${!cid ? 'on' : ''}" data-ac="0">Alle Kinder</button>${S.kids.map(c => `<button class="chip ${cid === c.id ? 'on' : ''}" data-ac="${c.id}">${esc(c.name)}</button>`).join('')}</div>
    <div class="app-grid">${apps.map(a => { const b = isBlocked(a), viaAll = cid && a.blocked_for.includes(0); return `<button class="app-tile ${b ? 'blocked' : 'allowed'}" data-app="${a.id}" data-b="${b ? 1 : 0}" title="${esc(a.exes)}${viaAll ? ' – für alle gesperrt' : ''}">
      ${+a.is_new ? '<span class="new-rib">NEU</span>' : ''}<span class="app-logo ${a.icon ? 'applogo-' + esc(a.icon) : ''}">${appLetter(a.name)}</span>${esc(a.name)}<span class="lock-state">${b ? '⊘ Gesperrt' : '✓ Erlaubt'}</span></button>`; }).join('')}</div>
    ${compact ? '<p class="small-note"><a href="#apps">Eigene Programme sperren →</a></p>' : ''}`;
  $$('[data-ac]', box).forEach(b => b.onclick = () => { S.appChild = +b.dataset.ac; appsPanel(box, compact); });
  $$('[data-app]', box).forEach(b => b.onclick = run(async () => {
    const a = j.apps.find(x => x.id === +b.dataset.app), blocked = b.dataset.b === '1';
    if (blocked && cid && a.blocked_for.includes(0)) { toast('Für alle Kinder gesperrt – unter „Alle Kinder“ ändern', true); return; }
    await api('app_toggle', { app_id: a.id, child_id: cid, blocked: !blocked }); toast(`${a.name}: ${blocked ? 'erlaubt' : 'gesperrt'}`); appsPanel(box, compact);
  }));
  return j;
}

/* ---------- Neu entdeckte Apps ---------- */
function newAppsPanel(box, list, done) {
  const kidName = id => (S.kids.find(k => k.id === id) || {}).name || '';
  box.innerHTML = `<div class="panel-heading"><h2><span class="accent">🆕</span> Neue Apps entdeckt <span class="na-count">${list.length}</span></h2>${list.length > 1 ? '<button class="btn small" id="naAll">Alle als gesehen markieren</button>' : ''}</div>
    <div class="na-list">${list.map(a => { const strictBlocked = a.blocked_for.some(x => x > 0) && !a.blocked_for.includes(0);
      return `<div class="na-row ${strictBlocked ? 'strict' : ''}"><span class="app-logo na-logo">${appLetter(a.name)}</span>
      <div class="na-info"><b>${esc(a.name)}</b><small><code>${esc(a.exe)}</code> · ${a.child ? esc(a.child) + ' · ' : ''}${ago(a.first_seen)}${a.sec ? ' · ' + dur(a.sec) + ' genutzt' : ''}</small>
        ${strictBlocked ? `<span class="na-tag">🔒 Strenger Modus: gesperrt für ${a.blocked_for.filter(x => x > 0).map(kidName).filter(Boolean).map(esc).join(', ') || 'Kinder im strengen Modus'}</span>` : ''}</div>
      <div class="na-btns"><button class="btn ok small" data-nad="allow" data-id="${a.id}">✓ Erlauben</button><button class="btn danger small" data-nad="block" data-id="${a.id}">⛔ Sperren</button>
        ${a.child_id ? `<button class="btn small" data-nad="block" data-cid="${a.child_id}" data-id="${a.id}" title="Nur für ${esc(a.child)} sperren">nur ${esc(a.child)}</button>` : ''}</div></div>`; }).join('')}</div>
    <p class="small-note">„Erlauben“ gibt die App für alle Kinder frei, „Sperren“ sperrt sie für alle. Danach kannst du sie jederzeit unter <a href="#apps">App-Sperren</a> pro Kind ändern.</p>`;
  $$('[data-nad]', box).forEach(b => b.onclick = run(async () => {
    const r = await api('app_decide', { app_id: +b.dataset.id, decision: b.dataset.nad, child_id: +b.dataset.cid || 0 });
    toast(`${r.name}: ${b.dataset.nad === 'allow' ? 'erlaubt ✓' : 'gesperrt ⛔'}`); done();
  }));
  const all = $('#naAll', box); if (all) all.onclick = run(async () => { await api('apps_seen_all', {}); toast('Alle als gesehen markiert'); done(); });
}

/* ---------- Kinder ---------- */
VIEWS.kids = async el => {
  await loadKids();
  const pct = st => st.unlimited ? 0 : Math.min(100, Math.round(st.used_sec / Math.max(1, (st.limit_min + st.bonus_min) * 60) * 100));
  el.innerHTML = `<div class="view"><div class="view-title"><h2>Kinder-Profile</h2><button class="btn primary" id="addKid">${I.plus} Kind hinzufügen</button></div>
    ${S.kids.length ? '' : '<div class="empty panel"><b>Noch keine Kinder</b>Lege für jedes Kind ein Profil an. Jeder Windows-PC wird danach einem Kind zugeordnet.</div>'}
    <div class="kid-grid">${S.kids.map(c => { const st = c.state, p = pct(st), col = COLORS[c.color] || COLORS.cyan;
      const status = st.lock_reason ? `<span class="kchip red">🔒 ${LOCKR[st.lock_reason]}</span>` : c.online ? '<span class="kchip green">● Online</span>' : `<span class="kchip">○ Offline${c.last_seen ? ' · ' + ago(c.last_seen) : ''}</span>`;
      return `<section class="kid-card" style="--kc:${col}">
        <div class="kid-head" data-child="${c.id}">
          <img class="kid-av" src="${esc(avatar(c))}" alt="">
          <div class="kid-id"><h3>${esc(c.name)}</h3><div class="kchips">${status}<span class="kchip">${I.pc} ${c.devices} Gerät${c.devices === 1 ? '' : 'e'}</span>${c.pending ? `<span class="kchip pink">🙋 ${c.pending} Anfrage${c.pending > 1 ? 'n' : ''}</span>` : ''}</div></div>
          <span class="chevron">›</span></div>
        <div class="kid-today">
          <div class="kt-row"><span>Heute · <b>${esc(st.day_label)}</b></span><span><b>${dur(st.used_sec)}</b> / ${minsTxt(st.limit_min)}${st.bonus_min ? ` <small>+${st.bonus_min} min</small>` : ''}</span></div>
          <div class="kbar"><i style="width:${st.unlimited ? 100 : p}%;${st.unlimited ? 'opacity:.35' : ''}"></i></div>
          <div class="kt-row muted"><span>Erlaubt ${esc(st.start)} – ${esc(st.end === '24:00' ? '24:00' : st.end)} Uhr${nightNote(st)}</span><span>${st.unlimited ? 'ohne Limit' : 'noch ' + dur(st.remaining_sec)}</span></div>
        </div>
        <div class="kid-rules">${Object.entries(c.rules).map(([t, r]) => `<div class="krule ${t === st.day_type ? 'today' : ''}"><small>${{ school: 'Schultag', weekend: 'Wochenende', holiday: 'Ferien' }[t]}</small><b>${minsTxt(r.limit_min)}</b><span>${r.start_time}–${r.end_time}</span></div>`).join('')}</div>
        <div class="kid-quick"><button class="btn small ok" data-q="bonus" data-m="15" data-c="${c.id}">+15 min</button><button class="btn small ok" data-q="bonus" data-m="30" data-c="${c.id}">+30 min</button>
          ${st.lock_reason === 'manual' ? `<button class="btn small" data-q="unlock" data-c="${c.id}">🔓 Entsperren</button>` : `<button class="btn small danger" data-q="lock" data-m="60" data-c="${c.id}">🔒 1 h sperren</button>`}</div>
        <div class="kid-foot"><button class="btn small" data-open="${c.id}">⚙ Steuern</button><a class="btn small" href="#limits">${I.clock} Zeiten</a><button class="btn small" data-edit="${c.id}">✎ Profil</button><button class="btn small" data-pair="${c.id}">${I.pc} Koppeln</button></div>
      </section>`; }).join('')}</div></div>`;
  $('#addKid').onclick = () => kidForm();
  $$('.kid-head', el).forEach(h => h.onclick = () => childModal(+h.dataset.child));
  $$('[data-open]', el).forEach(b => b.onclick = () => childModal(+b.dataset.open));
  $$('[data-edit]', el).forEach(b => b.onclick = () => kidForm(S.kids.find(k => k.id === +b.dataset.edit)));
  $$('[data-pair]', el).forEach(b => b.onclick = run(() => pairModal(+b.dataset.pair)));
  $$('[data-q]', el).forEach(b => b.onclick = run(async () => { const r = await api('child_action', { child_id: +b.dataset.c, action: b.dataset.q, minutes: +b.dataset.m || 30 }); toast(r.message); VIEWS.kids(el); }));
};

/* ---------- Statistik ---------- */
VIEWS.stats = async el => {
  if (!S.kids.length) await loadKids();
  const j = await api('stats', undefined, `&days=${S.statsDays}&child_id=${S.statsChild}`);
  const mxA = Math.max(1, ...j.apps.map(a => +a.sec)), mxW = Math.max(1, ...j.web.map(a => +a.sec));
  const chart = S.statsChild ? { labels: j.chart.labels, series: j.chart.series.filter(s => s.id === S.statsChild) } : j.chart;
  el.innerHTML = `<div class="view"><div class="view-title"><h2>Nutzungsstatistiken</h2><div class="segmented">${[[1, 'Heute'], [7, '7 Tage'], [30, '30 Tage']].map(([v, l]) => `<button data-d="${v}" class="${S.statsDays === v ? 'selected' : ''}">${l}</button>`).join('')}</div></div>
    <div class="chips"><button class="chip ${!S.statsChild ? 'on' : ''}" data-c="0">Alle Kinder</button>${S.kids.map(c => `<button class="chip ${S.statsChild === c.id ? 'on' : ''}" data-c="${c.id}">${esc(c.name)}</button>`).join('')}</div>
    <section class="panel usage-panel"><div class="panel-heading"><h2><span class="accent">▥</span> Verlauf · gesamt ${dur(j.total)}</h2></div><div class="chart-area"><div class="chart-wrap">${chartSVG(chart, j.days === 1)}</div><div class="legend">${legend(chart, j.days === 1)}</div></div></section>
    <div class="page-grid"><section class="panel"><div class="panel-heading"><h2><span class="accent">⊞</span> Top-Programme</h2></div>
      <table class="table">${j.apps.map(a => { const ic = appIcon(a.app); return `<tr><td><b class="app-icon ${ic ? 'applogo-' + ic : ''}" style="background:#143a70">${appLetter(a.label || a.app)}</b> ${esc(a.label || a.app)}<br><small class="muted">${esc(a.app)}</small></td><td style="width:35%"><div class="bar"><i style="width:${(a.sec / mxA * 100).toFixed(1)}%"></i></div></td><td style="text-align:right;white-space:nowrap">${dur(a.sec)}</td></tr>`; }).join('') || '<tr><td class="muted">Keine Daten</td></tr>'}</table></section>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">◎</span> Top-Webseiten</h2></div>
      <table class="table">${j.web.map(w => `<tr><td>${esc(w.domain)}</td><td style="width:35%"><div class="bar"><i style="width:${(w.sec / mxW * 100).toFixed(1)}%"></i></div></td><td style="text-align:right;white-space:nowrap">${dur(w.sec)}</td><td><button class="btn small danger" data-block="${esc(w.domain)}">Sperren</button></td></tr>`).join('') || '<tr><td class="muted">Keine Daten – Browser-Erweiterung aktiv?</td></tr>'}</table></section></div></div>`;
  $$('[data-d]', el).forEach(b => b.onclick = () => { S.statsDays = +b.dataset.d; VIEWS.stats(el); });
  $$('[data-c]', el).forEach(b => b.onclick = () => { S.statsChild = +b.dataset.c; VIEWS.stats(el); });
  $$('[data-block]', el).forEach(b => b.onclick = run(async () => { await api('site_add', { domain: b.dataset.block, child_id: S.statsChild }); toast(b.dataset.block + ' gesperrt'); }));
};

/* ---------- Webseiten ---------- */
VIEWS.sites = async el => {
  if (!S.kids.length) await loadKids();
  el.innerHTML = `<div class="view"><div class="view-title"><h2>Webseiten-Sperren</h2></div>
    <section class="panel" id="flt"><div class="empty">Filterlisten laden …</div></section>
    <div class="page-grid"><section class="panel" id="sp"></section><section class="panel" id="sp2"></section><section class="panel" id="alw"></section><section class="panel" id="safe"></section></div>
    <div class="note">Eigene Sperren wirken über die Browser-Erweiterung (mit Sperrseite) und zusätzlich systemweit über die Windows-App. Kategorie-Filter prüft die Windows-App lokal auf dem Kinder-PC – die Listen werden täglich automatisch aktualisiert. Subdomains sind immer mitgesperrt.</div></div>`;
  filtersPanel(el);
  const j = await sitesPanel($('#sp'));
  $('#sp2').innerHTML = `<div class="panel-heading"><h2><span class="accent">✓</span> Aktive Freigaben</h2></div>
    ${j.grants.map(g => `<div class="block-item"><span style="color:var(--ok)">✓</span><span class="domain">${esc(g.domain.startsWith("app:") ? "🎮 " + g.domain.slice(4) : g.domain)} <small>· ${esc(g.child)} bis ${fmtDT(g.until_at)}</small></span><button data-g="${g.id}">${I.trash}</button></div>`).join('') || '<p class="muted">Keine befristeten Freigaben.</p>'}
    <div class="panel-heading" style="margin-top:16px"><h2><span class="accent">◉</span> Meistbesucht (7 Tage)</h2></div>
    <table class="table">${j.top.map(t => `<tr><td>${esc(t.domain)}</td><td>${dur(t.sec)}</td><td><button class="btn small danger" data-quick="${esc(t.domain)}">Sperren</button></td></tr>`).join('') || '<tr><td class="muted">Noch keine Daten.</td></tr>'}</table>`;
  $$('[data-g]', el).forEach(b => b.onclick = run(async () => { await api('grant_del', { id: +b.dataset.g }); VIEWS.sites(el); }));
  $$('[data-quick]', el).forEach(b => b.onclick = run(async () => { await api('site_add', { domain: b.dataset.quick, child_id: 0 }); toast('Gesperrt'); VIEWS.sites(el); }));
};

async function filtersPanel(el) {
  const j = await api('filters');
  const total = j.filters.filter(f => f.enabled).reduce((a, f) => a + f.count, 0);
  $('#flt', el).innerHTML = `<div class="panel-heading"><h2><span class="accent">🛡</span> Filterlisten nach Kategorie</h2><span class="muted">${total.toLocaleString('de-DE')} Seiten gesperrt · heute ${j.blocked_today} Treffer</span></div>
    <div class="filter-grid">${j.filters.map(f => `<label class="filter ${f.enabled ? 'on' : ''}"><input type="checkbox" data-fk="${f.key}" ${f.enabled ? 'checked' : ''}>
      <span class="ficon">${f.icon}</span><span class="ftxt"><b>${esc(f.name)}</b><small>${esc(f.desc)}</small>
      <small class="${f.error ? 'ferr' : ''}">${f.error ? '⚠ ' + esc(f.error) : f.enabled ? (f.count ? f.count.toLocaleString('de-DE') + ' Einträge · ' + (f.updated ? 'Stand ' + fmtDT(f.updated) : '') : '⏳ wird geladen (spätestens in 1 Min.)') : 'aus'}</small></span>
      ${f.enabled ? `<button class="btn small" data-fr="${f.key}" title="Jetzt aktualisieren">⟳</button>` : ''}</label>`).join('')}</div>
    <p class="hint">Quelle: Block List Project (github.com/blocklistproject/Lists, MIT-Lizenz). Falsch gesperrte Seiten unter „Immer erlauben“ eintragen.</p>`;
  $$('[data-fk]', el).forEach(c => c.onchange = run(async () => {
    c.disabled = true; c.closest('.filter').classList.add('busy');
    try { const r = await api('filter_toggle', { key: c.dataset.fk, enabled: c.checked }); toast(c.checked ? (r.count ? `Aktiviert · ${r.count.toLocaleString('de-DE')} Seiten` : 'Aktiviert') : 'Deaktiviert'); }
    finally { filtersPanel(el); }
  }));
  $$('[data-fr]', el).forEach(b => b.onclick = run(async e => { e.preventDefault(); b.disabled = true; b.textContent = '…'; const r = await api('filter_refresh', { key: b.dataset.fr }); toast(`Aktualisiert · ${r.count.toLocaleString('de-DE')} Einträge`); filtersPanel(el); }));
  $('#alw', el).innerHTML = `<div class="panel-heading"><h2><span class="accent" style="color:var(--ok)">✓</span> Immer erlauben</h2></div>
    <div class="block-list">${j.allow.map(a => `<div class="block-item"><span style="color:var(--ok)">✓</span><span class="domain">${esc(a.domain)}</span><button data-ad="${a.id}">${I.trash}</button></div>`).join('') || '<p class="muted">Keine Ausnahmen. Hier eintragen, wenn ein Filter eine harmlose Seite sperrt (z. B. Schul-Seiten).</p>'}</div>
    <form class="block-form" id="alF"><input name="d" placeholder="z. B. schulportal.de" required><button type="submit">+ Erlauben</button></form>`;
  $$('[data-ad]', el).forEach(b => b.onclick = run(async () => { await api('allow_del', { id: +b.dataset.ad }); filtersPanel(el); }));
  $('#alF', el).onsubmit = run(async e => { e.preventDefault(); await api('allow_add', { domain: new FormData(e.target).get('d') }); toast('Ausnahme gespeichert'); filtersPanel(el); });
  $('#safe', el).innerHTML = `<div class="panel-heading"><h2><span class="accent">🔍</span> Sichere Suche</h2></div>
    <label class="check"><input type="checkbox" id="ss" ${j.safesearch ? 'checked' : ''}> SafeSearch bei Google und Bing erzwingen (Chrome & Edge)</label>
    <label style="margin-top:10px">YouTube eingeschränkter Modus<select id="yt"><option value="0">Aus</option><option value="1">Moderat</option><option value="2">Streng</option></select></label>
    <p class="hint">Gilt nur in den Windows-Konten der Kinder und wird beim nächsten Kontakt übernommen (Browser einmal neu starten).</p>`;
  $('#yt', el).value = String(j.youtube_restrict);
  $('#ss', el).onchange = run(async () => { await api('safe_set', { safesearch: $('#ss', el).checked }); toast('Gespeichert'); });
  $('#yt', el).onchange = run(async () => { await api('safe_set', { youtube_restrict: +$('#yt', el).value }); toast('Gespeichert'); });
}

/* ---------- Apps ---------- */
VIEWS.apps = async el => {
  if (!S.kids.length) await loadKids();
  el.innerHTML = `<div class="view"><div class="view-title"><h2>App-Sperren</h2><button class="btn primary" id="appAdd">${I.plus} Programm hinzufügen</button></div>
    <section class="panel newapps-panel hidden" id="naBox"></section>
    <section class="panel" id="ap"></section><section class="panel" id="autoBox"></section><section class="panel" id="seen"></section>
    <div class="note">Gesperrte Programme werden auf dem Kinder-PC sofort beendet und das Kind sieht einen Hinweis. „Andere Browser“ ist standardmäßig gesperrt, damit nicht an der Browser-Erweiterung vorbei gesurft werden kann.</div></div>`;
  const j = await appsPanel($('#ap'));
  const nl = j.new_apps || [];
  setAppBadge(nl.length);
  if (nl.length) { $('#naBox').classList.remove('hidden'); newAppsPanel($('#naBox'), nl, () => VIEWS.apps(el)); }
  $('#autoBox').innerHTML = `<div class="panel-heading"><h2><span class="accent">⚙</span> Neue Programme – Automatik</h2><span class="tech-mark">///</span></div>
    <div class="auto-grid">
      <label class="switch-row"><input type="checkbox" id="optAuto" ${j.auto_add ? 'checked' : ''}><span class="sw"></span><span><b>Automatisch in die Liste aufnehmen</b><small>Nutzt ein Kind ein Programm zum ersten Mal, erscheint es oben als Kachel mit „NEU“.</small></span></label>
      <label class="switch-row"><input type="checkbox" id="optPush" ${j.push_new ? 'checked' : ''}><span class="sw"></span><span><b>Push aufs Handy bei neuer App</b><small>Mit „Erlauben“ / „Sperren“ direkt aus der Mitteilung (wo das Handy Aktions-Knöpfe unterstützt).</small></span></label>
    </div>
    <h3 class="strict-h">🛡 Strenger Modus pro Kind</h3>
    <p class="muted" style="margin:0 0 10px;font-size:12px">Im strengen Modus laufen <b>nur Programme aus der Liste</b>, die nicht gesperrt sind. Alles Neue wird sofort geschlossen – das Kind kann es mit „Eltern fragen“ anfragen, und du siehst es hier oben als neue App. Windows-Bordmittel (Explorer, Einstellungen, Rechner, Editor …) bleiben immer erlaubt.</p>
    <div class="strict-list">${j.strict.map(c => `<label class="switch-row strict-kid"><input type="checkbox" data-strict="${c.id}" ${c.strict ? 'checked' : ''}><span class="sw"></span><img src="${esc(avatar(S.kids.find(k => k.id === c.id) || c))}" alt=""><span><b>${esc(c.name)}</b><small>${c.strict ? 'Nur erlaubte Programme' : 'Neue Programme laufen, bis du sie sperrst'}</small></span></label>`).join('') || '<p class="muted">Keine Kinder.</p>'}</div>`;
  $('#optAuto').onchange = run(async e => { await api('apps_options', { auto_add: e.target.checked }); toast(e.target.checked ? 'Neue Apps werden automatisch aufgenommen' : 'Automatische Aufnahme aus'); });
  $('#optPush').onchange = run(async e => { await api('apps_options', { push_new: e.target.checked }); toast(e.target.checked ? 'Push bei neuen Apps an' : 'Push bei neuen Apps aus'); });
  $$('[data-strict]', el).forEach(b => b.onchange = run(async () => {
    const c = j.strict.find(x => x.id === +b.dataset.strict);
    if (b.checked && !await confirmBox(`<b>Strenger Modus für ${esc(c.name)}</b><br><br>Ab jetzt laufen auf dem PC nur Programme aus der App-Liste, die nicht gesperrt sind. Unbekannte Programme werden sofort geschlossen.<br><br>Tipp: Lass den PC vorher ein paar Tage normal laufen – dann sind alle genutzten Programme schon in der Liste und du musst sie nur noch erlauben.`, 'Einschalten', false)) { b.checked = false; return; }
    await api('child_strict', { child_id: c.id, strict: b.checked }); toast(`${c.name}: strenger Modus ${b.checked ? 'an' : 'aus'}`); VIEWS.apps(el);
  }));
  $('#seen').innerHTML = `<div class="panel-heading"><h2><span class="accent">◉</span> Zuletzt genutzte Programme (14 Tage)</h2></div><div class="scroll-x"><table class="table"><tr><th>Programm</th><th>Datei</th><th>Zeit</th><th></th></tr>
    ${j.seen.map(s => { const known = j.apps.some(a => a.exes.toLowerCase().split(',').includes(s.app)); return `<tr><td>${esc(s.label || s.app)}</td><td><code>${esc(s.app)}</code></td><td>${dur(s.sec)}</td><td>${known ? '<span class="muted">in Liste</span>' : (j.system || []).includes(s.app) ? '<span class="muted">Windows</span>' : `<button class="btn small danger" data-seen="${esc(s.app)}" data-label="${esc(s.label || s.app)}">Sperren</button>`}</td></tr>`; }).join('') || '<tr><td class="muted" colspan="4">Noch keine Daten.</td></tr>'}</table></div>
    <div class="panel-heading" style="margin-top:14px"><h2><span class="accent">✎</span> Liste verwalten</h2></div>
    <table class="table">${j.apps.map(a => `<tr><td>${esc(a.name)}${+a.is_new ? ' <span class="status-tag pending">NEU</span>' : ''}${a.first_child_name ? `<br><small class="muted">entdeckt bei ${esc(a.first_child_name)} · ${fmtDT(a.first_seen)}</small>` : ''}</td><td><small class="muted">${esc(a.exes)}</small></td><td><button class="btn small" data-adel="${a.id}">${I.trash}</button></td></tr>`).join('')}</table>`;
  $$('[data-seen]', el).forEach(b => b.onclick = run(async () => { await api('app_add', { name: b.dataset.label.slice(0, 80), exes: b.dataset.seen, block: true, child_id: S.appChild }); toast('Gesperrt'); VIEWS.apps(el); }));
  $$('[data-adel]', el).forEach(b => b.onclick = run(async () => { if (!await confirmBox('Programm aus der Liste entfernen?', 'Entfernen')) return; await api('app_del', { id: +b.dataset.adel }); VIEWS.apps(el); }));
  $('#appAdd').onclick = () => modal(`<h2>Programm hinzufügen</h2><form class="modal-form" id="appF"><label>Name<input name="name" required placeholder="z. B. Valorant"></label>
    <label>Programmdatei(en), Komma-getrennt<input name="exes" required placeholder="VALORANT.exe, RiotClientServices.exe"></label><p class="hint">Den Dateinamen findest du im Task-Manager unter „Details“ oder oben in „Zuletzt genutzte Programme“.</p>
    <label class="check"><input type="checkbox" name="game"> Ist ein Spiel (zeigt „Spiel gesperrt“ statt „App gesperrt“)</label>
    <label class="check"><input type="checkbox" name="block" checked> Sofort sperren (${S.appChild ? esc((S.kids.find(k => k.id === S.appChild) || {}).name) : 'alle Kinder'})</label><button class="btn primary">Hinzufügen</button></form>`, () => {
    $('#appF').onsubmit = run(async e => { e.preventDefault(); const f = new FormData(e.target); await api('app_add', { name: f.get('name'), exes: f.get('exes'), block: !!f.get('block'), game: !!f.get('game'), child_id: S.appChild }); closeModal(); toast('Hinzugefügt'); VIEWS.apps(el); });
  });
};

/* ---------- Zeitlimits ---------- */
function nightNote(st) { return st.night_type && st.night_type !== st.day_type ? ` <small class="night-note">🌙 Bettzeit wie ${esc(st.night_label)}</small>` : ''; }
VIEWS.limits = async el => {
  await loadKids();
  S.eveNext = (await api('settings').catch(() => ({ settings: {} }))).settings.evening_next_day !== '0';
  const todayKey = S.kids.length ? S.kids[0].state.day_type : '';
  el.innerHTML = `<div class="view"><div class="view-title"><h2>Zeitlimits &amp; Bettzeiten</h2><span class="muted">Heute gilt: <b style="color:var(--cyan)">${esc(DT[todayKey] || '')}</b></span></div>
    <details class="panel howto" ${localStorage.getItem('msfc_howto') === 'closed' ? '' : 'open'}><summary>ℹ So funktionieren die Zeiten</summary>
      <ol>
        <li><b>Welcher Tag ist heute?</b> Mo–Fr = <b>Schultag</b>, Sa/So = <b>Wochenende</b>. In den Ferien und an Feiertagen (unter „Schulzeit / Ferienmodus“ importiert oder Schalter „Ferien“) gilt <b>Ferien</b>.</li>
        <li><b>Der Abend gehört zum nächsten Tag</b> (Schalter unten): Die <b>Bettzeit</b> richtet sich danach, was morgen ist. Freitagabend gilt also schon die Wochenend-Bettzeit, Sonntagabend wieder die Schultag-Bettzeit – in den Ferien die Ferien-Bettzeit, und am letzten Ferien-Sonntag die Schultag-Bettzeit. Tageslimit und „Erlaubt ab“ bleiben beim heutigen Tag.</li>
        <li><b>Für jeden dieser 3 Tagestypen</b> stellst du unten ein: wie lange der PC <b>insgesamt</b> am Tag benutzt werden darf (z. B. 2 h) – und das <b>Zeitfenster</b>: „Erlaubt ab“ und „Bettzeit ab“.</li>
        <li><b>Beides gilt gleichzeitig:</b> Der PC ist nur im Zeitfenster nutzbar <i>und</i> nur so lange, bis das Tageslimit verbraucht ist – je nachdem, was zuerst kommt.</li>
        <li><b>Gezählt</b> wird nur echte Nutzung: angemeldet, nicht gesperrt, nicht länger als 10 Min. ohne Maus/Tastatur (Ausnahme: Vollbild-Video/Spiel). Mehrere PCs eines Kindes teilen sich ein Konto.</li>
        <li><b>Extra-Zeit</b> (+15 min, genehmigte Anfragen) gilt nur für heute. „Sperren“ und „ohne Limit“ aus dem Kinder-Profil gehen allen Regeln vor.</li>
        <li><b>Schulzeit-Sperre</b> (optional unter „Schulzeit / Ferienmodus“): an Schultagen zusätzlich gesperrt während des Unterrichts.</li>
      </ol>
      <p class="hint">Beispiel Schultag „2 h, erlaubt 14:00–19:30“: Um 13:00 kommt „Guten Morgen – noch nicht“, ab 14:00 läuft die Zeit; nach 2 h Nutzung (z. B. 17:10) kommt „Ätsch Bätsch – Zeit is um!“; spätestens um 19:30 „Zeit fürs Bett“. Vorher gibt es Warnungen nach 15/5/1 Min.</p>
    </details>
    <section class="panel"><label class="switch-row"><input type="checkbox" id="eveNext" ${S.eveNext ? 'checked' : ''}><span class="sw"></span><span><b>Abend gehört zum nächsten Tag</b><small>Freitagabend = Wochenend-Bettzeit, Sonntagabend = Schultag-Bettzeit (außer in den Ferien). Aus: Jeder Tag hat seine eigene Bettzeit.</small></span></label></section>
    <div class="page-grid">${S.kids.map(c => { const st = c.state; return `<div class="dev"><h3><img class="av-sm" src="${esc(avatar(c))}" alt=""> ${esc(c.name)} – heute</h3><div class="kv">
      <b>Tagestyp</b><span>${esc(st.day_label)}</span><b>Erlaubt</b><span>${esc(st.start)} – ${esc(st.end)} Uhr${nightNote(st)}</span>
      <b>Tageslimit</b><span>${minsTxt(st.limit_min)}${st.bonus_min ? ` + ${st.bonus_min} min Extra` : ''}</span><b>Genutzt</b><span>${dur(st.used_sec)}</span>
      <b>Übrig</b><span>${st.unlimited ? '∞' : dur(st.remaining_sec)}</span><b>Status</b><span>${st.lock_reason ? '🔒 ' + LOCKR[st.lock_reason] : '🟢 frei'}</span></div></div>`; }).join('')}</div>
    ${S.kids.map(c => `<section class="panel"><div class="panel-heading"><h2><img class="av-sm" src="${esc(avatar(c))}" alt=""> ${esc(c.name)}</h2><button class="btn primary small" data-save="${c.id}">Speichern</button></div>
      <div class="page-grid">${Object.entries(DT).map(([t, l]) => { const r = c.rules[t]; return `<div class="rule-card ${t === todayKey ? 'today' : ''}" data-rule="${c.id}:${t}"><h3>${l}${t === todayKey ? ' <small class="today-tag">heute aktiv</small>' : ''}</h3>
        <div class="rule-line"><input type="range" min="0" max="600" step="15" value="${Math.min(600, r.limit_min)}" data-k="limit" ${r.limit_min >= 1440 ? 'disabled' : ''}><output>${minsTxt(r.limit_min)}</output></div>
        <label class="check"><input type="checkbox" data-k="unl" ${r.limit_min >= 1440 ? 'checked' : ''}> Kein Zeitlimit</label>
        <div class="grid2"><label>Erlaubt ab<input type="time" data-k="start" value="${r.start_time}"></label><label>Bettzeit ab<input type="time" data-k="end" value="${r.end_time === '24:00' ? '23:59' : r.end_time}"></label></div></div>`; }).join('')}</div></section>`).join('') || '<div class="empty panel"><b>Keine Kinder</b>Bitte zuerst Kinderprofile anlegen.</div>'}</div>`;
  const ht = $('.howto', el); if (ht) ht.addEventListener('toggle', () => { try { localStorage.setItem('msfc_howto', ht.open ? 'open' : 'closed'); } catch (e) {} });
  $$('.rule-card', el).forEach(card => {
    const rng = $('[data-k=limit]', card), out = $('output', card), unl = $('[data-k=unl]', card);
    rng.oninput = () => out.textContent = minsTxt(+rng.value);
    unl.onchange = () => { rng.disabled = unl.checked; out.textContent = unl.checked ? 'Unbegrenzt' : minsTxt(+rng.value); };
  });
  const saveRules = async (cid, quiet) => {
    const rules = {};
    $$(`[data-rule^="${cid}:"]`, el).forEach(card => {
      const t = card.dataset.rule.split(':')[1];
      rules[t] = { limit_min: $('[data-k=unl]', card).checked ? 1440 : +$('[data-k=limit]', card).value, start_time: $('[data-k=start]', card).value, end_time: $('[data-k=end]', card).value === '23:59' ? '24:00' : $('[data-k=end]', card).value };
    });
    await api('rules_save', { child_id: cid, rules });
    toast(quiet ? 'Automatisch gespeichert ✓' : 'Zeitregeln gespeichert ✓ (Schultag, Wochenende, Ferien)');
  };
  $$('[data-save]', el).forEach(b => b.onclick = run(() => saveRules(+b.dataset.save)));
  $('#eveNext').onchange = run(async e => { await api('evening_set', { on: e.target.checked }); toast(e.target.checked ? 'Abend gehört jetzt zum nächsten Tag' : 'Jeder Tag hat seine eigene Bettzeit'); VIEWS.limits(el); });
  // Änderungen werden automatisch gespeichert
  const auto = {};
  $$('.rule-card', el).forEach(card => {
    const cid = +card.dataset.rule.split(':')[0];
    auto[cid] = auto[cid] || debounce(run(() => saveRules(cid, true)), 900);
    $$('input', card).forEach(i => i.addEventListener('change', auto[cid]));
  });
};

/* ---------- Schulzeit / Ferien ---------- */
const STATES = { BW: 'Baden-Württemberg', BY: 'Bayern', BE: 'Berlin', BB: 'Brandenburg', HB: 'Bremen', HH: 'Hamburg', HE: 'Hessen', MV: 'Mecklenburg-Vorpommern', NI: 'Niedersachsen', NW: 'Nordrhein-Westfalen', RP: 'Rheinland-Pfalz', SL: 'Saarland', SN: 'Sachsen', ST: 'Sachsen-Anhalt', SH: 'Schleswig-Holstein', TH: 'Thüringen' };
VIEWS.mode = async el => {
  const j = await api('mode');
  el.innerHTML = `<div class="view"><div class="view-title"><h2>Schulzeit / Ferienmodus</h2></div><div class="page-grid">
    <section class="panel"><div class="panel-heading"><h2><span class="accent">⚑</span> Modus</h2></div>
      <div class="mode-switch"><button data-mode="school" class="${j.mode === 'school' ? 'selected' : ''}">▤ Schulzeit</button><button data-mode="auto" class="${j.mode === 'auto' ? 'selected' : ''}">⟳ Automatisch</button><button data-mode="holiday" class="${j.mode === 'holiday' ? 'selected' : ''}">☼ Ferien</button></div>
      <p class="mode-info">Heute gilt: <b>${DT[j.today_type]}</b>. <br><span class="muted">Automatisch = Wochenenden und eingetragene Ferien/Feiertage werden erkannt.</span></p>
      <div class="grid2"><label>Unterricht von<input type="time" id="sf" value="${esc(j.school_from)}"></label><label>bis<input type="time" id="st" value="${esc(j.school_to)}"></label></div>
      <label class="check" style="margin-top:10px"><input type="checkbox" id="slock" ${j.school_lock ? 'checked' : ''}> PC an Schultagen während der Unterrichtszeit sperren</label>
      <p class="hint">Welche Bildschirmzeit und Bettzeit an Schultagen, Wochenenden und in den Ferien gilt, stellst du unter <a href="#limits">Zeitlimits</a> getrennt ein.</p>
      <button class="btn wide" id="saveSchool">Schulzeit speichern</button></section>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">⟳</span> Ferien automatisch laden</h2></div>
      <label>Bundesland<select id="state">${Object.entries(STATES).map(([k, v]) => `<option value="${k}" ${j.state === k ? 'selected' : ''}>${v}</option>`).join('')}</select></label>
      <button class="btn primary wide" id="imp">Schulferien &amp; Feiertage importieren</button><p class="hint">Quelle: openholidaysapi.org · ${j.last_import ? 'Letzter Import: ' + esc(j.last_import) : 'noch nicht importiert'}</p></section></div>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">▦</span> Ferien &amp; freie Tage</h2><button class="btn small" id="hAdd">${I.plus} Eigener Eintrag</button></div>
      <div class="scroll-x"><table class="table"><tr><th>Name</th><th>Von</th><th>Bis</th><th>Quelle</th><th></th></tr>${j.holidays.map(h => `<tr><td>${esc(h.name)}</td><td>${fmtD(h.start_date)}</td><td>${fmtD(h.end_date)}</td><td><small class="muted">${esc(h.source)}</small></td><td><button class="btn small" data-hdel="${h.id}">${I.trash}</button></td></tr>`).join('') || '<tr><td colspan="5" class="muted">Noch keine Einträge – oben importieren.</td></tr>'}</table></div></section></div>`;
  $$('[data-mode]', el).forEach(b => b.onclick = run(async () => { await api('mode_set', { mode: b.dataset.mode }); toast('Modus gespeichert'); VIEWS.mode(el); }));
  $('#saveSchool').onclick = run(async () => { await api('mode_set', { mode: $('.mode-switch .selected', el) ? $('.mode-switch .selected', el).dataset.mode : j.mode, school_from: $('#sf').value, school_to: $('#st').value, school_lock: $('#slock').checked }); toast('Schulzeit gespeichert'); });
  $('#imp').onclick = run(async () => { $('#imp').disabled = true; try { const r = await api('holiday_import', { state: $('#state').value }); toast(`${r.count} Einträge importiert`); VIEWS.mode(el); } finally { $('#imp').disabled = false; } });
  $$('[data-hdel]', el).forEach(b => b.onclick = run(async () => { await api('holiday_del', { id: +b.dataset.hdel }); VIEWS.mode(el); }));
  $('#hAdd').onclick = () => modal(`<h2>Freien Tag / Ferien eintragen</h2><form class="modal-form" id="hF"><label>Bezeichnung<input name="name" placeholder="z. B. Brückentag" required></label><div class="grid2"><label>Von<input type="date" name="start" required></label><label>Bis<input type="date" name="end" required></label></div><button class="btn primary">Speichern</button></form>`, () => {
    $('#hF').onsubmit = run(async e => { e.preventDefault(); const f = new FormData(e.target); await api('holiday_add', { name: f.get('name'), start: f.get('start'), end: f.get('end') }); closeModal(); VIEWS.mode(el); });
  });
};

/* ---------- Anfragen ---------- */
VIEWS.requests = async el => {
  const j = await api('requests'); const pend = j.requests.filter(r => r.status === 'pending');
  setBadges(pend.length);
  el.innerHTML = `<div class="view"><div class="view-title"><h2>Anfragen der Kinder</h2><span class="muted">${pend.length} offen</span></div>
    ${!pend.length ? '<div class="empty panel"><b>Keine offenen Anfragen 🎉</b>Wenn ein Kind am PC nach mehr Zeit fragt, erscheint das hier – und als Push auf deinem iPhone.</div>' : ''}
    <div class="page-grid">${j.requests.map(r => `<div class="req ${r.status}"><img src="${esc(avatar({ id: r.child_id, avatar: r.avatar }))}" alt="">
      <div><h3>${esc(r.child)} ${r.kind === 'site' ? `möchte <span style="color:var(--cyan)">${esc(r.domain)}</span> öffnen` : r.kind === 'app' ? `möchte <span style="color:var(--cyan)">${esc(r.label || r.domain)}</span> starten` : `bittet um <span style="color:var(--cyan)">${r.minutes} min</span>`}</h3>
      <div class="meta">${esc(REASONS[r.reason] || r.reason)} · ${esc(r.device || '')} · ${ago(r.created_at)} <span class="status-tag ${r.status}">${r.status === 'pending' ? 'offen' : r.status === 'approved' ? `genehmigt (${r.granted} min)` : 'abgelehnt'}</span></div>
      ${r.message ? `<p class="msg">„${esc(r.message)}“</p>` : ''}
      ${r.status === 'pending' ? `<div class="btn-row">${[15, 30, 60].map(m => `<button class="btn ok small" data-ok="${r.id}" data-m="${m}">✓ ${m} min</button>`).join('')}<button class="btn small" data-custom="${r.id}">Andere…</button><button class="btn danger small" data-no="${r.id}">✕ Ablehnen</button></div>` : ''}</div></div>`).join('')}</div></div>`;
  $$('[data-ok]', el).forEach(b => b.onclick = run(async () => { await api('request_decide', { id: +b.dataset.ok, approve: true, minutes: +b.dataset.m }); toast('Genehmigt ✓'); VIEWS.requests(el); }));
  $$('[data-no]', el).forEach(b => b.onclick = run(async () => { await api('request_decide', { id: +b.dataset.no, approve: false }); toast('Abgelehnt'); VIEWS.requests(el); }));
  $$('[data-custom]', el).forEach(b => b.onclick = () => modal(`<h2>Wie viel Zeit?</h2><form class="modal-form" id="cF"><label>Minuten<input type="number" name="m" min="5" max="600" value="45" required></label><button class="btn primary">Genehmigen</button></form>`, () => {
    $('#cF').onsubmit = run(async e => { e.preventDefault(); await api('request_decide', { id: +b.dataset.custom, approve: true, minutes: +new FormData(e.target).get('m') }); closeModal(); toast('Genehmigt ✓'); VIEWS.requests(el); });
  }));
  every(15, async () => { if (location.hash === '#requests') await VIEWS.requests(el); });
};

/* ---------- Warnungen ---------- */
const EVI = { message_read: '✓', tamper: '⚠', master_fail: '🔑', uninstall: '🗑', ext_missing: '🧩', blocked_app: '⊘', blocked_site: '⊘', lock: '⏰', request: '🙋', offline: '📴', online: '📶', paired: '🔗', parent: '👤', parent_unlock: '🔓', time_jump: '🕑', agent_start: '▶', warning: '⏳', browser_closed: '🧩', new_app: '🆕' };
VIEWS.alerts = async el => {
  const lvl = (location.hash.split('?')[1] || '') === 'warn' ? 'warn' : '';
  const j = await api('events', undefined, lvl ? '&level=warn' : '');
  api('events_seen', {}).catch(() => {});
  el.innerHTML = `<div class="view"><div class="view-title"><h2>Warnungen &amp; Berichte</h2><div class="segmented"><button class="${!lvl ? 'selected' : ''}" data-l="">Alle</button><button class="${lvl ? 'selected' : ''}" data-l="warn">Nur Warnungen</button></div></div>
    <section class="panel">${j.events.map(e => `<div class="ev ${e.level}"><span class="ic">${EVI[e.type] || '•'}</span><div><b>${esc(e.title)}</b><small>${esc(e.child || '')}${e.device ? ' · ' + esc(e.device) : ''}${e.detail ? ' · ' + esc(e.detail) : ''}</small></div><time>${fmtDT(e.created_at)}</time></div>`).join('') || '<div class="empty">Keine Einträge.</div>'}</section></div>`;
  $$('[data-l]', el).forEach(b => b.onclick = () => { location.hash = 'alerts' + (b.dataset.l ? '?warn' : ''); });
};

/* ---------- Geräte ---------- */
const PROT = { usage: 'Nutzungszugriff', access: 'Bedienungshilfe', overlay: 'Über anderen Apps', admin: 'Geräteadmin', vpn: 'Webfilter', battery: 'Akku' };
function protTxt(p) {
  if (!p || !Object.keys(p).length) return '–';
  const miss = Object.keys(PROT).filter(k => k in p && !p[k]);
  return miss.length ? `<span style="color:var(--red)">⚠ fehlt: ${miss.map(k => PROT[k]).join(', ')}</span>` : '<span style="color:var(--green)">✓ vollständig</span>';
}
VIEWS.devices = async el => {
  if (!S.kids.length) await loadKids();
  const j = await api('devices');
  const dl = (f, l) => j.downloads[f] ? `<a class="btn" href="downloads/${f}" download>⬇ ${l} <small class="muted">(${(j.downloads[f] / 1048576).toFixed(1)} MB)</small></a>` : `<span class="btn" style="opacity:.5">${l} – fehlt in /downloads</span>`;
  el.innerHTML = `<div class="view"><div class="view-title"><h2>Geräte &amp; Download</h2><button class="btn primary" id="pairBtn">${I.plus} Gerät koppeln</button></div>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">⬇</span> Installation auf PC &amp; Handy</h2></div>
      <div class="btn-row">${dl('MSFamilyControl-Setup.exe', 'Windows-App (EXE)')}${dl('MSFamilyControl.apk', 'Android-App (APK)')}${dl('browser-extension-chrome-edge.zip', 'Erweiterung Chrome/Edge')}${dl('browser-extension-firefox.zip', 'Erweiterung Firefox')}</div>
      <ol class="steps"><li>„Gerät koppeln“ klicken → Kopplungscode notieren.</li><li>EXE auf dem Kinder-PC (als Administrator) starten, Server-Adresse <code>${esc(j.server_url)}</code>, Code und Masterpasswort eingeben.</li>
      <li>Die App läuft danach als Windows-Dienst im Hintergrund (Schild-Symbol im Infobereich) – beenden nur mit Masterpasswort.</li><li>Die Browser-Erweiterungen liegen nach der Installation unter <code>C:\\Program Files\\MS Family Control\\extensions</code>; der Assistent öffnet die Seiten zum Aktivieren.</li><li><b>Android-Handy:</b> APK aufs Handy laden, installieren, App öffnen – Server-Adresse, Masterpasswort und Kopplungscode eingeben, dann führt der Assistent durch die Berechtigungen.</li></ol></section>
    <div class="page-grid">${j.devices.map(d => { const s = d.status || {}, andr = s.platform === 'android' || /android/i.test(d.os || ''); return `<div class="dev"><h3><span class="dot ${d.online ? 'green-bg' : 'muted-bg'}"></span>${andr ? '📱' : '🖥'} ${esc(d.name)}</h3>
      ${andr ? '' : `<label class="check testmode"><input type="checkbox" data-test="${d.id}" ${+d.test_mode ? 'checked' : ''}> 🧪 Testmodus – Regeln gelten auch für Eltern-Konten an diesem PC</label>`}
      <div class="kv"><b>Kind</b><span>${esc(d.child || '–')}</span><b>Status</b><span>${d.online ? (s.locked ? '🔒 ' + (LOCKR[s.reason] || 'gesperrt') : '🟢 online') : '⚪ offline · ' + ago(d.last_seen)}</span>
      ${andr ? `<b>App</b><span>${esc(s.app_label || '–')}</span><b>Schutz</b><span>${protTxt(s.prot)}</span>${s.battery != null ? `<b>Akku</b><span>${s.battery} %</span>` : ''}<b>System</b><span>${esc(d.os || '–')}</span>`
      : `<b>Windows-Nutzer</b><span>${esc(s.user || '–')}</span><b>Programm</b><span>${esc(s.app_label || '–')}</span><b>Browser-Schutz</b><span>${s.ext && Object.keys(s.ext).length ? Object.keys(s.ext).map(esc).join(', ') + ' ✓' : '<span style="color:var(--gold)">keine Erweiterung aktiv</span>'}</span>`}
      <b>App-Version</b><span>${esc(d.version || '–')}</span><b>IP</b><span>${esc(d.last_ip || '–')}</span><b>Gekoppelt</b><span>${fmtDT(d.created_at)}</span></div>
      <div class="btn-row"><button class="btn small" data-msg="${d.id}">💬 Nachricht</button>${andr ? '' : `<button class="btn small" data-logoff="${d.id}">Abmelden</button><button class="btn small danger" data-shut="${d.id}">⏻ Herunterfahren</button>`}
      <button class="btn small" data-dedit="${d.id}">✎</button><button class="btn small" data-mpw="${d.id}">🔑 Masterpasswort</button><button class="btn small danger" data-ddel="${d.id}">${I.trash}</button></div></div>`; }).join('') || '<div class="empty panel"><b>Noch kein Gerät gekoppelt</b>Erzeuge einen Kopplungscode und installiere die Windows-App bzw. Android-App beim Kind.</div>'}</div></div>`;
  $('#pairBtn').onclick = () => modal(`<h2>PC koppeln</h2><form class="modal-form" id="pF"><label>Für welches Kind?<select name="c">${S.kids.map(c => `<option value="${c.id}">${esc(c.name)}</option>`).join('')}</select></label><button class="btn primary">Code erzeugen</button></form>`, () => {
    $('#pF').onsubmit = run(async e => { e.preventDefault(); await pairModal(+new FormData(e.target).get('c')); });
  });
  const cmd = (id, type, text) => run(async () => { await api('device_cmd', { id, type, text }); toast('Befehl gesendet – wird beim nächsten Kontakt ausgeführt'); })();
  $$('[data-msg]', el).forEach(b => b.onclick = () => modal(`<h2>💬 Nachricht an den PC</h2><form class="modal-form" id="mF"><label>Text<textarea name="t" rows="3" maxlength="300" required placeholder="Essen ist fertig! 🍝"></textarea></label><label class="check"><input type="checkbox" name="hi"> ❗ Wichtig</label><button class="btn primary">Senden</button><p class="hint">Mehrere Kinder auf einmal: Menü „Nachrichten“.</p></form>`, () => {
    $('#mF').onsubmit = run(async e => { e.preventDefault(); const f = new FormData(e.target); await api('device_cmd', { id: +b.dataset.msg, type: 'message', text: f.get('t'), priority: f.get('hi') ? 'high' : 'normal' }); toast('Nachricht gesendet ✓'); closeModal(); });
  }));
  $$('[data-test]', el).forEach(c => c.onchange = run(async () => {
    const d = j.devices.find(x => x.id === +c.dataset.test);
    await api('device_update', { id: d.id, name: d.name, child_id: d.child_id, test_mode: c.checked });
    toast(c.checked ? '🧪 Testmodus an – greift in ca. 15 Sekunden' : 'Testmodus aus');
  }));
  $$('[data-shut]', el).forEach(b => b.onclick = async () => { if (await confirmBox('PC jetzt herunterfahren? (Das Kind bekommt 60 Sekunden Vorwarnung.)', 'Herunterfahren')) cmd(+b.dataset.shut, 'shutdown'); });
  $$('[data-logoff]', el).forEach(b => b.onclick = async () => { if (await confirmBox('Windows-Benutzer jetzt abmelden? Ungespeicherte Arbeit kann verloren gehen.', 'Abmelden')) cmd(+b.dataset.logoff, 'logoff'); });
  $$('[data-ddel]', el).forEach(b => b.onclick = run(async () => { if (!await confirmBox('Gerät entfernen? Die App auf dem PC verliert ihre Verbindung (Regeln gelten dort zuletzt bekannt weiter, bis sie per Masterpasswort deinstalliert wird).', 'Entfernen')) return; await api('device_delete', { id: +b.dataset.ddel }); VIEWS.devices(el); }));
  $$('[data-dedit]', el).forEach(b => b.onclick = () => { const d = j.devices.find(x => x.id === +b.dataset.dedit); modal(`<h2>Gerät bearbeiten</h2><form class="modal-form" id="dF"><label>Name<input name="n" value="${esc(d.name)}"></label><label>Kind<select name="c">${S.kids.map(c => `<option value="${c.id}" ${c.id === d.child_id ? 'selected' : ''}>${esc(c.name)}</option>`).join('')}</select></label><button class="btn primary">Speichern</button></form>`, () => {
    $('#dF').onsubmit = run(async e => { e.preventDefault(); const f = new FormData(e.target); await api('device_update', { id: d.id, name: f.get('n'), child_id: +f.get('c') }); closeModal(); VIEWS.devices(el); });
  }); });
  $$('[data-mpw]', el).forEach(b => b.onclick = () => modal(`<h2>🔑 Masterpasswort ändern</h2><p class="muted">Das neue Passwort wird beim nächsten Kontakt auf den PC übertragen (nur als sicherer Hash).</p><form class="modal-form" id="mpF"><label>Neues Masterpasswort<input type="password" name="p" minlength="6" required></label><label>Wiederholen<input type="password" name="p2" minlength="6" required></label><label class="check"><input type="checkbox" name="all"> Für alle Geräte setzen</label><button class="btn primary">Übernehmen</button></form>`, () => {
    $('#mpF').onsubmit = run(async e => { e.preventDefault(); const f = new FormData(e.target); if (f.get('p') !== f.get('p2')) throw new Error('Passwörter stimmen nicht überein'); const r = await api('master_set', { id: f.get('all') ? 0 : +b.dataset.mpw, password: f.get('p') }); closeModal(); toast(`Masterpasswort für ${r.count} Gerät(e) gesetzt`); });
  }));
  every(20, async () => { if (location.hash === '#devices') await VIEWS.devices(el); });
};

/* ---------- Einstellungen ---------- */
function b64ToU8(s) { const p = '='.repeat((4 - s.length % 4) % 4); const r = atob((s + p).replace(/-/g, '+').replace(/_/g, '/')); return Uint8Array.from([...r].map(c => c.charCodeAt(0))); }
async function pushState() {
  if (!('serviceWorker' in navigator) || !('PushManager' in window)) return 'unsupported';
  const reg = await navigator.serviceWorker.getRegistration(); if (!reg) return 'off';
  const sub = await reg.pushManager.getSubscription(); return sub ? 'on' : 'off';
}
async function enablePush() {
  if (!window.isSecureContext) throw new Error('Push benötigt HTTPS (z. B. über Cloudflare)');
  if (!('PushManager' in window)) throw new Error(/iPhone|iPad/.test(navigator.userAgent) ? 'Auf dem iPhone zuerst „Zum Home-Bildschirm“ hinzufügen und die App von dort öffnen.' : 'Dieser Browser unterstützt kein Web-Push');
  const perm = await Notification.requestPermission(); if (perm !== 'granted') throw new Error('Benachrichtigungen wurden nicht erlaubt');
  const reg = await navigator.serviceWorker.register('sw.js'); await navigator.serviceWorker.ready;
  const key = (await api('vapid')).key;
  let sub = await reg.pushManager.getSubscription();
  if (sub) await sub.unsubscribe();
  sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: b64ToU8(key) });
  await api('push_sub', { subscription: sub.toJSON() });
}
VIEWS.settings = async el => {
  const j = await api('settings'), s = j.settings, ps = await pushState().catch(() => 'unsupported');
  const tog = (k, l) => `<label class="check"><input type="checkbox" data-s="${k}" ${s[k] === '1' ? 'checked' : ''}> ${l}</label>`;
  const curDt = S.me.dash_theme || 'anime';
  el.innerHTML = `<div class="view"><div class="view-title"><h2>Einstellungen</h2></div>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">🎨</span> Dashboard-Design</h2><span class="muted">gilt nur für dein Konto (${esc(S.me.name)}) · die Kinder-PCs stellst du unter <a href="#designs">Designs</a> ein</span></div>
      <div class="dt-grid">${Object.entries(DASH_THEMES).map(([k, d]) => `<button class="dt-card ${curDt === k ? 'on' : ''}" data-dt="${k}"><span class="dt-mini" style="background:${d[1]}"><i style="background:${d[4]};border:1px solid ${d[2]}55"></i><div><i style="background:${d[2]}"></i><i style="background:${k === 'family' ? '#fff' : d[4]};border:1px solid ${d[2]}66"></i><i style="background:${k === 'family' ? '#fff' : d[4]};border:1px solid ${d[3]}66"></i></div></span>${esc(d[0])}</button>`).join('')}</div></section>
    <div class="page-grid">
    <section class="panel"><div class="panel-heading"><h2><span class="accent">🔔</span> Push aufs Handy</h2></div>
      <p>Status auf diesem Gerät: <b>${ps === 'on' ? '✅ aktiv' : ps === 'unsupported' ? '⚠ nicht verfügbar' : 'aus'}</b> · ${j.push_count} Gerät(e) registriert</p>
      ${!S.https ? '<div class="alert">Push funktioniert nur über HTTPS. Sobald Cloudflare mit deiner Domain davor hängt, hier aktivieren.</div>' : ''}
      ${/iPhone|iPad/.test(navigator.userAgent) && !navigator.standalone ? '<div class="note">iPhone: In Safari auf Teilen → „Zum Home-Bildschirm“ tippen, die Webapp von dort öffnen und dann Push aktivieren (iOS 16.4+).</div>' : ''}
      <div class="btn-row"><button class="btn primary" id="pushOn">${ps === 'on' ? 'Neu verbinden' : 'Push aktivieren'}</button><button class="btn" id="pushTest">Test senden</button></div>
      ${tog('push_requests', 'Anfragen der Kinder (mehr Zeit / Webseite)')}${tog('push_limit', 'Zeit abgelaufen / Bettzeit erreicht')}${tog('push_new_app', 'Neue App entdeckt (mit Erlauben / Sperren)')}${tog('push_tamper', 'Manipulationsversuche (App beendet, falsches Masterpasswort …)')}${tog('push_offline', 'PC nicht mehr erreichbar')}
      <label>Absender-Kontakt für Push (E-Mail)<input data-v="push_subject" value="${esc(s.push_subject.replace('mailto:', ''))}"></label></section>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">⚙</span> Allgemein</h2></div><div class="modal-form">
      <label>Familienname<input data-v="family_name" value="${esc(s.family_name)}"></label>
      <label>Zeitzone<input data-v="timezone" value="${esc(s.timezone)}"></label>
      <label>Vorwarnungen vor Zeitende (Minuten, Komma-getrennt)<input data-v="warn_minutes" value="${esc(s.warn_minutes)}"></label>
      <label>Detaildaten aufbewahren (Tage)<input type="number" min="7" max="400" data-v="retention_days" value="${esc(s.retention_days)}"></label>
      ${tog('enforce_extension', 'Browser ohne aktive Schutz-Erweiterung automatisch schließen')}
      <button class="btn primary" id="saveS">Speichern</button></div></section>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">👤</span> Eltern-Konten</h2><button class="btn small" id="uAdd">${I.plus}</button></div>
      <table class="table">${j.users.map(u => `<tr><td>${esc(u.name)}<br><small class="muted">${esc(u.email)}</small></td><td>${u.id === S.me.id ? '<small class="muted">du</small>' : `<button class="btn small" data-udel="${u.id}">${I.trash}</button>`}</td></tr>`).join('')}</table></section>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">🖼</span> Sperrbildschirme</h2></div>
      <div class="lock-prev" style="background-image:url(assets/img/lock/lock-scenery.jpg)"><h3>Ätsch Bätsch – <em>Zeit is um!</em> 🤣</h3><img src="assets/img/lock/awake-robot.png" alt=""><p>Mehr Zeit anfragen · Herunterfahren</p></div>
      <div class="lock-prev" style="margin-top:10px;background-image:url(assets/img/lock/bed-scenery.jpg)"><h3>Zeit fürs <em>Bett</em> 😴</h3><img src="assets/img/lock/sleeping-robot.png" alt=""><p>Gute Nacht und bis morgen!</p></div>
      <p class="hint">So sehen die Sperren auf dem Kinder-PC aus. Außerdem: „Pause! Die Eltern haben den PC gesperrt“ und „Ätsch Bätsch – Seite gesperrt“ im Browser.</p></section>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">☰</span> Protokoll</h2></div><div style="max-height:320px;overflow:auto"><table class="table">${j.audit.map(a => `<tr><td><small>${fmtDT(a.created_at)}</small></td><td>${esc(a.user || '–')}</td><td><small>${esc(a.action)} ${esc((a.detail || '').slice(0, 80))}</small></td></tr>`).join('')}</table></div></section>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">ℹ</span> System</h2></div><p class="muted">Version ${esc(j.version)} · Datenbank ${esc(j.db)} · PHP ${esc(j.php)}</p><p class="muted">${esc(S.copyright)}</p></section></div></div>`;
  $$('[data-dt]', el).forEach(b => b.onclick = run(async () => {
    const t = b.dataset.dt; await api('dash_theme', { theme: t }); S.me.dash_theme = t; applyDash(t);
    $$('[data-dt]', el).forEach(x => x.classList.toggle('on', x === b)); toast(`Dashboard-Design: ${DASH_THEMES[t][0]}`);
  }));
  $('#pushOn').onclick = run(async () => { await enablePush(); toast('Push aktiviert ✓'); VIEWS.settings(el); });
  $('#pushTest').onclick = run(async () => { const r = await api('push_test', {}); toast(r.sent ? `Test an ${r.sent} Gerät(e) gesendet` : 'Kein Gerät erreicht – Push zuerst aktivieren', !r.sent); });
  $$('[data-s]', el).forEach(c => c.onchange = run(async () => { await api('settings_save', { settings: { [c.dataset.s]: c.checked } }); toast('Gespeichert'); }));
  $('#saveS').onclick = run(async () => { const o = {}; $$('[data-v]', el).forEach(i => o[i.dataset.v] = i.value); await api('settings_save', { settings: o }); toast('Einstellungen gespeichert'); S.family = o.family_name; });
  $('#uAdd').onclick = () => modal(`<h2>Weiteres Eltern-Konto</h2><form class="modal-form" id="uF"><label>Name<input name="name" required></label><label>E-Mail<input type="email" name="email" required></label><label>Passwort (mind. 8)<input type="password" name="password" minlength="8" required></label><button class="btn primary">Anlegen</button></form>`, () => {
    $('#uF').onsubmit = run(async e => { e.preventDefault(); const f = new FormData(e.target); await api('user_add', Object.fromEntries(f)); closeModal(); toast('Konto angelegt'); VIEWS.settings(el); });
  });
  $$('[data-udel]', el).forEach(b => b.onclick = run(async () => { if (!await confirmBox('Eltern-Konto löschen?', 'Löschen')) return; await api('user_del', { id: +b.dataset.udel }); VIEWS.settings(el); }));
};

/* ---------- Nachrichten-Center ---------- */
VIEWS.messages = async el => {
  if (!S.kids.length) await loadKids();
  const j = await api('messages');
  el.innerHTML = `<div class="view"><div class="view-title"><h2>Nachrichten an die Kinder</h2></div>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">✉</span> Neue Nachricht</h2></div>
      <form class="modal-form" id="msgF">
        <div class="chips"><label class="chip on"><input type="checkbox" id="mAll" checked hidden> Alle Kinder</label>${S.kids.map(c => `<label class="chip"><input type="checkbox" name="k" value="${c.id}" hidden> ${esc(c.name)}</label>`).join('')}</div>
        <textarea name="t" rows="3" maxlength="600" required placeholder="z. B. Essen ist fertig! 🍝 Bitte in 5 Minuten runterkommen."></textarea>
        <div class="btn-row"><label class="check"><input type="checkbox" name="hi"> ❗ Wichtig (auffälliger, mit Hinweis „WICHTIG“)</label><span style="flex:1"></span><button class="btn primary">${I.mail} Senden</button></div>
        <p class="hint">Erscheint sofort im Vollbild auf dem PC – auch über Spielen und dem Sperrbildschirm – im Design des Kindes. „Verstanden“ schickt dir eine Lesebestätigung. Ist der PC aus, wird die Nachricht beim nächsten Start zugestellt.</p>
      </form></section>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">☰</span> Verlauf</h2></div><div class="scroll-x"><table class="table"><tr><th>Kind / Gerät</th><th>Nachricht</th><th>Gesendet</th><th>Status</th></tr>
      ${j.messages.map(m => `<tr><td><b>${esc(m.child || '–')}</b><br><small class="muted">${esc(m.device || '')}</small></td><td>${m.priority === 'high' ? '<span class="status-tag denied">WICHTIG</span> ' : ''}${esc(m.text)}<br><small class="muted">von ${esc(m.sender)}</small></td><td><small>${fmtDT(m.created_at)}</small></td>
        <td>${m.read_at ? `<span class="status-tag approved">✓✓ gelesen ${fmtDT(m.read_at)}</span>` : m.delivered_at ? `<span class="status-tag pending">✓ zugestellt</span>` : '<span class="status-tag">⏳ wartet auf PC</span>'}</td></tr>`).join('') || '<tr><td colspan="4" class="muted">Noch keine Nachrichten.</td></tr>'}</table></div></section></div>`;
  const all = $('#mAll'), boxes = $$('input[name=k]', el);
  const sync = () => { all.parentElement.classList.toggle('on', all.checked); boxes.forEach(b => b.parentElement.classList.toggle('on', b.checked)); };
  all.onchange = () => { if (all.checked) boxes.forEach(b => b.checked = false); sync(); };
  boxes.forEach(b => b.onchange = () => { if (b.checked) all.checked = false; if (!boxes.some(x => x.checked)) all.checked = true; sync(); });
  $('#msgF').onsubmit = run(async e => {
    e.preventDefault(); const f = new FormData(e.target);
    const r = await api('message_send', { text: f.get('t'), priority: f.get('hi') ? 'high' : 'normal', all: all.checked, child_ids: boxes.filter(b => b.checked).map(b => +b.value) });
    toast(`An ${r.sent} Gerät(e) gesendet` + (r.no_device.length ? ` · ohne PC: ${r.no_device.join(', ')}` : '')); VIEWS.messages(el);
  });
  every(20, async () => { if (location.hash === '#messages' && !document.activeElement.matches('textarea')) await VIEWS.messages(el); });
};

/* ---------- Designs (Kinder-PCs) ---------- */
const THEME_ART = { app_blocked: 'army-app.jpg', game_blocked: 'army-game.jpg' };
function pvHTML(theme, ev, title, text, o) {
  // Nachbildung der Windows-Oberfläche (Designfläche 1600×900)
  const name = o.name || 'Minecraft', fill = s => esc((s || '').replace(/\{name\}/g, name).replace(/\{until\}/g, '20:00').replace(/\{child\}/g, o.child || ''));
  const lockEv = ['time_expired', 'bedtime', 'access_blocked'].includes(ev), msg = ev === 'parent_message';
  const btns = msg ? [['✔', 'Verstanden', 1], ['🕑', 'Später lesen', 0]] : lockEv ? [['🙋', ev === 'time_expired' ? 'Mehr Zeit anfragen' : 'Ausnahme anfragen', 1], ['⏻', 'Herunterfahren', 0]] : [['👪', 'Eltern fragen', 1], ['✕', 'Schließen', 0]];
  const glyph = { time_expired: '⏰', bedtime: '🌙', access_blocked: '✋', app_blocked: '🧩', game_blocked: '🎮', website_blocked: '🌐', parent_message: '✉' }[ev];
  const header = { time_expired: 'Bildschirmzeit', bedtime: 'Schlafenszeit', access_blocked: 'Zugriffskontrolle', app_blocked: 'Familienschutz · App-Kontrolle', game_blocked: 'Familienschutz · Spielzeit', website_blocked: 'Familienschutz · Webfilter', parent_message: 'Nachricht' }[ev];
  const t = fill(title), words = t.trim().split(' '), last = words.pop();
  const titleHTML = words.length ? `${words.join(' ')} <em>${last}</em>` : `<em>${last}</em>`;
  const body = msg ? (o.msg || 'Essen ist fertig! 🍝 Bitte in 5 Minuten runterkommen.') : fill(text);
  const head = `<div class="pv-head"><img src="assets/img/icon-192.png" alt=""><div><b>MS Family Control</b><small>${esc(header)}</small></div></div>`;
  const style = o.accent ? `--pa:${o.accent};` : '';
  const sc = `--fs:${(o.scale || 100) / 100};`;
  if (theme === 'anime') {
    if (ev === 'app_blocked' || ev === 'game_blocked' || (msg && !o.custom)) {
      const img = ev === 'app_blocked' ? 'app-blocked-template.jpg' : ev === 'game_blocked' ? 'game-blocked-template.jpg' : 'message-template.jpg';
      return `<div class="pv pv-tpl" style="background-image:url(assets/img/themes/${img})">${ev === 'app_blocked' ? `<span class="pv-appname">„${esc(name)}“</span>` : ev === 'game_blocked' ? `<span class="pv-gamename">„${esc(name)}“ wurde durch deine Familienregeln blockiert.</span>` : `<span class="pv-from">Von: ${esc(o.from || 'Marc')}</span><span class="pv-time">${esc(o.time || 'Heute · 20:16 Uhr')}</span><span class="pv-msg">${esc(body)}</span>`}
        <p class="pv-note">Vorlage „Anime“: feste Bildtexte, dynamisch nur Name/Nachricht</p></div>`;
    }
    const night = ev === 'bedtime';
    return `<div class="pv pv-anime" style="${style}${sc}background-image:url(assets/img/lock/${night ? 'bed-scenery' : 'lock-scenery'}.jpg)">${head.replace('pv-head', 'pv-head pv-center')}
      <h1 class="pv-title">${titleHTML}</h1><p class="pv-sub">${esc(body)}</p><img class="pv-robot" src="assets/img/lock/${night ? 'sleeping' : 'awake'}-robot.png" alt="">
      <div class="pv-card"><span>${glyph}</span><b>${lockEv ? 'PC gesperrt' : esc(header)}</b></div>
      <div class="pv-btns pv-center">${btns.map(b => `<span class="pv-btn ${b[2] ? 'p' : ''}">${b[0]} ${esc(b[1])}</span>`).join('')}</div><p class="pv-foot">🛡 Nur Eltern können die Sperre aufheben.</p></div>`;
  }
  const art = theme === 'army' ? 'assets/img/themes/army-access.jpg' : theme === 'manga' ? `assets/img/themes/${ev === 'game_blocked' || ev === 'bedtime' || msg ? 'army-game' : 'army-app'}.jpg` : theme === 'family' ? `assets/img/lock/${ev === 'bedtime' ? 'sleeping' : 'awake'}-robot.png` : '';
  return `<div class="pv pv-${theme}" style="${style}${sc}">${art ? `<div class="pv-art" style="background-image:url(${art})"></div>` : `<div class="pv-icon">${glyph}</div>`}
    <div class="pv-col">${head}${msg && o.high ? '<span class="pv-badge">❗ WICHTIG</span>' : ''}<h1 class="pv-title">${titleHTML}</h1>
    <div class="pv-box">${theme !== 'neon' ? `<span class="pv-glyph">${glyph}</span>` : ''}<div>${msg ? `<small>Von: ${esc(o.from || 'Marc')} · ${esc(o.time || 'Heute · 20:16 Uhr')}</small>` : ''}<p>${esc(body)}</p></div></div>
    <div class="pv-btns">${btns.map(b => `<span class="pv-btn ${b[2] ? 'p' : ''}">${b[0]} ${esc(b[1])}</span>`).join('')}</div>
    <p class="pv-foot">🛡 ${msg ? 'Nachrichten von deinen Eltern werden immer angezeigt.' : 'Nur Eltern können die Sperre aufheben.'}</p></div></div>`;
}
VIEWS.designs = async el => {
  const j = await api('themes');
  if (!j.children.length) { el.innerHTML = '<div class="view"><div class="empty panel"><b>Keine Kinder</b>Bitte zuerst Kinderprofile anlegen.</div></div>'; return; }
  if (!S.dsChild || !j.children.find(c => c.id === S.dsChild)) S.dsChild = j.children[0].id;
  const kid = j.children.find(c => c.id === S.dsChild), cfg = JSON.parse(JSON.stringify(kid.config));
  let pvEv = S.dsEv || 'time_expired', pvRes = S.dsRes || '1920x1080';
  const cat = j.catalog;
  el.innerHTML = `<div class="view"><div class="view-title"><h2>Designs der Kinder-PCs</h2><span class="muted">Das Eltern-Dashboard bleibt im Cyberpunk-Design.</span></div>
    <div class="chips">${j.children.map(c => `<button class="chip ${c.id === S.dsChild ? 'on' : ''}" data-dk="${c.id}">${esc(c.name)} · ${esc(cat[c.config.theme].name)}</button>`).join('')}</div>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">🎨</span> Standard-Design für ${esc(kid.name)}</h2></div>
      <div class="theme-grid">${Object.entries(cat).map(([k, t]) => `<button class="theme-card ${cfg.theme === k ? 'on' : ''}" data-th="${k}"><div class="theme-mini">${pvHTML(k, 'game_blocked', j.defaults.game_blocked.title, j.defaults.game_blocked.text, { name: 'Minecraft' })}</div><b>${esc(t.name)}</b><small>${esc(t.desc)}</small><span class="swatches"><i style="background:${t.bg}"></i><i style="background:${t.accent}"></i><i style="background:${t.secondary}"></i></span></button>`).join('')}</div>
      <div class="grid3" style="margin-top:12px"><label>Akzentfarbe <span class="row-inline"><input type="color" id="dAcc" value="${cfg.accent || cat[cfg.theme].accent}"><button class="btn small" id="dAccReset" type="button">Standard</button></span></label>
        <label>Schriftgröße <span class="row-inline"><input type="range" id="dScale" min="80" max="160" step="5" value="${cfg.scale}"><output id="dScaleO">${cfg.scale} %</output></span></label>
        <label class="check" style="margin-top:22px"><input type="checkbox" id="dAnim" ${cfg.anim ? 'checked' : ''}> Einblend-Animationen</label></div></section>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">⚡</span> Design &amp; Text je Ereignis</h2><span class="muted">leer = Standard · Platzhalter: {name} {until} {child}</span></div>
      <div class="scroll-x"><table class="table ev-table"><tr><th>Ereignis</th><th>Design</th><th>Überschrift</th><th>Text</th><th></th></tr>
      ${Object.entries(j.events).map(([k, l]) => `<tr data-ev="${k}"><td><b>${esc(l)}</b></td><td><select data-f="theme"><option value="">Standard (${esc(cat[cfg.theme].name)})</option>${Object.entries(cat).map(([tk, t]) => `<option value="${tk}" ${cfg.events[k] === tk ? 'selected' : ''}>${esc(t.name)}</option>`).join('')}</select></td>
        <td><input data-f="title" maxlength="80" value="${esc(cfg.texts[k].title)}" placeholder="${esc(j.defaults[k].title)}"></td><td><input data-f="text" maxlength="220" value="${esc(cfg.texts[k].text)}" placeholder="${esc(j.defaults[k].text || '(Nachrichtentext vom Portal)')}"></td>
        <td><button class="btn small" data-pv="${k}" type="button">👁</button></td></tr>`).join('')}</table></div>
      <div class="btn-row" style="margin-top:12px"><button class="btn primary" id="dSave">Speichern &amp; an PCs senden</button><button class="btn" id="dCopy">Auf alle Kinder übertragen</button><button class="btn danger" id="dReset">Zurücksetzen</button></div></section>
    <section class="panel"><div class="panel-heading"><h2><span class="accent">👁</span> Live-Vorschau</h2>
      <div class="btn-row"><select id="pvEv">${Object.entries(j.events).map(([k, l]) => `<option value="${k}" ${k === pvEv ? 'selected' : ''}>${esc(l)}</option>`).join('')}</select>
      <select id="pvRes">${['1920x1080', '2560x1440', '3840x2160', '1366x768', '2560x1080'].map(r => `<option ${r === pvRes ? 'selected' : ''}>${r}</option>`).join('')}</select>
      <input id="pvName" value="Minecraft" style="width:140px" title="Beispielname für {name}"><label class="check"><input type="checkbox" id="pvHigh"> wichtig</label></div></div>
      <div class="pv-frame" id="pvFrame"></div><p class="hint">Vorschau im Browser – so nah wie möglich an der Windows-Darstellung. Keine echte Sperre. Auflösung: <span id="pvResL"></span></p></section></div>`;
  const read = () => {
    $$('.ev-table tr[data-ev]', el).forEach(tr => { const k = tr.dataset.ev; cfg.events[k] = $('[data-f=theme]', tr).value; cfg.texts[k] = { title: $('[data-f=title]', tr).value, text: $('[data-f=text]', tr).value }; });
    cfg.scale = +$('#dScale').value; cfg.anim = $('#dAnim').checked;
  };
  const draw = () => {
    read();
    const ev = $('#pvEv').value, [w, h] = $('#pvRes').value.split('x').map(Number), th = cfg.events[ev] || cfg.theme;
    const t = cfg.texts[ev].title || j.defaults[ev].title, tx = cfg.texts[ev].text || j.defaults[ev].text;
    const fr = $('#pvFrame'); fr.style.aspectRatio = `${w} / ${h}`;
    fr.innerHTML = pvHTML(th, ev, t, tx, { name: $('#pvName').value, accent: cfg.accent, scale: cfg.scale, high: $('#pvHigh').checked, custom: !!(cfg.texts[ev].title || cfg.texts[ev].text), child: kid.name });
    const pv = $('.pv', fr); const s = Math.min(fr.clientWidth / 1600, fr.clientHeight / 900); pv.style.transform = `translate(-50%,-50%) scale(${s})`;
    const ttl = $('.pv-title', pv); if (ttl && !pv.classList.contains('pv-anime')) { let fs = parseFloat(getComputedStyle(ttl).fontSize), max = pv.classList.contains('pv-neon') ? 136 : 270; while (ttl.scrollHeight > max + 4 && fs > 30) { fs *= 0.92; ttl.style.fontSize = fs + 'px'; } }
    $('#pvResL').textContent = `${w} × ${h} · Design „${cat[th].name}“`;
    S.dsEv = ev; S.dsRes = $('#pvRes').value;
  };
  const sizeMinis = () => $$('.theme-mini', el).forEach(m => { const p = $('.pv', m); p.style.transform = `translate(-50%,-50%) scale(${m.clientWidth / 1600})`; });
  sizeMinis(); draw();
  window.onresize = () => { if (location.hash === '#designs') { sizeMinis(); draw(); } };
  $$('[data-dk]', el).forEach(b => b.onclick = () => { S.dsChild = +b.dataset.dk; VIEWS.designs(el); });
  $$('[data-th]', el).forEach(b => b.onclick = () => { cfg.theme = b.dataset.th; $$('[data-th]', el).forEach(x => x.classList.toggle('on', x === b)); if (!cfg.accent) $('#dAcc').value = cat[cfg.theme].accent; $$('[data-f=theme] option[value=""]', el).forEach(o => o.textContent = `Standard (${cat[cfg.theme].name})`); draw(); });
  $('#dAcc').oninput = () => { cfg.accent = $('#dAcc').value; draw(); };
  $('#dAccReset').onclick = () => { cfg.accent = ''; $('#dAcc').value = cat[cfg.theme].accent; draw(); };
  $('#dScale').oninput = () => { $('#dScaleO').textContent = $('#dScale').value + ' %'; draw(); };
  $$('.ev-table input, .ev-table select', el).forEach(i => i.addEventListener('input', draw));
  $$('[data-pv]', el).forEach(b => b.onclick = () => { $('#pvEv').value = b.dataset.pv; draw(); $('#pvFrame').scrollIntoView({ behavior: 'smooth', block: 'center' }); });
  ['#pvEv', '#pvRes', '#pvName', '#pvHigh'].forEach(id => $(id).addEventListener('input', draw));
  $('#dSave').onclick = run(async () => { read(); await api('theme_save', { child_id: kid.id, theme: cfg.theme, accent: cfg.accent, scale: cfg.scale, anim: cfg.anim, events: cfg.events, texts: cfg.texts }); toast(`Design für ${kid.name} gespeichert – der PC übernimmt es in ca. 15 Sekunden`); VIEWS.designs(el); });
  $('#dCopy').onclick = run(async () => { if (!await confirmBox(`Die gespeicherten Design-Einstellungen von <b>${esc(kid.name)}</b> auf alle anderen Kinder übertragen?`, 'Übertragen', false)) return; await api('theme_copy', { child_id: kid.id }); toast('Übertragen'); VIEWS.designs(el); });
  $('#dReset').onclick = run(async () => { if (!await confirmBox(`Design von <b>${esc(kid.name)}</b> auf Standard (Anime/Cyberpunk) zurücksetzen?`, 'Zurücksetzen')) return; await api('theme_reset', { child_id: kid.id }); VIEWS.designs(el); });
};

/* ---------- Mehr (Handy) ---------- */
VIEWS.more = async el => {
  el.innerHTML = `<div class="view"><div class="view-title"><h2>Mehr</h2></div><div class="more-grid">${NAV.filter(n => !['dashboard', 'kids', 'requests', 'alerts'].includes(n[0])).map(([k, l, ic]) => `<a class="btn" href="#${k}">${I[ic]}<span>${esc(l)}</span></a>`).join('')}
    <button class="btn danger" id="lo">${I.logout}<span>Abmelden</span></button></div></div>`;
  $('#lo').onclick = run(async () => { await api('logout', {}); S.me = null; await boot(); });
};

/* ---------- Dashboard-Design ---------- */
const DASH_THEMES = { anime: ['Anime / Cyberpunk', '#060a1c', '#00e9ff', '#f23ee9', '#11142a'], neon: ['Modern / Neon Tech', '#070b16', '#00d6ff', '#ab31f5', '#0a1224'], family: ['Family / Friendly', '#eef7ff', '#1469d8', '#22ba94', '#ffffff'],
  army: ['Army / Tactical', '#15191e', '#f6b800', '#ed5339', '#111417'], manga: ['Anime / Charaktere', '#120a2a', '#ff2d6f', '#21e6ff', '#170c30'], auto: ['Automatisch (hell/dunkel)', '#eef7ff', '#1469d8', '#00e9ff', '#060a1c'] };
function applyDash(t) {
  try { localStorage.setItem('msfc_dt', t); } catch (e) {}
  let r = t === 'auto' ? (window.matchMedia && matchMedia('(prefers-color-scheme: light)').matches ? 'family' : 'anime') : t;
  document.documentElement.setAttribute('data-dt', DASH_THEMES[r] ? r : 'anime');
  const m = document.querySelector('meta[name=theme-color]'); if (m) m.content = r === 'family' ? '#eef7ff' : (DASH_THEMES[r] || DASH_THEMES.anime)[1];
}
if (window.matchMedia) matchMedia('(prefers-color-scheme: light)').addEventListener('change', () => { if (S.me && S.me.dash_theme === 'auto') applyDash('auto'); });

/* ---------- Start ---------- */
async function boot() {
  const j = await api('me');
  Object.assign(S, { me: j.user, csrf: j.csrf, family: j.family, version: j.version, copyright: j.copyright, https: j.https });
  if (!S.me) { renderLogin(); return; }
  applyDash(S.me.dash_theme || 'anime');
  shell(); route();
}
if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('sw.js').catch(() => {});
  navigator.serviceWorker.addEventListener('message', e => { if (e.data && e.data.type === 'push' && S.me) { toast(e.data.data.title || 'Neue Benachrichtigung'); route(); } });
}
document.addEventListener('visibilitychange', () => { if (!document.hidden && S.me && ['', '#dashboard', '#requests'].includes(location.hash)) route(); });
boot().catch(e => { $('#root').innerHTML = `<div class="boot"><img src="assets/img/icon-192.png" alt=""><span>${esc(e.message)}</span><button class="btn" id="rl">Neu laden</button></div>`; const b = document.getElementById('rl'); if (b) b.onclick = () => location.reload(); });
})();
