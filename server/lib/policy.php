<?php
declare(strict_types=1);

const DAY_TYPES = ['school' => 'Schultag', 'weekend' => 'Wochenende', 'holiday' => 'Ferien / Feiertag'];
const UNLIMITED = 1440;
const GAME_ICONS = ['fortnite', 'minecraft', 'steam', 'roblox', 'epic', 'game'];

function default_rules(): array
{
    return [
        'school' => ['limit_min' => 120, 'start_time' => '07:00', 'end_time' => '20:00'],
        'weekend' => ['limit_min' => 180, 'start_time' => '08:00', 'end_time' => '21:00'],
        'holiday' => ['limit_min' => 180, 'start_time' => '08:00', 'end_time' => '21:00'],
    ];
}

function child_rules(int $childId): array
{
    $r = default_rules();
    foreach (qall('SELECT * FROM rules WHERE child_id=?', [$childId]) as $row) {
        $r[$row['day_type']] = ['limit_min' => (int)$row['limit_min'], 'start_time' => $row['start_time'], 'end_time' => $row['end_time']];
    }
    return $r;
}

function holiday_ranges(?string $from = null, ?string $to = null): array
{
    $from = $from ?: date('Y-m-d', strtotime('-1 day'));
    $to = $to ?: date('Y-m-d', strtotime('+400 days'));
    $out = [];
    foreach (qall('SELECT name, start_date, end_date FROM holidays WHERE end_date >= ? AND start_date <= ? ORDER BY start_date', [$from, $to]) as $h) {
        $out[] = [$h['start_date'], $h['end_date'], $h['name']];
    }
    return $out;
}

function day_type(string $date): string
{
    $mode = setting('mode', 'auto');
    if ($mode === 'holiday') {
        return 'holiday';
    }
    $dow = (int)date('N', strtotime($date));
    if ($mode === 'auto') {
        if (qv('SELECT 1 FROM holidays WHERE start_date <= ? AND end_date >= ?', [$date, $date])) {
            return 'holiday';
        }
    }
    return $dow >= 6 ? 'weekend' : 'school';
}

function used_sec(int $childId, string $day): int
{
    return (int)qv('SELECT COALESCE(SUM(sec),0) FROM usage_hourly WHERE child_id=? AND day=?', [$childId, $day]);
}

function bonus_min(int $childId, string $day): int
{
    return (int)qv('SELECT minutes FROM bonus WHERE child_id=? AND day=?', [$childId, $day]);
}

/** Aktueller Zustand eines Kindes (gleiche Logik wie im Windows-Agent) */
function child_state(array $child): array
{
    $day = today();
    $type = day_type($day);
    $rules = child_rules((int)$child['id']);
    $rule = $rules[$type];
    // Abend/Bettzeit richtet sich nach dem nächsten Tag: Fr-Abend = Wochenende, So-Abend = Schultag (in den Ferien: Ferien)
    $night = setting('evening_next_day', '1') === '1' ? day_type(date('Y-m-d', strtotime($day . ' +1 day'))) : $type;
    $end = $rules[$night]['end_time'];
    $used = used_sec((int)$child['id'], $day);
    $bonus = bonus_min((int)$child['id'], $day);
    $limit = (int)$rule['limit_min'];
    $unlimited = $limit >= UNLIMITED;
    $remaining = $unlimited ? null : max(0, ($limit + $bonus) * 60 - $used);
    $now = now();
    $hm = date('H:i');
    $reason = '';
    if (!empty($child['free_until']) && $child['free_until'] > $now) {
        $reason = '';
    } elseif (!empty($child['lock_until']) && $child['lock_until'] > $now) {
        $reason = 'manual';
    } elseif ($hm < $rule['start_time']) {
        $reason = 'early';
    } elseif ($end !== '24:00' && $hm >= $end) {
        $reason = 'bedtime';
    } elseif ($type === 'school' && setting('school_lock', '0') === '1' && $hm >= setting('school_from', '07:00') && $hm < setting('school_to', '15:00')) {
        $reason = 'school';
    } elseif (!$unlimited && $remaining <= 0) {
        $reason = 'time';
    }
    return [
        'day' => $day, 'day_type' => $type, 'day_label' => DAY_TYPES[$type], 'limit_min' => $limit, 'unlimited' => $unlimited,
        'bonus_min' => $bonus, 'used_sec' => $used, 'remaining_sec' => $remaining, 'start' => $rule['start_time'], 'end' => $end,
        'night_type' => $night, 'night_label' => DAY_TYPES[$night],
        'lock_reason' => $reason, 'lock_until' => $child['lock_until'] ?? null, 'free_until' => $child['free_until'] ?? null,
    ];
}

function blocked_sites_for(int $childId): array
{
    return array_values(array_unique(array_column(qall('SELECT domain FROM blocked_sites WHERE child_id=0 OR child_id=? ORDER BY domain', [$childId]), 'domain')));
}

function blocked_apps_for(int $childId): array
{
    $rows = qall('SELECT DISTINCT a.name, a.exes, a.icon FROM apps a JOIN app_blocks b ON b.app_id=a.id WHERE b.child_id=0 OR b.child_id=?', [$childId]);
    $granted = [];
    foreach (qall("SELECT domain FROM site_grants WHERE child_id=? AND until_at > ? AND domain LIKE 'app:%'", [$childId, now()]) as $g) {
        $granted[substr($g['domain'], 4)] = true;
    }
    $out = [];
    foreach ($rows as $r) {
        $kind = in_array($r['icon'], GAME_ICONS, true) ? 'game' : 'app';
        $exes = array_filter(array_map(fn($x) => strtolower(trim($x)), explode(',', $r['exes'])));
        if (array_intersect($exes, array_keys($granted))) {
            continue; // Freigabe gilt für das ganze Programm (z. B. Launcher + Spiel)
        }
        foreach ($exes as $e) {
            if ($e !== '') {
                $out[] = ['exe' => $e, 'name' => $r['name'], 'kind' => $kind];
            }
        }
    }
    return $out;
}

/** Vollständige Richtlinie für ein Gerät (wird lokal zwischengespeichert → funktioniert offline weiter) */
function device_policy(array $device): array
{
    $child = q1('SELECT * FROM children WHERE id=?', [$device['child_id']]);
    if (!$child) {
        return ['ok' => true, 'unassigned' => true];
    }
    $cid = (int)$child['id'];
    $state = child_state($child);
    $grants = qall("SELECT domain, until_at FROM site_grants WHERE child_id=? AND until_at > ? AND domain NOT LIKE 'app:%'", [$cid, now()]);
    $reqs = qall('SELECT id, local_id, kind, status, granted, domain, label, minutes FROM requests WHERE device_id=? AND created_at > ? ORDER BY id DESC LIMIT 20', [$device['id'], date('Y-m-d H:i:s', strtotime('-1 day'))]);
    $cmds = qall('SELECT id, type, payload FROM commands WHERE device_id=? AND delivered_at IS NULL ORDER BY id', [$device['id']]);
    if ($cmds) {
        q('UPDATE commands SET delivered_at=? WHERE device_id=? AND delivered_at IS NULL', [now(), $device['id']]);
        foreach ($cmds as $c) {
            $p = json_decode((string)$c['payload'], true) ?: [];
            if ($c['type'] === 'message' && !empty($p['message_id'])) {
                q('UPDATE messages SET delivered_at=? WHERE id=? AND delivered_at IS NULL', [now(), (int)$p['message_id']]);
            }
        }
    }
    $policy = [
        'ok' => true,
        'server_time' => date('c'),
        'child' => ['id' => $cid, 'name' => $child['name'], 'color' => $child['color']],
        'today' => $state,
        'rules' => child_rules($cid),
        'mode' => setting('mode', 'auto'),
        'evening_next_day' => setting('evening_next_day', '1') === '1',
        'school' => ['lock' => setting('school_lock', '0') === '1', 'from' => setting('school_from', '07:00'), 'to' => setting('school_to', '15:00')],
        'holidays' => array_map(fn($h) => [$h[0], $h[1]], holiday_ranges()),
        'blocked_sites' => blocked_sites_for($cid),
        'allow_sites' => array_column(qall('SELECT domain FROM allow_sites ORDER BY domain'), 'domain'),
        'filters' => filters_for_device(),
        'safesearch' => setting('safesearch', '1') === '1',
        'youtube_restrict' => (int)setting('youtube_restrict', '0'),
        'site_grants' => array_map(fn($g) => ['domain' => $g['domain'], 'until' => date('c', strtotime($g['until_at']))], $grants),
        'blocked_apps' => blocked_apps_for($cid),
        'strict_apps' => (int)($child['strict_apps'] ?? 0) === 1,
        'known_apps' => (int)($child['strict_apps'] ?? 0) === 1 ? known_exes() : [],
        'app_grants' => app_grants_for($cid),
        'system_apps' => SYSTEM_EXES,
        'warn_minutes' => array_values(array_filter(array_map('intval', explode(',', (string)setting('warn_minutes', '15,5,1'))))),
        'enforce_extension' => setting('enforce_extension', '1') === '1',
        'requests' => array_map(fn($r) => ['id' => (int)$r['id'], 'local_id' => $r['local_id'], 'kind' => $r['kind'], 'status' => $r['status'], 'granted' => (int)$r['granted'], 'domain' => $r['domain'], 'label' => $r['label'] ?? ''], $reqs),
        'commands' => array_map(fn($c) => ['id' => (int)$c['id'], 'type' => $c['type'], 'payload' => json_decode((string)$c['payload'], true)], $cmds),
        'sync_interval' => 15,
        'test_mode' => (int)($device['test_mode'] ?? 0) === 1,
        'theme' => theme_for_device($cid),
    ];
    if ((int)$device['master_rev'] > 0 && !empty($device['master_hash'])) {
        $policy['master'] = ['rev' => (int)$device['master_rev'], 'salt' => $device['master_salt'], 'hash' => $device['master_hash'], 'iter' => (int)$device['master_iter']];
    }
    return $policy;
}

/** PBKDF2-Hash im gleichen Format wie der Windows-Agent (SHA-256, Base64) */
function master_hash(string $password, ?string $saltB64 = null, int $iter = 120000): array
{
    $salt = $saltB64 ? base64_decode($saltB64) : random_bytes(16);
    return ['salt' => base64_encode($salt), 'hash' => base64_encode(hash_pbkdf2('sha256', $password, $salt, $iter, 32, true)), 'iter' => $iter];
}
