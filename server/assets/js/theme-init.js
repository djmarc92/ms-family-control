/* Dashboard-Design sofort setzen (vor dem ersten Zeichnen, kein Flackern) */
(function () {
  var t = 'anime';
  try { t = localStorage.getItem('msfc_dt') || 'anime'; } catch (e) {}
  if (t === 'auto') t = window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches ? 'family' : 'anime';
  document.documentElement.setAttribute('data-dt', t);
})();
