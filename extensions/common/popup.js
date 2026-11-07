const api = typeof browser !== 'undefined' ? browser : chrome;
api.runtime.sendMessage({ type: 'policy' }, p => {
  const s = document.getElementById('state'), w = document.getElementById('who');
  if (!p || !p.ok) { w.textContent = 'Familien-App nicht erreichbar'; s.className = 'status bad'; s.textContent = '⚠ Die Windows-App läuft nicht. Gesperrte Seiten bleiben trotzdem gesperrt.'; return; }
  w.textContent = 'Geschützt · ' + p.child;
  s.className = 'status ok';
  s.textContent = p.paused ? '⏸ Schutz von den Eltern pausiert' : '✅ Aktiv · ' + (p.blocked || []).length + ' gesperrte Seiten' + (p.server_ok ? '' : ' · offline');
});
