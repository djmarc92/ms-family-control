const api = typeof browser !== 'undefined' ? browser : chrome;
const qs = new URLSearchParams(location.search);
const domain = qs.get('d') || '', host = qs.get('h') || domain;
let minutes = 30, reason = 'school';
document.getElementById('dom').textContent = host;
const cat = qs.get('c') || '';
if (cat) { document.getElementById('why').textContent = 'Gesperrt durch Filter: ' + cat; document.getElementById('sub').textContent = 'Diese Webseite ist für Kinder nicht geeignet.'; }
document.title = host + ' gesperrt – MS Family Control';
const st = document.getElementById('status');
function chips(id, attr, set) { document.querySelectorAll('#' + id + ' button').forEach(b => b.addEventListener('click', () => { document.querySelectorAll('#' + id + ' button').forEach(x => x.classList.toggle('on', x === b)); set(b.dataset[attr]); })); }
chips('mins', 'm', v => minutes = +v); chips('reasons', 'r', v => reason = v);
function send(m) { return new Promise(res => api.runtime.sendMessage(m, r => res(r || {}))); }
let themed = false;
function applyTheme(t) {
  if (!t || themed) return; themed = true;
  const th = ['anime', 'neon', 'family', 'army', 'manga'].includes(t.theme) ? t.theme : 'anime';
  document.body.classList.add('th-' + th);
  if (t.accent && /^#[0-9a-f]{6}$/i.test(t.accent)) document.body.style.setProperty('--accent', t.accent);
  const fill = s => (s || '').replace(/\{name\}/g, host);
  if (th !== 'anime') {
    const h1 = document.querySelector('h1'); const title = fill(t.title) || 'Diese Webseite ist nicht erlaubt!';
    const i = title.trim().lastIndexOf(' ');
    h1.textContent = '';
    if (i > 0) { h1.append(title.slice(0, i + 1)); const em = document.createElement('em'); em.textContent = title.slice(i + 1); h1.append(em); } else h1.textContent = title;
    if (!cat) document.getElementById('sub').textContent = fill(t.text);
    const img = document.querySelector('.robot');
    img.src = th === 'army' ? 'army-access.jpg' : th === 'manga' ? 'army-app.jpg' : th === 'neon' ? 'icon-128.png' : 'robot.png';
  }
}
async function refresh() {
  const p = await send({ type: 'policy' });
  if (p && p.theme) applyTheme(p.theme);
  if (!p || !p.ok) { st.textContent = '⚠ Die Familien-App auf diesem PC antwortet gerade nicht.'; return; }
  const now = Date.now();
  const granted = (p.grants || []).some(g => (host === g.domain || host.endsWith('.' + g.domain) || g.domain === domain) && new Date(g.until).getTime() > now);
  if (granted || (!cat && !(p.blocked || []).some(d => host === d || host.endsWith('.' + d)))) {
    st.className = 'status ok'; st.textContent = '✅ Freigegeben! Seite wird geöffnet …'; setTimeout(() => location.href = 'https://' + host + '/', 900); return;
  }
  const r = (p.requests || []).filter(x => x.domain === domain || x.domain === host).pop();
  if (r) {
    st.className = 'status ' + (r.status === 'denied' ? 'bad' : r.status === 'approved' ? 'ok' : 'wait');
    st.textContent = r.status === 'denied' ? '❌ Deine Eltern haben die Freigabe abgelehnt.' : r.status === 'approved' ? '✅ Genehmigt!' : '⏳ Anfrage gesendet – warte auf Antwort deiner Eltern …';
  }
}
document.getElementById('send').addEventListener('click', async () => {
  st.className = 'status wait'; st.textContent = 'Sende …';
  const r = await send({ type: 'request', domain: host, minutes, reason, message: document.getElementById('msg').value });
  if (r.ok) { st.textContent = '📨 Anfrage gesendet! Deine Eltern bekommen eine Nachricht aufs Handy.'; }
  else { st.className = 'status bad'; st.textContent = '⚠ ' + (r.error || 'Konnte nicht gesendet werden'); }
});
document.getElementById('check').addEventListener('click', refresh);
document.getElementById('back').addEventListener('click', () => { history.length > 2 ? history.go(-2) : location.href = 'about:blank'; });
refresh(); setInterval(refresh, 10000);
