<?php
declare(strict_types=1);
/*
 * Eltern-API (Session + CSRF). Wird vom Control Center / der iPhone-Webapp genutzt.
 */
require __DIR__ . '/lib/bootstrap.php';

header('X-Content-Type-Options: nosniff');
header('Referrer-Policy: same-origin');
if (!installed()) {
    fail('Nicht installiert', 503);
}
session_boot();
$a = $_GET['a'] ?? '';
$isPost = $_SERVER['REQUEST_METHOD'] === 'POST';
$b = $isPost ? body() : [];

// ---------- öffentlich ----------
if ($a === 'me') {
    $u = current_user();
    json_out(['ok' => true, 'user' => $u, 'csrf' => $_SESSION['csrf'], 'family' => setting('family_name', 'Familie'), 'version' => APP_VERSION, 'copyright' => COPYRIGHT, 'https' => is_https()]);
}
if ($a === 'login' && $isPost) {
    check_csrf();
    $email = mb_strtolower(trim((string)($b['email'] ?? '')));
    if (!throttle_hit('login:' . client_ip(), 8, 900)) {
        fail('Zu viele Fehlversuche. Bitte 15 Minuten warten.', 429);
    }
    $u = q1('SELECT * FROM users WHERE email=?', [$email]);
    if (!$u || !password_verify((string)($b['password'] ?? ''), $u['pass_hash'])) {
        usleep(400000);
        fail('E-Mail oder Passwort falsch', 403);
    }
    session_regenerate_id(true);
    $_SESSION['uid'] = (int)$u['id'];
    q('DELETE FROM throttle WHERE k=?', ['login:' . client_ip()]);
    audit('login', $email);
    json_out(['ok' => true]);
}

// ---------- ab hier angemeldet ----------
$user = require_user();
if ($isPost) {
    check_csrf();
}
maintenance();

function child_or_fail(int $id): array
{
    $c = q1('SELECT * FROM children WHERE id=?', [$id]);
    if (!$c) {
        fail('Kind nicht gefunden', 404);
    }
    return $c;
}

function device_online(?string $lastSeen): bool
{
    return $lastSeen !== null && strtotime($lastSeen) > time() - 75;
}

function children_overview(): array
{
    $out = [];
    foreach (qall('SELECT * FROM children ORDER BY sort, id') as $c) {
        $st = child_state($c);
        $devs = qall('SELECT id, name, last_seen, status, version FROM devices WHERE child_id=? AND revoked=0 ORDER BY last_seen DESC', [$c['id']]);
        $online = false;
        $live = null;
        $lastSeen = null;
        foreach ($devs as $d) {
            $on = device_online($d['last_seen']);
            $s = json_decode((string)$d['status'], true) ?: [];
            if ($on && !$online) {
                $online = true;
                $live = $s + ['device' => $d['name']];
            }
            if (!$live && $d['last_seen']) {
                $live = $s + ['device' => $d['name']];
            }
            if ($d['last_seen'] && (!$lastSeen || $d['last_seen'] > $lastSeen)) {
                $lastSeen = $d['last_seen'];
            }
        }
        $out[] = [
            'id' => (int)$c['id'], 'name' => $c['name'], 'color' => $c['color'], 'avatar' => $c['avatar'],
            'state' => $st, 'online' => $online, 'live' => $live, 'last_seen' => $lastSeen, 'devices' => count($devs),
            'pending' => (int)qv("SELECT COUNT(*) FROM requests WHERE child_id=? AND status='pending'", [$c['id']]),
            'rules' => child_rules((int)$c['id']),
        ];
    }
    return $out;
}

function series(string $range): array
{
    $kids = qall('SELECT id, name, color FROM children ORDER BY sort, id');
    $res = ['labels' => [], 'series' => []];
    if ($range === 'today') {
        $res['labels'] = array_map(fn($h) => sprintf('%02d:00', $h), range(0, 23));
        foreach ($kids as $k) {
            $vals = array_fill(0, 24, 0);
            foreach (qall('SELECT hour, sec FROM usage_hourly WHERE child_id=? AND day=?', [$k['id'], today()]) as $r) {
                $vals[(int)$r['hour']] = (int)$r['sec'];
            }
            $res['series'][] = ['id' => (int)$k['id'], 'name' => $k['name'], 'color' => $k['color'], 'values' => $vals, 'total' => array_sum($vals)];
        }
        return $res;
    }
    $days = $range === '30' ? 30 : 7;
    $from = date('Y-m-d', strtotime('-' . ($days - 1) . ' days'));
    $dates = [];
    for ($i = $days - 1; $i >= 0; $i--) {
        $dates[] = date('Y-m-d', strtotime("-$i days"));
    }
    $res['labels'] = array_map(fn($d) => date('d.m.', strtotime($d)), $dates);
    foreach ($kids as $k) {
        $map = [];
        foreach (qall('SELECT day, SUM(sec) s FROM usage_hourly WHERE child_id=? AND day>=? GROUP BY day', [$k['id'], $from]) as $r) {
            $map[$r['day']] = (int)$r['s'];
        }
        $vals = array_map(fn($d) => $map[$d] ?? 0, $dates);
        $res['series'][] = ['id' => (int)$k['id'], 'name' => $k['name'], 'color' => $k['color'], 'values' => $vals, 'total' => array_sum($vals)];
    }
    return $res;
}

switch ($a) {
    case 'logout':
        audit('logout');
        $_SESSION = [];
        session_destroy();
        json_out(['ok' => true]);

    case 'dashboard':
        $kids = children_overview();
        $todaySec = (int)qv('SELECT COALESCE(SUM(sec),0) FROM usage_hourly WHERE day=?', [today()]);
        $yday = date('Y-m-d', strtotime('-1 day'));
        $ydaySameTime = (int)qv('SELECT COALESCE(SUM(sec),0) FROM usage_hourly WHERE day=? AND hour<=?', [$yday, (int)date('G')]);
        $devTotal = (int)qv('SELECT COUNT(*) FROM devices WHERE revoked=0');
        $devOnline = (int)qv('SELECT COUNT(*) FROM devices WHERE revoked=0 AND last_seen > ?', [date('Y-m-d H:i:s', time() - 75)]);
        json_out([
            'ok' => true,
            'children' => $kids,
            'stats' => [
                'today_sec' => $todaySec,
                'trend' => $ydaySameTime > 0 ? (int)round(($todaySec - $ydaySameTime) / $ydaySameTime * 100) : null,
                'devices_online' => $devOnline, 'devices_total' => $devTotal,
                'sites' => (int)qv('SELECT COUNT(*) FROM blocked_sites'),
                'sites_today' => (int)qv('SELECT COUNT(*) FROM blocked_sites WHERE created_at >= ?', [today() . ' 00:00:00']),
                'warnings' => (int)qv("SELECT COUNT(*) FROM events WHERE level='warn' AND created_at > ?", [date('Y-m-d H:i:s', time() - 86400)]),
                'pending' => (int)qv("SELECT COUNT(*) FROM requests WHERE status='pending'"),
            ],
            'chart' => series((string)($_GET['range'] ?? 'today')),
            'mode' => setting('mode', 'auto'), 'today_type' => DAY_TYPES[day_type(today())], 'today_key' => day_type(today()), 'school_lock' => setting('school_lock', '0') === '1',
            'school_from' => setting('school_from', '07:00'), 'school_to' => setting('school_to', '15:00'),
            'holiday_now' => q1('SELECT name, end_date FROM holidays WHERE start_date<=? AND end_date>=?', [today(), today()]),
            'unseen' => (int)qv('SELECT COUNT(*) FROM events WHERE seen=0'),
            'new_apps' => new_apps_list(),
        ]);

    case 'chart':
        json_out(['ok' => true, 'chart' => series((string)($_GET['range'] ?? 'today'))]);

    case 'children':
        $kids = children_overview();
        json_out(['ok' => true, 'children' => $kids, 'avatars' => avatar_presets()]);

    case 'child_save':
        $name = trim(strip_tags((string)($b['name'] ?? '')));
        if ($name === '' || mb_strlen($name) > 60) {
            fail('Bitte einen Namen (max. 60 Zeichen) angeben');
        }
        $color = in_array($b['color'] ?? '', ['cyan', 'pink', 'violet', 'gold', 'green', 'orange'], true) ? $b['color'] : 'cyan';
        $avatar = (string)($b['avatar'] ?? '');
        if ($avatar !== '' && !preg_match('#^(assets/img/avatars/[a-z0-9_-]+\.(png|svg)|uploads/avatars/[a-f0-9]{24}\.png)$#', $avatar)) {
            $avatar = '';
        }
        $id = (int)($b['id'] ?? 0);
        if ($id) {
            child_or_fail($id);
            q('UPDATE children SET name=?, color=?, avatar=? WHERE id=?', [$name, $color, $avatar, $id]);
            audit('child_update', "#$id $name");
        } else {
            $id = db_insert('children', ['name' => $name, 'color' => $color, 'avatar' => $avatar, 'sort' => (int)qv('SELECT COALESCE(MAX(sort),0)+1 FROM children'), 'created_at' => now()]);
            foreach (default_rules() as $t => $r) {
                db_put('rules', ['child_id' => $id, 'day_type' => $t], $r);
            }
            audit('child_create', "#$id $name");
        }
        json_out(['ok' => true, 'id' => $id]);

    case 'child_delete':
        $c = child_or_fail((int)($b['id'] ?? 0));
        foreach (['rules', 'usage_hourly', 'usage_apps', 'usage_web', 'bonus', 'requests', 'site_grants', 'events', 'pair_codes'] as $t) {
            q("DELETE FROM $t WHERE child_id=?", [$c['id']]);
        }
        q('DELETE FROM blocked_sites WHERE child_id=?', [$c['id']]);
        q('DELETE FROM app_blocks WHERE child_id=?', [$c['id']]);
        q('UPDATE devices SET revoked=1 WHERE child_id=?', [$c['id']]);
        q('DELETE FROM children WHERE id=?', [$c['id']]);
        audit('child_delete', $c['name']);
        json_out(['ok' => true]);

    case 'avatar_upload':
        if (empty($_FILES['file']) || $_FILES['file']['error'] !== UPLOAD_ERR_OK || $_FILES['file']['size'] > 4 * 1024 * 1024) {
            fail('Bild fehlt oder ist größer als 4 MB');
        }
        if (!function_exists('imagecreatefromstring')) {
            fail('PHP-GD fehlt auf dem Server');
        }
        $info = @getimagesize($_FILES['file']['tmp_name']);
        if (!$info || !in_array($info[2], [IMAGETYPE_PNG, IMAGETYPE_JPEG, IMAGETYPE_WEBP, IMAGETYPE_GIF], true)) {
            fail('Nur PNG, JPG, WEBP oder GIF');
        }
        $src = @imagecreatefromstring((string)file_get_contents($_FILES['file']['tmp_name']));
        if (!$src) {
            fail('Bild konnte nicht gelesen werden');
        }
        $w = imagesx($src);
        $h = imagesy($src);
        $s = min($w, $h);
        $dst = imagecreatetruecolor(256, 256);
        imagealphablending($dst, false);
        imagesavealpha($dst, true);
        imagecopyresampled($dst, $src, 0, 0, (int)(($w - $s) / 2), (int)(($h - $s) / 2), 256, 256, $s, $s);
        $dir = ROOT . '/uploads/avatars';
        if (!is_dir($dir)) {
            mkdir($dir, 0755, true);
        }
        $name = bin2hex(random_bytes(12)) . '.png';
        imagepng($dst, "$dir/$name");
        json_out(['ok' => true, 'path' => "uploads/avatars/$name"]);

    case 'rules_save':
        $c = child_or_fail((int)($b['child_id'] ?? 0));
        foreach ((array)($b['rules'] ?? []) as $type => $r) {
            if (!isset(DAY_TYPES[$type])) {
                continue;
            }
            $limit = max(0, min(UNLIMITED, (int)($r['limit_min'] ?? 120)));
            $st = (string)($r['start_time'] ?? '07:00');
            $en = (string)($r['end_time'] ?? '20:00');
            if (!valid_time($st) || !valid_time($en) || $st >= $en) {
                fail('Ungültige Uhrzeit bei ' . DAY_TYPES[$type] . ' (Beginn muss vor Ende liegen)');
            }
            db_put('rules', ['child_id' => $c['id'], 'day_type' => $type], ['limit_min' => $limit, 'start_time' => $st, 'end_time' => $en]);
        }
        audit('rules_save', $c['name'] . ': ' . json_encode($b['rules'] ?? []));
        json_out(['ok' => true]);

    case 'limit_quick': // Schieberegler im Dashboard
        $c = child_or_fail((int)($b['child_id'] ?? 0));
        $type = isset(DAY_TYPES[$b['day_type'] ?? '']) ? $b['day_type'] : day_type(today());
        $limit = max(0, min(UNLIMITED, (int)($b['limit_min'] ?? 120)));
        $r = child_rules((int)$c['id'])[$type];
        db_put('rules', ['child_id' => $c['id'], 'day_type' => $type], ['limit_min' => $limit, 'start_time' => $r['start_time'], 'end_time' => $r['end_time']]);
        audit('limit_quick', $c['name'] . " $type $limit");
        json_out(['ok' => true, 'day_type' => $type]);

    case 'child_action':
        $c = child_or_fail((int)($b['child_id'] ?? 0));
        $act = (string)($b['action'] ?? '');
        $min = max(1, min(24 * 60, (int)($b['minutes'] ?? 30)));
        switch ($act) {
            case 'lock':
                $until = !empty($b['until_tomorrow']) ? date('Y-m-d 05:00:00', strtotime('+1 day')) : date('Y-m-d H:i:s', time() + $min * 60);
                q('UPDATE children SET lock_until=?, free_until=NULL WHERE id=?', [$until, $c['id']]);
                $msg = 'PC gesperrt bis ' . date('d.m. H:i', strtotime($until));
                break;
            case 'unlock':
                q('UPDATE children SET lock_until=NULL WHERE id=?', [$c['id']]);
                $msg = 'Sperre aufgehoben';
                break;
            case 'free':
                $until = date('Y-m-d H:i:s', time() + $min * 60);
                q('UPDATE children SET free_until=?, lock_until=NULL WHERE id=?', [$until, $c['id']]);
                $msg = 'Ohne Einschränkung bis ' . date('H:i', strtotime($until));
                break;
            case 'unfree':
                q('UPDATE children SET free_until=NULL WHERE id=?', [$c['id']]);
                $msg = 'Freigabe beendet';
                break;
            case 'bonus':
                $add = max(-600, min(600, (int)($b['minutes'] ?? 30)));
                db_add('bonus', ['child_id' => $c['id'], 'day' => today()], 'minutes', $add);
                $msg = ($add >= 0 ? '+' : '') . $add . ' Min. für heute';
                break;
            default:
                fail('Unbekannte Aktion');
        }
        audit('child_action', $c['name'] . ': ' . $msg);
        add_event((int)$c['id'], 0, 'parent', 'info', $msg, 'Durch ' . $user['name']);
        json_out(['ok' => true, 'message' => $msg]);

    case 'sites':
        json_out(['ok' => true, 'sites' => qall('SELECT s.*, c.name AS child FROM blocked_sites s LEFT JOIN children c ON c.id=s.child_id ORDER BY s.domain'),
            'top' => qall('SELECT domain, SUM(sec) sec FROM usage_web WHERE day>=? GROUP BY domain ORDER BY sec DESC LIMIT 20', [date('Y-m-d', strtotime('-7 days'))]),
            'grants' => qall("SELECT g.*, c.name AS child FROM site_grants g LEFT JOIN children c ON c.id=g.child_id WHERE until_at > ? ORDER BY until_at", [now()])]);

    case 'site_add':
        $d = clean_domain((string)($b['domain'] ?? ''));
        if ($d === '') {
            fail('Bitte eine gültige Domain eingeben, z. B. beispiel.com');
        }
        $cid = (int)($b['child_id'] ?? 0);
        if ($cid) {
            child_or_fail($cid);
        }
        if (!qv('SELECT 1 FROM blocked_sites WHERE domain=? AND child_id=?', [$d, $cid])) {
            db_insert('blocked_sites', ['child_id' => $cid, 'domain' => $d, 'created_at' => now()]);
        }
        audit('site_add', "$d ($cid)");
        json_out(['ok' => true]);

    case 'site_del':
        q('DELETE FROM blocked_sites WHERE id=?', [(int)($b['id'] ?? 0)]);
        audit('site_del', (string)($b['id'] ?? ''));
        json_out(['ok' => true]);

    case 'filters':
        filter_ensure();
        $rows = [];
        foreach (qall('SELECT * FROM filter_lists') as $r) {
            $c = filter_catalog()[$r['k']] ?? null;
            if ($c) {
                $rows[] = ['key' => $r['k'], 'name' => $c[0], 'desc' => $c[1], 'icon' => $c[3], 'enabled' => (bool)$r['enabled'], 'count' => (int)$r['cnt'], 'updated' => $r['updated_at'], 'error' => $r['error']];
            }
        }
        $order = array_keys(filter_catalog());
        usort($rows, fn($x, $y) => array_search($x['key'], $order) <=> array_search($y['key'], $order));
        json_out(['ok' => true, 'filters' => $rows, 'allow' => qall('SELECT * FROM allow_sites ORDER BY domain'),
            'safesearch' => setting('safesearch', '1') === '1', 'youtube_restrict' => (int)setting('youtube_restrict', '0'),
            'blocked_today' => (int)qv("SELECT COUNT(*) FROM events WHERE type='blocked_site' AND created_at >= ?", [today() . ' 00:00:00'])]);

    case 'filter_toggle':
        $k = (string)($b['key'] ?? '');
        if (!isset(filter_catalog()[$k])) {
            fail('Unbekannte Liste');
        }
        filter_ensure();
        q('UPDATE filter_lists SET enabled=? WHERE k=?', [!empty($b['enabled']) ? 1 : 0, $k]);
        audit('filter_toggle', "$k " . (!empty($b['enabled']) ? 'an' : 'aus'));
        $res = ['ok' => true];
        if (!empty($b['enabled']) && !is_file(filter_dir() . "/$k.txt.gz")) {
            $res = filter_download($k) + ['ok' => true];
        }
        json_out($res);

    case 'filter_refresh':
        $k = (string)($b['key'] ?? '');
        $r = filter_download($k);
        if (!$r['ok']) {
            fail($r['error']);
        }
        json_out($r);

    case 'allow_add':
        $d = clean_domain((string)($b['domain'] ?? ''));
        if ($d === '') {
            fail('Bitte eine gültige Domain eingeben');
        }
        if (!qv('SELECT 1 FROM allow_sites WHERE domain=?', [$d])) {
            db_insert('allow_sites', ['domain' => $d, 'created_at' => now()]);
        }
        audit('allow_add', $d);
        json_out(['ok' => true]);

    case 'allow_del':
        q('DELETE FROM allow_sites WHERE id=?', [(int)($b['id'] ?? 0)]);
        json_out(['ok' => true]);

    case 'safe_set':
        if (isset($b['safesearch'])) {
            set_setting('safesearch', $b['safesearch'] ? '1' : '0');
        }
        if (isset($b['youtube_restrict'])) {
            set_setting('youtube_restrict', (string)max(0, min(2, (int)$b['youtube_restrict'])));
        }
        audit('safe_set', json_encode($b));
        json_out(['ok' => true]);

    case 'grant_del':
        q('DELETE FROM site_grants WHERE id=?', [(int)($b['id'] ?? 0)]);
        json_out(['ok' => true]);

    case 'apps':
        $apps = qall('SELECT * FROM apps ORDER BY sort, name');
        $blocks = [];
        foreach (qall('SELECT * FROM app_blocks') as $r) {
            $blocks[$r['app_id']][] = (int)$r['child_id'];
        }
        foreach ($apps as &$ap) {
            $ap['blocked_for'] = $blocks[$ap['id']] ?? [];
        }
        $seen = qall('SELECT app, MAX(label) label, SUM(sec) sec FROM usage_apps WHERE day>=? GROUP BY app ORDER BY sec DESC LIMIT 25', [date('Y-m-d', strtotime('-14 days'))]);
        $kidsN = [];
        foreach (qall('SELECT id, name, strict_apps FROM children ORDER BY sort, id') as $k) {
            $kidsN[(int)$k['id']] = $k['name'];
        }
        foreach ($apps as &$ap) {
            $ap['first_child_name'] = $kidsN[(int)($ap['first_child'] ?? 0)] ?? '';
        }
        unset($ap);
        json_out(['ok' => true, 'apps' => $apps, 'seen' => $seen, 'system' => SYSTEM_EXES,
            'strict' => array_map(fn($k) => ['id' => (int)$k['id'], 'name' => $k['name'], 'strict' => (int)$k['strict_apps'] === 1], qall('SELECT id, name, strict_apps FROM children ORDER BY sort, id')),
            'auto_add' => setting('auto_add_apps', '1') === '1', 'push_new' => setting('push_new_app', '1') === '1', 'new_apps' => new_apps_list()]);

    case 'app_decide':
        $app = app_decide((int)($b['app_id'] ?? 0), (string)($b['decision'] ?? ''), (int)($b['child_id'] ?? 0));
        json_out(['ok' => true, 'name' => $app['name']]);

    case 'apps_seen_all':
        q('UPDATE apps SET is_new=0 WHERE is_new=1');
        json_out(['ok' => true]);

    case 'child_strict':
        $c = child_or_fail((int)($b['child_id'] ?? 0));
        $on = !empty($b['strict']);
        q('UPDATE children SET strict_apps=? WHERE id=?', [$on ? 1 : 0, $c['id']]);
        audit('child_strict', $c['name'] . ': ' . ($on ? 'an' : 'aus'));
        json_out(['ok' => true]);

    case 'evening_set':
        set_setting('evening_next_day', !empty($b['on']) ? '1' : '0');
        audit('evening_set', !empty($b['on']) ? 'an' : 'aus');
        json_out(['ok' => true]);

    case 'apps_options':
        foreach (['auto_add_apps' => 'auto_add', 'push_new_app' => 'push_new'] as $k => $f) {
            if (isset($b[$f])) {
                set_setting($k, $b[$f] ? '1' : '0');
            }
        }
        json_out(['ok' => true]);

    case 'app_toggle':
        $app = q1('SELECT * FROM apps WHERE id=?', [(int)($b['app_id'] ?? 0)]);
        if (!$app) {
            fail('App nicht gefunden', 404);
        }
        $cid = (int)($b['child_id'] ?? 0);
        if (!empty($b['blocked'])) {
            if (!qv('SELECT 1 FROM app_blocks WHERE app_id=? AND child_id=?', [$app['id'], $cid])) {
                db_insert('app_blocks', ['app_id' => $app['id'], 'child_id' => $cid]);
            }
        } else {
            q('DELETE FROM app_blocks WHERE app_id=? AND child_id=?', [$app['id'], $cid]);
            if ($cid === 0) { // „für alle erlauben“ entfernt auch Einzelsperren
                q('DELETE FROM app_blocks WHERE app_id=?', [$app['id']]);
            }
        }
        audit('app_toggle', $app['name'] . " child=$cid blocked=" . (int)!empty($b['blocked']));
        json_out(['ok' => true]);

    case 'app_add':
        $name = trim(strip_tags((string)($b['name'] ?? '')));
        $exes = implode(',', array_filter(array_map(fn($e) => preg_replace('/[^A-Za-z0-9._ -]/', '', trim($e)), explode(',', (string)($b['exes'] ?? '')))));
        if ($name === '' || $exes === '') {
            fail('Name und mindestens eine .exe angeben');
        }
        $exes = implode(',', array_map(fn($e) => str_ends_with(strtolower($e), '.exe') ? $e : $e . '.exe', explode(',', $exes)));
        $id = db_insert('apps', ['name' => mb_substr($name, 0, 80), 'exes' => mb_substr($exes, 0, 400), 'icon' => !empty($b['game']) ? 'game' : '', 'sort' => 200]);
        if (!empty($b['block'])) {
            db_insert('app_blocks', ['app_id' => $id, 'child_id' => (int)($b['child_id'] ?? 0)]);
        }
        audit('app_add', "$name ($exes)");
        json_out(['ok' => true, 'id' => $id]);

    case 'app_del':
        q('DELETE FROM app_blocks WHERE app_id=?', [(int)($b['id'] ?? 0)]);
        q('DELETE FROM apps WHERE id=?', [(int)($b['id'] ?? 0)]);
        json_out(['ok' => true]);

    case 'stats':
        $days = in_array((int)($_GET['days'] ?? 7), [1, 7, 30], true) ? (int)$_GET['days'] : 7;
        $from = date('Y-m-d', strtotime('-' . ($days - 1) . ' days'));
        $cid = (int)($_GET['child_id'] ?? 0);
        $w = $cid ? ' AND child_id=' . $cid : '';
        json_out(['ok' => true,
            'chart' => series($days === 1 ? 'today' : (string)$days),
            'apps' => qall("SELECT app, MAX(label) label, SUM(sec) sec FROM usage_apps WHERE day>=? $w GROUP BY app ORDER BY sec DESC LIMIT 15", [$from]),
            'web' => qall("SELECT domain, SUM(sec) sec FROM usage_web WHERE day>=? $w GROUP BY domain ORDER BY sec DESC LIMIT 15", [$from]),
            'total' => (int)qv("SELECT COALESCE(SUM(sec),0) FROM usage_hourly WHERE day>=? $w", [$from]),
            'days' => $days]);

    case 'mode':
        json_out(['ok' => true, 'mode' => setting('mode', 'auto'), 'state' => setting('state', 'NW'), 'today_type' => day_type(today()),
            'school_from' => setting('school_from', '07:00'), 'school_to' => setting('school_to', '15:00'), 'school_lock' => setting('school_lock', '0') === '1',
            'holidays' => qall('SELECT * FROM holidays WHERE end_date >= ? ORDER BY start_date', [date('Y-m-d', strtotime('-30 days'))]),
            'last_import' => setting('holiday_import')]);

    case 'mode_set':
        $m = in_array($b['mode'] ?? '', ['auto', 'school', 'holiday'], true) ? $b['mode'] : 'auto';
        set_setting('mode', $m);
        if (isset($b['school_lock'])) {
            set_setting('school_lock', $b['school_lock'] ? '1' : '0');
        }
        if (isset($b['school_from'], $b['school_to']) && valid_time((string)$b['school_from']) && valid_time((string)$b['school_to']) && $b['school_from'] >= $b['school_to']) {
            fail('Schulbeginn muss vor Schulende liegen');
        }
        foreach (['school_from', 'school_to'] as $k) {
            if (isset($b[$k]) && valid_time((string)$b[$k])) {
                set_setting($k, $b[$k]);
            }
        }
        if (isset($b['state']) && preg_match('/^[A-Z]{2}$/', (string)$b['state'])) {
            set_setting('state', $b['state']);
        }
        audit('mode_set', $m);
        json_out(['ok' => true, 'today_type' => day_type(today())]);

    case 'holiday_add':
        $s = (string)($b['start'] ?? '');
        $e = (string)($b['end'] ?? $s);
        if (!preg_match('/^\d{4}-\d{2}-\d{2}$/', $s) || !preg_match('/^\d{4}-\d{2}-\d{2}$/', $e) || $e < $s) {
            fail('Ungültiger Zeitraum');
        }
        db_insert('holidays', ['name' => mb_substr(trim(strip_tags((string)($b['name'] ?? 'Frei'))), 0, 120) ?: 'Frei', 'start_date' => $s, 'end_date' => $e, 'source' => 'manuell']);
        json_out(['ok' => true]);

    case 'holiday_del':
        q('DELETE FROM holidays WHERE id=?', [(int)($b['id'] ?? 0)]);
        json_out(['ok' => true]);

    case 'holiday_import':
        $state = preg_match('/^[A-Z]{2}$/', (string)($b['state'] ?? '')) ? $b['state'] : setting('state', 'NW');
        set_setting('state', $state);
        $from = date('Y-01-01');
        $to = date('Y-12-31', strtotime('+1 year'));
        $items = [];
        foreach (['SchoolHolidays', 'PublicHolidays'] as $ep) {
            $url = "https://openholidaysapi.org/$ep?countryIsoCode=DE&subdivisionCode=DE-$state&languageIsoCode=DE&validFrom=$from&validTo=$to";
            $ch = curl_init($url);
            curl_setopt_array($ch, [CURLOPT_RETURNTRANSFER => true, CURLOPT_TIMEOUT => 15, CURLOPT_HTTPHEADER => ['Accept: application/json']]);
            $res = curl_exec($ch);
            $code = (int)curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
            curl_close($ch);
            $data = json_decode((string)$res, true);
            if ($code !== 200 || !is_array($data)) {
                fail('Ferien konnten nicht geladen werden (openholidaysapi.org nicht erreichbar)');
            }
            foreach ($data as $h) {
                $name = $h['name'][0]['text'] ?? ($ep === 'PublicHolidays' ? 'Feiertag' : 'Ferien');
                $items[] = [$name, $h['startDate'], $h['endDate'], $ep === 'PublicHolidays' ? 'feiertag' : 'ferien'];
            }
        }
        db()->beginTransaction();
        q("DELETE FROM holidays WHERE source IN ('ferien','feiertag')");
        foreach ($items as $i) {
            db_insert('holidays', ['name' => mb_substr($i[0], 0, 120), 'start_date' => $i[1], 'end_date' => $i[2], 'source' => $i[3]]);
        }
        db()->commit();
        set_setting('holiday_import', now() . " · $state · openholidaysapi.org");
        audit('holiday_import', "$state " . count($items));
        json_out(['ok' => true, 'count' => count($items)]);

    case 'events':
        $lvl = ($_GET['level'] ?? '') === 'warn' ? " AND e.level='warn'" : '';
        $rows = qall("SELECT e.*, c.name AS child, d.name AS device FROM events e LEFT JOIN children c ON c.id=e.child_id LEFT JOIN devices d ON d.id=e.device_id WHERE 1=1 $lvl ORDER BY e.id DESC LIMIT 200");
        json_out(['ok' => true, 'events' => $rows]);

    case 'events_seen':
        q('UPDATE events SET seen=1 WHERE seen=0');
        json_out(['ok' => true]);

    case 'requests':
        json_out(['ok' => true, 'requests' => qall('SELECT r.*, c.name AS child, c.avatar, c.color, d.name AS device FROM requests r LEFT JOIN children c ON c.id=r.child_id LEFT JOIN devices d ON d.id=r.device_id ORDER BY (r.status=\'pending\') DESC, r.id DESC LIMIT 60')]);

    case 'request_decide':
        $r = q1('SELECT * FROM requests WHERE id=?', [(int)($b['id'] ?? 0)]);
        if (!$r) {
            fail('Anfrage nicht gefunden', 404);
        }
        if ($r['status'] !== 'pending') {
            fail('Diese Anfrage wurde bereits beantwortet');
        }
        $approve = !empty($b['approve']);
        $min = max(5, min(600, (int)($b['minutes'] ?? $r['minutes'])));
        if ($approve) {
            if ($r['kind'] === 'site') {
                db_insert('site_grants', ['child_id' => $r['child_id'], 'domain' => $r['domain'], 'until_at' => date('Y-m-d H:i:s', time() + $min * 60)]);
            } elseif ($r['kind'] === 'app') {
                db_insert('site_grants', ['child_id' => $r['child_id'], 'domain' => 'app:' . $r['domain'], 'until_at' => date('Y-m-d H:i:s', time() + $min * 60)]);
            } else {
                $child = q1('SELECT * FROM children WHERE id=?', [$r['child_id']]);
                $st = child_state($child);
                // Bei Bettzeit/Sperre gilt die Genehmigung als befristete Freigabe
                if (in_array($st['lock_reason'], ['bedtime', 'early', 'manual', 'school'], true)) {
                    q('UPDATE children SET free_until=?, lock_until=NULL WHERE id=?', [date('Y-m-d H:i:s', time() + $min * 60), $r['child_id']]);
                } else {
                    db_add('bonus', ['child_id' => $r['child_id'], 'day' => today()], 'minutes', $min);
                }
            }
        }
        q('UPDATE requests SET status=?, granted=?, decided_at=? WHERE id=?', [$approve ? 'approved' : 'denied', $approve ? $min : 0, now(), $r['id']]);
        audit('request_decide', "#{$r['id']} " . ($approve ? "ok $min" : 'nein'));
        json_out(['ok' => true]);

    case 'devices':
        $devs = qall('SELECT d.id, d.child_id, d.name, d.os, d.version, d.last_seen, d.last_ip, d.status, d.created_at, d.master_rev, d.test_mode, c.name AS child FROM devices d LEFT JOIN children c ON c.id=d.child_id WHERE d.revoked=0 ORDER BY d.id');
        foreach ($devs as &$d) {
            $d['status'] = json_decode((string)$d['status'], true);
            $d['online'] = device_online($d['last_seen']);
        }
        $dl = [];
        foreach (['MSFamilyControl-Setup.exe', 'MSFamilyControl.apk', 'browser-extension-chrome-edge.zip', 'browser-extension-firefox.zip'] as $f) {
            if (is_file(ROOT . '/downloads/' . $f)) {
                $dl[$f] = filesize(ROOT . '/downloads/' . $f);
            }
        }
        json_out(['ok' => true, 'devices' => $devs, 'downloads' => $dl, 'server_url' => base_url(), 'win_app' => win_app_info(), 'auto_update' => setting('auto_update', '1') === '1']);

    case 'power_log':
        // Ein-/Ausschalt-Protokoll mit Laufzeit (Einschalten → nächstes Ausschalten desselben Geräts)
        $days = max(1, min(400, (int)($_GET['days'] ?? 30)));
        $where = ['p.at >= ?'];
        $par = [date('Y-m-d H:i:s', strtotime("-$days days"))];
        if (!empty($_GET['child'])) { $where[] = 'p.child_id = ?'; $par[] = (int)$_GET['child']; }
        if (!empty($_GET['device'])) { $where[] = 'p.device_id = ?'; $par[] = (int)$_GET['device']; }
        $rows = qall('SELECT p.*, d.name AS device, d.os AS os, c.name AS child FROM power_log p LEFT JOIN devices d ON d.id=p.device_id LEFT JOIN children c ON c.id=p.child_id WHERE ' . implode(' AND ', $where) . ' ORDER BY p.at ASC, p.id ASC', $par);
        $openOn = [];
        foreach ($rows as $i => $r) {
            $rows[$i]['duration'] = null;
            $dev = (int)$r['device_id'];
            if ($r['power'] === 'on') {
                if (!isset($openOn[$dev]) || $r['how'] === 'eingeschaltet' || $r['how'] === 'aus dem Energiesparmodus geweckt' || $r['how'] === 'Handy eingeschaltet') {
                    $openOn[$dev] = $i;
                }
            } elseif (isset($openOn[$dev]) && in_array($r['how'], ['heruntergefahren', 'in den Energiesparmodus versetzt', 'Handy ausgeschaltet'], true)) {
                $start = strtotime((string)$rows[$openOn[$dev]]['at']);
                $rows[$i]['duration'] = max(0, strtotime((string)$r['at']) - $start);
                unset($openOn[$dev]);
            }
        }
        $rows = array_reverse($rows);
        $sum = ['on' => 0, 'locked' => 0];
        foreach ($rows as $r) {
            if ($r['power'] === 'on') { $sum['on']++; $sum['locked'] += (int)$r['locked']; }
        }
        json_out(['ok' => true, 'log' => array_slice($rows, 0, 1000), 'sum' => $sum, 'reasons' => ['bedtime' => 'Bettzeit', 'early' => 'zu früh', 'school' => 'Schulzeit', 'manual' => 'gesperrt', 'time' => 'Zeit aufgebraucht']]);

    case 'win_app_upload':
        if (empty($_FILES['file']) || $_FILES['file']['error'] !== UPLOAD_ERR_OK) {
            $err = (int)($_FILES['file']['error'] ?? 4);
            fail($err === UPLOAD_ERR_INI_SIZE || $err === UPLOAD_ERR_FORM_SIZE ? 'Datei zu groß für die Server-Einstellungen (upload_max_filesize)' : 'Keine Datei empfangen');
        }
        if ($_FILES['file']['size'] > 40 * 1024 * 1024) {
            fail('Datei größer als 40 MB');
        }
        $info = win_app_store_upload($_FILES['file']['tmp_name'], (string)$_FILES['file']['name']);
        audit('win_app_upload', 'Version ' . ($info['version'] ?? '?'));
        json_out(['ok' => true, 'win_app' => $info]);

    case 'pair_code':
        $c = child_or_fail((int)($b['child_id'] ?? 0));
        $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
        $code = '';
        for ($i = 0; $i < 8; $i++) {
            $code .= $alphabet[random_int(0, strlen($alphabet) - 1)];
        }
        db_insert('pair_codes', ['code' => $code, 'child_id' => $c['id'], 'expires_at' => date('Y-m-d H:i:s', time() + 3600)]);
        audit('pair_code', $c['name']);
        json_out(['ok' => true, 'code' => $code, 'expires' => date('H:i', time() + 3600), 'server_url' => base_url()]);

    case 'device_update':
        $d = q1('SELECT * FROM devices WHERE id=? AND revoked=0', [(int)($b['id'] ?? 0)]);
        if (!$d) {
            fail('Gerät nicht gefunden', 404);
        }
        $name = mb_substr(trim(strip_tags((string)($b['name'] ?? $d['name']))), 0, 80) ?: $d['name'];
        $cid = (int)($b['child_id'] ?? $d['child_id']);
        child_or_fail($cid);
        q('UPDATE devices SET name=?, child_id=? WHERE id=?', [$name, $cid, $d['id']]);
        if (isset($b['test_mode'])) {
            q('UPDATE devices SET test_mode=? WHERE id=?', [$b['test_mode'] ? 1 : 0, $d['id']]);
            audit('test_mode', $d['name'] . ' ' . ($b['test_mode'] ? 'an' : 'aus'));
        }
        json_out(['ok' => true]);

    case 'device_delete':
        q('UPDATE devices SET revoked=1 WHERE id=?', [(int)($b['id'] ?? 0)]);
        audit('device_delete', (string)($b['id'] ?? ''));
        json_out(['ok' => true]);

    case 'device_cmd':
        $d = q1('SELECT * FROM devices WHERE id=? AND revoked=0', [(int)($b['id'] ?? 0)]);
        if (!$d) {
            fail('Gerät nicht gefunden', 404);
        }
        $type = (string)($b['type'] ?? '');
        if (!in_array($type, ['message', 'shutdown', 'logoff'], true)) {
            fail('Unbekannter Befehl');
        }
        $payload = ['text' => mb_substr(trim(strip_tags((string)($b['text'] ?? ''))), 0, 300), 'from' => $user['name']];
        if ($type === 'message' && $payload['text'] === '') {
            fail('Bitte eine Nachricht eingeben');
        }
        if ($type === 'message') {
            $payload['priority'] = ($b['priority'] ?? '') === 'high' ? 'high' : 'normal';
            $payload['message_id'] = db_insert('messages', ['child_id' => $d['child_id'], 'device_id' => $d['id'], 'sender' => mb_substr($user['name'], 0, 80), 'text' => $payload['text'], 'priority' => $payload['priority'], 'created_at' => now()]);
        }
        db_insert('commands', ['device_id' => $d['id'], 'type' => $type, 'payload' => json_encode($payload, JSON_UNESCAPED_UNICODE), 'created_at' => now()]);
        audit('device_cmd', "{$d['name']} $type");
        json_out(['ok' => true]);

    case 'message_send':
        $text = mb_substr(trim(strip_tags((string)($b['text'] ?? ''))), 0, 600);
        if ($text === '') {
            fail('Bitte eine Nachricht eingeben');
        }
        $prio = ($b['priority'] ?? '') === 'high' ? 'high' : 'normal';
        $ids = !empty($b['all']) ? array_column(qall('SELECT id FROM children'), 'id') : array_map('intval', (array)($b['child_ids'] ?? []));
        if (!$ids) {
            fail('Bitte mindestens ein Kind auswählen');
        }
        $sent = 0;
        $none = [];
        foreach ($ids as $cid) {
            $c = q1('SELECT * FROM children WHERE id=?', [$cid]);
            if (!$c) {
                continue;
            }
            $devs = qall('SELECT id FROM devices WHERE child_id=? AND revoked=0', [$cid]);
            if (!$devs) {
                $none[] = $c['name'];
                continue;
            }
            foreach ($devs as $d) {
                $mid = db_insert('messages', ['child_id' => $cid, 'device_id' => $d['id'], 'sender' => mb_substr($user['name'], 0, 80), 'text' => $text, 'priority' => $prio, 'created_at' => now()]);
                db_insert('commands', ['device_id' => $d['id'], 'type' => 'message', 'payload' => json_encode(['text' => $text, 'from' => $user['name'], 'message_id' => $mid, 'priority' => $prio], JSON_UNESCAPED_UNICODE), 'created_at' => now()]);
                $sent++;
            }
        }
        audit('message_send', count($ids) . ' Kind(er), ' . $prio);
        json_out(['ok' => true, 'sent' => $sent, 'no_device' => $none]);

    case 'messages':
        json_out(['ok' => true, 'messages' => qall('SELECT m.*, c.name AS child, c.avatar, d.name AS device FROM messages m LEFT JOIN children c ON c.id=m.child_id LEFT JOIN devices d ON d.id=m.device_id ORDER BY m.id DESC LIMIT 100')]);

    case 'themes':
        $kids = [];
        foreach (qall('SELECT id, name, color, avatar FROM children ORDER BY sort, id') as $c) {
            $kids[] = $c + ['config' => child_theme((int)$c['id'])];
        }
        json_out(['ok' => true, 'catalog' => theme_catalog(), 'events' => THEME_EVENTS, 'defaults' => theme_default_texts(), 'children' => $kids]);

    case 'theme_save':
        $c = child_or_fail((int)($b['child_id'] ?? 0));
        $cat = theme_catalog();
        $theme = isset($cat[$b['theme'] ?? '']) ? $b['theme'] : 'anime';
        $accent = preg_match('/^#[0-9a-f]{6}$/i', (string)($b['accent'] ?? '')) ? strtolower($b['accent']) : '';
        $events = [];
        foreach ((array)($b['events'] ?? []) as $k => $v) {
            if (isset(THEME_EVENTS[$k]) && isset($cat[$v])) {
                $events[$k] = $v;
            }
        }
        $texts = [];
        foreach ((array)($b['texts'] ?? []) as $k => $v) {
            if (!isset(THEME_EVENTS[$k]) || !is_array($v)) {
                continue;
            }
            $tt = mb_substr(trim(strip_tags((string)($v['title'] ?? ''))), 0, 80);
            $tx = mb_substr(trim(strip_tags((string)($v['text'] ?? ''))), 0, 220);
            if ($tt !== '' || $tx !== '') {
                $texts[$k] = ['title' => $tt, 'text' => $tx];
            }
        }
        db_put('child_themes', ['child_id' => $c['id']], ['theme' => $theme, 'accent' => $accent, 'scale' => max(80, min(160, (int)($b['scale'] ?? 100))),
            'anim' => !empty($b['anim']) ? 1 : 0, 'events' => json_encode($events), 'texts' => json_encode($texts, JSON_UNESCAPED_UNICODE), 'updated_at' => now()]);
        audit('theme_save', $c['name'] . ": $theme " . json_encode($events));
        json_out(['ok' => true]);

    case 'theme_reset':
        $c = child_or_fail((int)($b['child_id'] ?? 0));
        q('DELETE FROM child_themes WHERE child_id=?', [$c['id']]);
        audit('theme_reset', $c['name']);
        json_out(['ok' => true]);

    case 'theme_copy':
        $src = child_or_fail((int)($b['child_id'] ?? 0));
        $row = q1('SELECT * FROM child_themes WHERE child_id=?', [$src['id']]);
        foreach (qall('SELECT id FROM children WHERE id<>?', [$src['id']]) as $o) {
            if ($row) {
                db_put('child_themes', ['child_id' => $o['id']], ['theme' => $row['theme'], 'accent' => $row['accent'], 'scale' => $row['scale'], 'anim' => $row['anim'], 'events' => $row['events'], 'texts' => $row['texts'], 'updated_at' => now()]);
            } else {
                q('DELETE FROM child_themes WHERE child_id=?', [$o['id']]);
            }
        }
        audit('theme_copy', $src['name']);
        json_out(['ok' => true]);

    case 'master_set':
        $pw = (string)($b['password'] ?? '');
        if (mb_strlen($pw) < 6) {
            fail('Das Masterpasswort muss mindestens 6 Zeichen haben');
        }
        $ids = !empty($b['id']) ? [(int)$b['id']] : array_column(qall('SELECT id FROM devices WHERE revoked=0'), 'id');
        foreach ($ids as $id) {
            $h = master_hash($pw);
            q('UPDATE devices SET master_rev=master_rev+1000, master_salt=?, master_hash=?, master_iter=? WHERE id=?', [$h['salt'], $h['hash'], $h['iter'], $id]);
        }
        audit('master_set', implode(',', $ids));
        json_out(['ok' => true, 'count' => count($ids)]);

    case 'settings':
        $keys = ['family_name', 'timezone', 'warn_minutes', 'enforce_extension', 'retention_days', 'push_requests', 'push_tamper', 'push_offline', 'push_limit', 'push_subject'];
        $s = [];
        foreach ($keys as $k) {
            $s[$k] = setting($k, '');
        }
        $s['push_new_app'] = setting('push_new_app', '1');
        $s['evening_next_day'] = setting('evening_next_day', '1');
        $s['push_power_on'] = setting('push_power_on', '1');
        $s['push_power_all'] = setting('push_power_all', '1');
        $s['auto_update'] = setting('auto_update', '1');
        json_out(['ok' => true, 'settings' => $s, 'users' => qall('SELECT id, name, email, created_at FROM users ORDER BY id'),
            'audit' => qall('SELECT a.*, u.name AS user FROM audit a LEFT JOIN users u ON u.id=a.user_id ORDER BY a.id DESC LIMIT 40'),
            'push_count' => (int)qv('SELECT COUNT(*) FROM push_subs'), 'version' => APP_VERSION, 'db' => db_driver(), 'php' => PHP_VERSION]);

    case 'settings_save':
        $s = (array)($b['settings'] ?? []);
        if (isset($s['family_name'])) {
            set_setting('family_name', mb_substr(trim(strip_tags((string)$s['family_name'])), 0, 60) ?: 'Familie');
        }
        if (isset($s['timezone']) && in_array($s['timezone'], timezone_identifiers_list(), true)) {
            set_setting('timezone', $s['timezone']);
        }
        if (isset($s['warn_minutes'])) {
            $w = array_slice(array_values(array_unique(array_filter(array_map('intval', explode(',', (string)$s['warn_minutes'])), fn($x) => $x > 0 && $x <= 120))), 0, 5);
            rsort($w);
            set_setting('warn_minutes', implode(',', $w));
        }
        if (isset($s['retention_days'])) {
            set_setting('retention_days', (string)max(7, min(400, (int)$s['retention_days'])));
        }
        foreach (['enforce_extension', 'push_requests', 'push_tamper', 'push_offline', 'push_limit', 'push_new_app', 'push_power_on', 'push_power_all', 'auto_update'] as $k) {
            if (isset($s[$k])) {
                set_setting($k, $s[$k] ? '1' : '0');
            }
        }
        if (isset($s['push_subject'])) {
            $ps = trim((string)$s['push_subject']);
            if ($ps === '' || filter_var(preg_replace('/^mailto:/', '', $ps), FILTER_VALIDATE_EMAIL)) {
                set_setting('push_subject', $ps === '' ? '' : (str_starts_with($ps, 'mailto:') ? $ps : "mailto:$ps"));
            }
        }
        audit('settings_save');
        json_out(['ok' => true]);

    case 'user_add':
        $email = mb_strtolower(trim((string)($b['email'] ?? '')));
        $name = trim(strip_tags((string)($b['name'] ?? '')));
        $pw = (string)($b['password'] ?? '');
        if (!filter_var($email, FILTER_VALIDATE_EMAIL) || $name === '' || mb_strlen($pw) < 8) {
            fail('Name, gültige E-Mail und Passwort (mind. 8 Zeichen) angeben');
        }
        if (qv('SELECT 1 FROM users WHERE email=?', [$email])) {
            fail('E-Mail ist bereits vergeben');
        }
        db_insert('users', ['name' => mb_substr($name, 0, 80), 'email' => $email, 'pass_hash' => password_hash($pw, PASSWORD_DEFAULT), 'created_at' => now()]);
        audit('user_add', $email);
        json_out(['ok' => true]);

    case 'user_del':
        $id = (int)($b['id'] ?? 0);
        if ($id === (int)$user['id']) {
            fail('Du kannst dich nicht selbst löschen');
        }
        q('DELETE FROM users WHERE id=?', [$id]);
        q('DELETE FROM push_subs WHERE user_id=?', [$id]);
        audit('user_del', (string)$id);
        json_out(['ok' => true]);

    case 'password':
        $u = q1('SELECT * FROM users WHERE id=?', [$user['id']]);
        if (!password_verify((string)($b['old'] ?? ''), $u['pass_hash'])) {
            fail('Aktuelles Passwort ist falsch', 403);
        }
        if (mb_strlen((string)($b['new'] ?? '')) < 8) {
            fail('Neues Passwort: mindestens 8 Zeichen');
        }
        q('UPDATE users SET pass_hash=? WHERE id=?', [password_hash((string)$b['new'], PASSWORD_DEFAULT), $user['id']]);
        audit('password_change');
        json_out(['ok' => true]);

    case 'dash_theme':
        $t = in_array($b['theme'] ?? '', ['anime', 'neon', 'family', 'army', 'manga', 'auto'], true) ? $b['theme'] : 'anime';
        q('UPDATE users SET dash_theme=? WHERE id=?', [$t, $user['id']]);
        json_out(['ok' => true]);

    case 'vapid':
        json_out(['ok' => true, 'key' => vapid_keys()[0]]);

    case 'push_sub':
        $sub = (array)($b['subscription'] ?? []);
        $ep = (string)($sub['endpoint'] ?? '');
        $p = (string)($sub['keys']['p256dh'] ?? '');
        $au = (string)($sub['keys']['auth'] ?? '');
        if (!preg_match('#^https://#', $ep) || $p === '' || $au === '') {
            fail('Ungültiges Push-Abo');
        }
        db_put('push_subs', ['endpoint_hash' => hash('sha256', $ep)], ['user_id' => $user['id'], 'endpoint' => $ep, 'p256dh' => $p, 'auth' => $au,
            'ua' => mb_substr((string)($_SERVER['HTTP_USER_AGENT'] ?? ''), 0, 200), 'created_at' => now()]);
        json_out(['ok' => true]);

    case 'push_unsub':
        q('DELETE FROM push_subs WHERE endpoint_hash=?', [hash('sha256', (string)($b['endpoint'] ?? ''))]);
        json_out(['ok' => true]);

    case 'push_test':
        $n = push_parents('✅ Push funktioniert', 'MS Family Control kann dir Benachrichtigungen senden.', 'test');
        json_out(['ok' => true, 'sent' => $n]);
}

function avatar_presets(): array
{
    $out = [];
    foreach (glob(ROOT . '/assets/img/avatars/*.{png,svg}', GLOB_BRACE) ?: [] as $f) {
        $out[] = 'assets/img/avatars/' . basename($f);
    }
    sort($out);
    return $out;
}

fail('Unbekannte Aktion', 404);
