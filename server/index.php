<?php
declare(strict_types=1);
require __DIR__ . '/lib/bootstrap.php';
if (!installed()) {
    header('Location: install.php');
    exit;
}
header('X-Frame-Options: DENY');
header('X-Content-Type-Options: nosniff');
header('Referrer-Policy: same-origin');
header("Content-Security-Policy: default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; manifest-src 'self'; worker-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'");
$v = APP_VERSION . '-' . substr(md5((string)@filemtime(__DIR__ . '/assets/js/app.js') . @filemtime(__DIR__ . '/assets/css/app.css')), 0, 8);
?><!doctype html>
<html lang="de">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<meta name="theme-color" content="#080d23">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
<meta name="apple-mobile-web-app-title" content="Family Control">
<title>Family Control Center</title>
<link rel="manifest" href="manifest.webmanifest">
<link rel="icon" type="image/png" href="assets/img/favicon-32.png">
<link rel="apple-touch-icon" href="assets/img/icon-180.png">
<link rel="stylesheet" href="assets/css/design.css?v=<?= $v ?>">
<link rel="stylesheet" href="assets/css/app.css?v=<?= $v ?>">
<link rel="stylesheet" href="assets/css/themes.css?v=<?= $v ?>">
<script src="assets/js/theme-init.js?v=<?= $v ?>"></script>
</head>
<body>
<div id="root"><div class="boot"><img src="assets/img/icon-192.png" alt=""><span>Family Control Center lädt …</span></div></div>
<div class="toast" id="toast" role="status" aria-live="polite"></div>
<div class="modal" id="modal" aria-hidden="true"><div class="modal-box panel" role="dialog" aria-modal="true"><button class="modal-x" data-close aria-label="Schließen">✕</button><div id="modalBody"></div></div></div>
<script src="assets/js/app.js?v=<?= $v ?>"></script>
</body>
</html>
