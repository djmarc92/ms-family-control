<?php
declare(strict_types=1);
/*
 * Geräte-API für den Windows-Agenten (Bearer-Token je Gerät).
 * Endpunkte: ping, pair, sync, request
 */
require __DIR__ . '/lib/bootstrap.php';

header('X-Content-Type-Options: nosniff');
if (!installed()) {
    fail('Server ist noch nicht eingerichtet', 503);
}
$a = $_GET['a'] ?? '';

if ($a === 'ping') {
    json_out(['ok' => true, 'app' => APP_NAME, 'version' => APP_VERSION, 'family' => setting('family_name', 'Familie')]);
}

if ($_SERVER['REQUEST_METHOD'] !== 'POST' && $a !== 'filter' && $a !== 'update') {
    fail('POST erwartet', 405);
}
$b = body();

if ($a === 'pair') {
    if (!throttle_hit('pair:' . client_ip(), 10, 600)) {
        fail('Zu viele Versuche – bitte 10 Minuten warten.', 429);
    }
    $code = strtoupper(preg_replace('/[^A-Za-z0-9]/', '', (string)($b['code'] ?? '')));
    $pc = q1('SELECT * FROM pair_codes WHERE code=? AND used_at IS NULL AND expires_at > ?', [$code, now()]);
    if (!$pc) {
        fail('Kopplungscode ungültig oder abgelaufen. Bitte im Control Center einen neuen erzeugen.', 403);
    }
    $child = q1('SELECT * FROM children WHERE id=?', [$pc['child_id']]);
    if (!$child) {
        fail('Kinderprofil existiert nicht mehr', 404);
    }
    $token = bin2hex(random_bytes(32));
    $name = mb_substr(trim((string)($b['name'] ?? 'Windows-PC')), 0, 80) ?: 'Windows-PC';
    $id = db_insert('devices', [
        'child_id' => (int)$child['id'], 'name' => $name, 'token_hash' => hash('sha256', $token),
        'os' => mb_substr((string)($b['os'] ?? ''), 0, 120), 'version' => mb_substr((string)($b['version'] ?? ''), 0, 20),
        'last_seen' => now(), 'last_ip' => client_ip(), 'created_at' => now(),
    ]);
    q('UPDATE pair_codes SET used_at=? WHERE code=?', [now(), $code]);
    add_event((int)$child['id'], $id, 'paired', 'info', 'Neues Gerät gekoppelt: ' . $name, 'Für ' . $child['name']);
    json_out(['ok' => true, 'device_id' => $id, 'token' => $token, 'child' => ['id' => (int)$child['id'], 'name' => $child['name']]]);
}

// --- ab hier Geräte-Authentifizierung ---
$auth = $_SERVER['HTTP_AUTHORIZATION'] ?? $_SERVER['REDIRECT_HTTP_AUTHORIZATION'] ?? '';
if (!$auth && function_exists('getallheaders')) {
    foreach (getallheaders() as $k => $v) {
        if (strcasecmp($k, 'Authorization') === 0) {
            $auth = $v;
        }
    }
}
if (!preg_match('/^Bearer\s+([a-f0-9]{64})$/i', trim($auth), $m)) {
    fail('Nicht autorisiert', 401);
}
$device = q1('SELECT * FROM devices WHERE token_hash=?', [hash('sha256', strtolower($m[1]))]);
if (!$device) {
    fail('Gerät unbekannt', 401);
}
if ((int)$device['revoked'] === 1) {
    json_out(['ok' => false, 'revoked' => true, 'error' => 'Gerät wurde im Control Center entfernt'], 410);
}
$cid = (int)$device['child_id'];

if ($a === 'sync') {
    $day0 = today();
    if (($b['status']['platform'] ?? '') === 'android' || stripos((string)$device['os'], 'android') !== false) {
        apps_android_seed(); // Android-Paketnamen bekannter Apps vor der Erkennung ergänzen
    }
    $newApps = [];
    // Nutzungsdaten (idempotent über batch-id)
    foreach (array_slice((array)($b['batches'] ?? []), 0, 200) as $bt) {
        $bid = substr(preg_replace('/[^A-Za-z0-9-]/', '', (string)($bt['id'] ?? '')), 0, 40);
        $day = (string)($bt['day'] ?? '');
        if ($bid === '' || !preg_match('/^\d{4}-\d{2}-\d{2}$/', $day) || $day > $day0) {
            continue;
        }
        if (qv('SELECT 1 FROM batches WHERE device_id=? AND batch_id=?', [$device['id'], $bid])) {
            continue;
        }
        db()->beginTransaction();
        try {
            db_insert('batches', ['device_id' => $device['id'], 'batch_id' => $bid, 'created_at' => now()]);
            foreach ((array)($bt['hours'] ?? []) as $h => $sec) {
                $h = (int)$h;
                $sec = min(3600, max(0, (int)$sec));
                if ($h >= 0 && $h < 24 && $sec > 0) {
                    db_add('usage_hourly', ['child_id' => $cid, 'day' => $day, 'hour' => $h], 'sec', $sec);
                }
            }
            foreach (array_slice((array)($bt['apps'] ?? []), 0, 100, true) as $exe => $info) {
                $exe = mb_substr(strtolower((string)$exe), 0, 120);
                $sec = min(86400, max(0, (int)($info['sec'] ?? 0)));
                if ($exe !== '' && $sec > 0) {
                    db_add('usage_apps', ['child_id' => $cid, 'day' => $day, 'app' => $exe], 'sec', $sec, ['label' => mb_substr((string)($info['label'] ?? $exe), 0, 160)]);
                    if (empty($info['sys'])) { // System-Apps (Startbildschirm, Tastatur …) nie als „neu“ melden
                        $newApps[$exe] = (string)($info['label'] ?? '');
                    }
                }
            }
            foreach (array_slice((array)($bt['web'] ?? []), 0, 200, true) as $dom => $sec) {
                $dom = clean_domain((string)$dom);
                $sec = min(86400, max(0, (int)$sec));
                if ($dom !== '' && $sec > 0) {
                    db_add('usage_web', ['child_id' => $cid, 'day' => $day, 'domain' => $dom], 'sec', $sec);
                }
            }
            db()->commit();
        } catch (Throwable $e) {
            db()->rollBack();
        }
    }
    // Ereignisse
    $child = q1('SELECT name FROM children WHERE id=?', [$cid]);
    $cname = $child['name'] ?? 'Kind';
    $allowed = ['lock' => 'info', 'blocked_app' => 'warn', 'blocked_site' => 'info', 'tamper' => 'warn', 'master_fail' => 'warn',
        'ext_missing' => 'warn', 'uninstall' => 'warn', 'parent_unlock' => 'info', 'time_jump' => 'warn', 'agent_start' => 'info', 'warning' => 'info', 'browser_closed' => 'warn', 'message_read' => 'info', 'unknown_app' => 'warn', 'power_on' => 'warn', 'power_on_ok' => 'info', 'power_off' => 'info', 'app_update' => 'info'];
    $attempts = [];
    foreach (array_slice((array)($b['events'] ?? []), 0, 50) as $ev) {
        $type = (string)($ev['type'] ?? '');
        if (!isset($allowed[$type])) {
            continue;
        }
        if ($type === 'unknown_app') { // strenger Modus: unbekanntes Programm wurde am PC blockiert
            $attempts[mb_substr(strtolower((string)($ev['detail'] ?? '')), 0, 120)] = mb_substr((string)($ev['title'] ?? ''), 0, 160);
            continue;
        }
        $ts = isset($ev['ts']) && strtotime((string)$ev['ts']) ? date('Y-m-d H:i:s', strtotime((string)$ev['ts'])) : now();
        $title = mb_substr(strip_tags((string)($ev['title'] ?? $type)), 0, 190);
        if (in_array($type, ['power_on', 'power_on_ok', 'power_off'], true)) { // Ein-/Ausschalt-Protokoll
            power_log_add($cid, (int)$device['id'], $type, $ev, $ts);
            if ($type === 'power_on_ok' && setting('push_power_all', '1') === '1') {
                push_parents('🔌 ' . $cname . ': ' . $title, strip_tags((string)($ev['detail'] ?? '')), 'power-' . $device['id']);
            }
            if ($type !== 'power_on') {
                continue; // nur der Alarm zur gesperrten Zeit kommt zusätzlich in die Ereignisse
            }
        }
        add_event($cid, (int)$device['id'], $type, $allowed[$type], $title, strip_tags((string)($ev['detail'] ?? '')), $ts);
        if (in_array($type, ['tamper', 'master_fail', 'uninstall', 'ext_missing', 'time_jump', 'browser_closed'], true) && setting('push_tamper', '1') === '1') {
            push_parents('⚠ ' . $cname, $title, 'tamper-' . $device['id']);
        }
        if ($type === 'power_on' && setting('push_power_on', '1') === '1') { // Gerät zur gesperrten Zeit eingeschaltet
            push_parents('🚨 ' . $cname . ': ' . $title, strip_tags((string)($ev['detail'] ?? '')), 'power-' . $device['id']);
        }
        if ($type === 'message_read' && !empty($ev['ref'])) {
            q('UPDATE messages SET read_at=? WHERE id=? AND device_id=? AND read_at IS NULL', [$ts, (int)$ev['ref'], $device['id']]);
        }
        if ($type === 'message_read' && setting('push_requests', '1') === '1') {
            push_parents('✓ ' . $cname . ' hat gelesen', $title, 'read-' . $device['id']);
        }
        if ($type === 'lock' && setting('push_limit', '1') === '1') {
            push_parents('⏰ ' . $cname, $title, 'lock-' . $cid);
        }
    }
    // Neue Programme erkennen (Nutzung, aktives Fenster, im strengen Modus blockierte Starts)
    try {
        if ($attempts) {
            apps_discover($cid, (int)$device['id'], $attempts, true);
        }
        $fg = strtolower((string)(($b['status'] ?? [])['app'] ?? ''));
        if (!empty(($b['status'] ?? [])['app_sys'])) {
            $fg = '';
        }
        if ($fg !== '' && !isset($newApps[$fg])) {
            $newApps[$fg] = (string)(($b['status'] ?? [])['app_label'] ?? '');
        }
        if ($newApps) {
            apps_discover($cid, (int)$device['id'], $newApps);
        }
    } catch (Throwable $e) {
    }
    // Status
    $st = (array)($b['status'] ?? []);
    $status = [
        'user' => mb_substr((string)($st['user'] ?? ''), 0, 60),
        'app' => mb_substr((string)($st['app'] ?? ''), 0, 120),
        'app_label' => mb_substr((string)($st['app_label'] ?? ''), 0, 160),
        'domain' => clean_domain((string)($st['domain'] ?? '')),
        'locked' => !empty($st['locked']),
        'reason' => mb_substr((string)($st['reason'] ?? ''), 0, 20),
        'idle' => (int)($st['idle'] ?? 0),
        'active_since' => mb_substr((string)($st['active_since'] ?? ''), 0, 30),
        'ext' => array_map('intval', array_slice((array)($st['ext'] ?? []), 0, 5, true)),
        'shutdown' => !empty($st['shutdown']),
        'offline_sec' => (int)($st['offline_sec'] ?? 0),
        'platform' => preg_replace('/[^a-z]/', '', (string)($st['platform'] ?? '')),
        'prot' => array_map(fn($v) => (bool)$v, array_slice((array)($st['prot'] ?? []), 0, 10, true)),
        'battery' => isset($st['battery']) ? (int)$st['battery'] : null,
    ];
    $wasOffline = (int)$device['offline_alerted'] === 1;
    q('UPDATE devices SET last_seen=?, last_ip=?, status=?, version=?, offline_alerted=0 WHERE id=?', [
        now(), client_ip(), json_encode($status, JSON_UNESCAPED_UNICODE), mb_substr((string)($b['version'] ?? ''), 0, 20), $device['id'],
    ]);
    if ($wasOffline && !$status['shutdown']) {
        add_event($cid, (int)$device['id'], 'online', 'info', 'Gerät wieder online: ' . $device['name']);
    }
    $device = q1('SELECT * FROM devices WHERE id=?', [$device['id']]);
    if ((int)($b['master_rev'] ?? 0) >= (int)$device['master_rev']) {
        unset($device['master_hash']); // Gerät ist aktuell
    }
    $pol = device_policy($device);
    $upd = update_for_device($device, (string)($b['version'] ?? $device['version'] ?? ''));
    if ($upd) {
        $pol['update'] = $upd;
    }
    if (!isset($device['master_hash'])) {
        unset($pol['master']);
    }
    json_out($pol);
}

if ($a === 'update') {
    // Neue Windows-App für das automatische Update (nur für gekoppelte Geräte)
    $f = ROOT . '/downloads/' . WIN_APP_FILE;
    if (!is_file($f) || setting('auto_update', '1') !== '1') {
        fail('Kein Update verfügbar', 404);
    }
    header('Content-Type: application/octet-stream');
    header('Content-Length: ' . filesize($f));
    readfile($f);
    exit;
}

if ($a === 'filter') {
    // Filterliste (gzip, eine Domain pro Zeile) für die Windows-App
    $k = preg_replace('/[^a-z]/', '', (string)($_GET['key'] ?? ''));
    $f = filter_dir() . "/$k.txt.gz";
    $row = q1('SELECT hash FROM filter_lists WHERE k=? AND enabled=1', [$k]);
    if (!$row || !is_file($f)) {
        fail('Liste nicht verfügbar', 404);
    }
    header('Content-Type: application/gzip');
    header('Content-Length: ' . filesize($f));
    header('X-List-Hash: ' . $row['hash']);
    readfile($f);
    exit;
}

if ($a === 'request') {
    if (!throttle_hit('req:' . $device['id'], 12, 3600)) {
        fail('Zu viele Anfragen in kurzer Zeit. Bitte etwas warten.', 429);
    }
    $kind = in_array($b['kind'] ?? '', ['site', 'app'], true) ? $b['kind'] : 'time';
    $local = substr(preg_replace('/[^A-Za-z0-9-]/', '', (string)($b['local_id'] ?? '')), 0, 40);
    if ($local !== '' && ($ex = q1('SELECT id, status FROM requests WHERE device_id=? AND local_id=?', [$device['id'], $local]))) {
        json_out(['ok' => true, 'id' => (int)$ex['id'], 'status' => $ex['status']]);
    }
    $open = (int)qv("SELECT COUNT(*) FROM requests WHERE device_id=? AND status='pending'", [$device['id']]);
    if ($open >= 3) {
        fail('Es sind schon Anfragen offen. Bitte auf die Antwort warten.', 429);
    }
    $minutes = in_array((int)($b['minutes'] ?? 30), [15, 30, 45, 60, 90, 120], true) ? (int)$b['minutes'] : 30;
    $reasons = ['school' => 'Schule / Hausaufgaben', 'learn' => 'Lernen', 'play' => 'Spielen', 'friends' => 'Mit Freunden', 'other' => 'Sonstiges'];
    $reason = isset($reasons[$b['reason'] ?? '']) ? $b['reason'] : 'other';
    $domain = $kind === 'site' ? clean_domain((string)($b['domain'] ?? '')) : '';
    if ($kind === 'site' && $domain === '') {
        fail('Ungültige Webseite');
    }
    $label = '';
    if ($kind === 'app') {
        $domain = substr(strtolower(preg_replace('/[^A-Za-z0-9._ -]/', '', (string)($b['domain'] ?? ''))), 0, 120);
        if ($domain === '') {
            fail('Programm fehlt');
        }
        $label = mb_substr(trim(strip_tags((string)($b['label'] ?? ''))), 0, 160) ?: $domain;
    }
    $msg = mb_substr(trim(strip_tags((string)($b['message'] ?? ''))), 0, 300);
    $id = db_insert('requests', ['child_id' => $cid, 'device_id' => $device['id'], 'local_id' => $local, 'kind' => $kind, 'minutes' => $minutes, 'reason' => $reason,
        'message' => $msg, 'domain' => $domain, 'label' => $label, 'status' => 'pending', 'created_at' => now()]);
    $child = q1('SELECT name FROM children WHERE id=?', [$cid]);
    $cname = $child['name'] ?? 'Kind';
    $title = $kind === 'site' ? "$cname möchte $domain öffnen" : ($kind === 'app' ? "$cname möchte „{$label}“ starten" : "$cname bittet um $minutes Min. mehr Zeit");
    add_event($cid, (int)$device['id'], 'request', 'info', $title, $reasons[$reason] . ($msg ? ' – „' . $msg . '“' : ''));
    if (setting('push_requests', '1') === '1') {
        push_parents('🙋 ' . $title, $reasons[$reason] . ($msg ? ': ' . $msg : '') . ' – Tippen zum Entscheiden', 'req-' . $id, './#requests');
    }
    json_out(['ok' => true, 'id' => $id, 'status' => 'pending']);
}

fail('Unbekannte Aktion', 404);
