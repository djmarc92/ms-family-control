<?php
declare(strict_types=1);
/*
 * Einrichtungsassistent – legt Datenbank (SQLite oder MariaDB/MySQL), Eltern-Konto und Grundeinstellungen an.
 * Nach erfolgreicher Installation gesperrt (data/config.php existiert).
 */
require __DIR__ . '/lib/bootstrap.php';

header('X-Frame-Options: DENY');
header('X-Content-Type-Options: nosniff');

if (installed()) {
    header('Location: ./');
    exit;
}

session_name('MSFCSETUP');
session_start();
if (empty($_SESSION['csrf'])) {
    $_SESSION['csrf'] = bin2hex(random_bytes(16));
}
$h = fn($s) => htmlspecialchars((string)$s, ENT_QUOTES, 'UTF-8');
$preset = is_file(DATA . '/install-preset.json') ? (json_decode((string)file_get_contents(DATA . '/install-preset.json'), true) ?: []) : [];
$tokenFile = DATA . '/setup-token.txt';
$needToken = is_file($tokenFile);

$checks = [
    'PHP ≥ 8.1' => version_compare(PHP_VERSION, '8.1.0', '>='),
    'PDO SQLite oder MySQL' => extension_loaded('pdo_sqlite') || extension_loaded('pdo_mysql'),
    'OpenSSL (Push, Sicherheit)' => extension_loaded('openssl'),
    'cURL (Push, Ferien-Import)' => extension_loaded('curl'),
    'GD (Profilbilder)' => extension_loaded('gd'),
    'mbstring' => extension_loaded('mbstring'),
    'Ordner data/ beschreibbar' => is_writable(DATA),
    'Ordner uploads/ beschreibbar' => is_dir(ROOT . '/uploads') ? is_writable(ROOT . '/uploads') : is_writable(ROOT),
];
$allOk = !in_array(false, $checks, true);
$error = '';
$done = false;

if ($_SERVER['REQUEST_METHOD'] === 'POST' && $allOk) {
    try {
        if (!hash_equals($_SESSION['csrf'], (string)($_POST['csrf'] ?? ''))) {
            throw new RuntimeException('Formular abgelaufen – bitte neu laden.');
        }
        if ($needToken && !hash_equals(trim((string)file_get_contents($tokenFile)), trim((string)($_POST['token'] ?? '')))) {
            throw new RuntimeException('Einrichtungscode falsch. Er wurde am Ende von install.sh angezeigt (data/setup-token.txt).');
        }
        $name = trim((string)($_POST['name'] ?? ''));
        $email = mb_strtolower(trim((string)($_POST['email'] ?? '')));
        $pw = (string)($_POST['password'] ?? '');
        if ($name === '' || !filter_var($email, FILTER_VALIDATE_EMAIL)) {
            throw new RuntimeException('Bitte Name und gültige E-Mail angeben.');
        }
        if (mb_strlen($pw) < 8 || $pw !== ($_POST['password2'] ?? '')) {
            throw new RuntimeException('Passwort: mindestens 8 Zeichen, beide Eingaben müssen gleich sein.');
        }
        $driver = ($_POST['driver'] ?? 'sqlite') === 'mysql' ? 'mysql' : 'sqlite';
        if ($driver === 'mysql') {
            $dbc = ['driver' => 'mysql', 'host' => trim((string)$_POST['db_host']) ?: '127.0.0.1', 'port' => (int)($_POST['db_port'] ?: 3306),
                'name' => trim((string)$_POST['db_name']), 'user' => trim((string)$_POST['db_user']), 'pass' => (string)$_POST['db_pass']];
            if ($dbc['name'] === '' || $dbc['user'] === '') {
                throw new RuntimeException('Bitte Datenbankname und Benutzer angeben.');
            }
        } else {
            $dbc = ['driver' => 'sqlite', 'path' => DATA . '/family.sqlite'];
        }
        $pdo = db_connect($dbc);
        migrate($pdo);
        $cfg = ['db' => $dbc, 'secret' => bin2hex(random_bytes(32)), 'installed_at' => date('c')];
        file_put_contents(DATA . '/config.php', "<?php\n// MS Family Control – automatisch erzeugt. Nicht weitergeben!\nreturn " . var_export($cfg, true) . ";\n", LOCK_EX);
        @chmod(DATA . '/config.php', 0640);
        cfg(true);
        $st = $pdo->prepare('INSERT INTO users (name,email,pass_hash,created_at) VALUES (?,?,?,?)');
        $st->execute([mb_substr($name, 0, 80), $email, password_hash($pw, PASSWORD_DEFAULT), date('Y-m-d H:i:s')]);
        $done = true;
    } catch (Throwable $e) {
        @unlink(DATA . '/config.php');
        $error = $e->getMessage();
    }
    if ($done) {
        try {
            seed_defaults();
            set_setting('family_name', mb_substr(trim((string)($_POST['family'] ?? 'Familie')), 0, 60) ?: 'Familie');
            $state = preg_match('/^[A-Z]{2}$/', (string)($_POST['state'] ?? '')) ? $_POST['state'] : 'NW';
            set_setting('state', $state);
            set_setting('push_subject', 'mailto:' . $email);
            vapid_keys();
        } catch (Throwable $e) {
            $error = 'Grunddaten: ' . $e->getMessage();
        }
        @unlink($tokenFile);
        @unlink(DATA . '/install-preset.json');
    }
}
$states = ['BW' => 'Baden-Württemberg', 'BY' => 'Bayern', 'BE' => 'Berlin', 'BB' => 'Brandenburg', 'HB' => 'Bremen', 'HH' => 'Hamburg', 'HE' => 'Hessen', 'MV' => 'Mecklenburg-Vorpommern',
    'NI' => 'Niedersachsen', 'NW' => 'Nordrhein-Westfalen', 'RP' => 'Rheinland-Pfalz', 'SL' => 'Saarland', 'SN' => 'Sachsen', 'ST' => 'Sachsen-Anhalt', 'SH' => 'Schleswig-Holstein', 'TH' => 'Thüringen'];
$v = fn($k, $d = '') => $h($_POST[$k] ?? $preset[$k] ?? $d);
?><!doctype html>
<html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Einrichtung · MS Family Control</title><link rel="icon" href="assets/img/icon-192.png"><link rel="stylesheet" href="assets/css/app.css">
</head><body class="setup-body">
<div class="setup-card panel">
  <div class="setup-head"><img src="assets/img/icon-192.png" alt=""><div><h1>Family Control Center</h1><p>Einrichtung · Version <?= APP_VERSION ?></p></div></div>
<?php if ($done && !$error): ?>
  <h2>✅ Fertig eingerichtet</h2>
  <p>Melde dich jetzt mit deinem Eltern-Konto an. Danach unter <b>Kinder-Profile</b> die Kinder anlegen und unter <b>Geräte</b> einen Kopplungscode für die Windows-App erzeugen.</p>
  <p class="hint">Tipp fürs iPhone: Seite in Safari öffnen → Teilen → „Zum Home-Bildschirm“. Push-Benachrichtigungen benötigen HTTPS (z. B. über Cloudflare).</p>
  <a class="btn primary" href="./">Zum Control Center</a>
<?php else: ?>
  <?php if ($error): ?><div class="alert"><?= $h($error) ?></div><?php endif; ?>
  <h2>1 · Systemprüfung</h2>
  <ul class="checks"><?php foreach ($checks as $k => $ok): ?><li class="<?= $ok ? 'ok' : 'bad' ?>"><?= $ok ? '✔' : '✖' ?> <?= $h($k) ?></li><?php endforeach; ?></ul>
  <?php if (!$allOk): ?><p class="alert">Bitte die fehlenden Punkte beheben (am einfachsten mit <code>sudo bash install.sh</code>) und die Seite neu laden.</p><?php else: ?>
  <form method="post" class="setup-form" autocomplete="off">
    <input type="hidden" name="csrf" value="<?= $h($_SESSION['csrf']) ?>">
    <?php if ($needToken): ?>
    <h2>2 · Einrichtungscode</h2>
    <label>Code aus install.sh<input name="token" required value="<?= $v('token') ?>" placeholder="z. B. 7F3K-92QX"></label>
    <?php endif; ?>
    <h2><?= $needToken ? 3 : 2 ?> · Datenbank</h2>
    <div class="radio-row">
      <label class="radio"><input type="radio" name="driver" value="sqlite" <?= ($v('driver', 'sqlite') === 'sqlite') ? 'checked' : '' ?>> SQLite <small>(empfohlen, nichts weiter nötig)</small></label>
      <label class="radio"><input type="radio" name="driver" value="mysql" <?= ($v('driver') === 'mysql') ? 'checked' : '' ?>> MariaDB / MySQL</label>
    </div>
    <div class="grid2 mysql-fields">
      <label>Host<input name="db_host" value="<?= $v('db_host', '127.0.0.1') ?>"></label>
      <label>Port<input name="db_port" value="<?= $v('db_port', '3306') ?>"></label>
      <label>Datenbank<input name="db_name" value="<?= $v('db_name', 'family_control') ?>"></label>
      <label>Benutzer<input name="db_user" value="<?= $v('db_user', 'family') ?>"></label>
      <label class="span2">Passwort<input type="password" name="db_pass" value="<?= $v('db_pass') ?>"></label>
    </div>
    <h2><?= $needToken ? 4 : 3 ?> · Familie &amp; Eltern-Konto</h2>
    <div class="grid2">
      <label>Familienname<input name="family" value="<?= $v('family', 'Familie Straßen') ?>"></label>
      <label>Bundesland (Ferien)<select name="state"><?php foreach ($states as $k => $n): ?><option value="<?= $k ?>" <?= $v('state', 'NW') === $k ? 'selected' : '' ?>><?= $n ?></option><?php endforeach; ?></select></label>
      <label>Dein Name<input name="name" required value="<?= $v('name', 'Marc') ?>"></label>
      <label>E-Mail (Login)<input type="email" name="email" required value="<?= $v('email') ?>"></label>
      <label>Passwort<input type="password" name="password" required minlength="8"></label>
      <label>Passwort wiederholen<input type="password" name="password2" required minlength="8"></label>
    </div>
    <button class="btn primary wide" type="submit">Installation starten</button>
  </form>
  <?php endif; ?>
<?php endif; ?>
  <p class="copyright"><?= $h(COPYRIGHT) ?></p>
</div>
<script>
const f=document.querySelector('.mysql-fields');function t(){if(!f)return;const m=document.querySelector('input[name=driver]:checked');f.style.display=m&&m.value==='mysql'?'grid':'none'}
document.querySelectorAll('input[name=driver]').forEach(r=>r.addEventListener('change',t));t();
</script>
</body></html>
