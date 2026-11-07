/* MS Family Control – Service Worker: Offline-Hülle + Web-Push */
const CACHE = 'msfc-v2';
const SHELL = ['offline.html', 'assets/img/icon-192.png', 'assets/css/design.css', 'assets/css/app.css'];
self.addEventListener('install', e => { e.waitUntil(caches.open(CACHE).then(c => c.addAll(SHELL)).then(() => self.skipWaiting())); });
self.addEventListener('activate', e => { e.waitUntil(caches.keys().then(k => Promise.all(k.filter(x => x !== CACHE).map(x => caches.delete(x)))).then(() => self.clients.claim())); });
self.addEventListener('fetch', e => {
  const r = e.request;
  if (r.method !== 'GET') return;
  const u = new URL(r.url);
  if (u.origin !== location.origin || u.pathname.endsWith('.php') && !u.pathname.endsWith('/index.php')) return;
  if (r.mode === 'navigate') { e.respondWith(fetch(r).catch(() => caches.match('offline.html'))); return; }
  if (u.pathname.includes('/assets/')) {
    e.respondWith(caches.match(r).then(c => { const f = fetch(r).then(res => { if (res.ok) caches.open(CACHE).then(cc => cc.put(r, res.clone())); return res; }).catch(() => c); return c || f; }));
  }
});
self.addEventListener('push', e => {
  let d = {};
  try { d = e.data ? e.data.json() : {}; } catch (_) { d = { title: 'Family Control', body: e.data ? e.data.text() : '' }; }
  e.waitUntil((async () => {
    await self.registration.showNotification(d.title || 'Family Control', { body: d.body || '', tag: d.tag || undefined, renotify: !!d.tag, icon: 'assets/img/icon-192.png', badge: 'assets/img/icon-192.png', data: { url: d.url || './', actions: d.actions || [] }, actions: (d.actions || []).slice(0, 2).map(a => ({ action: a.action, title: a.title })) });
    const cl = await self.clients.matchAll({ type: 'window' });
    cl.forEach(c => c.postMessage({ type: 'push', data: d }));
    if (self.navigator && 'setAppBadge' in self.navigator) { try { await self.navigator.setAppBadge(); } catch (_) {} }
  })());
});
self.addEventListener('notificationclick', e => {
  e.notification.close();
  const dd = e.notification.data || {}, act = e.action && (dd.actions || []).find(a => a.action === e.action);
  const url = new URL((act && act.url) || dd.url || './', self.registration.scope).href;
  e.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(cl => {
    for (const c of cl) { if (c.url.startsWith(self.registration.scope)) { c.navigate(url).catch(() => {}); return c.focus(); } }
    return self.clients.openWindow(url);
  }));
});
