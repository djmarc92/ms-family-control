<?php
declare(strict_types=1);

const APP_NAME = 'MS Family Control';
const APP_VERSION = '1.6.0';
const COPYRIGHT = 'Copyright © 2026 · Entworfen und entwickelt by Marc-André Straßen mit ❤️';

define('ROOT', dirname(__DIR__));
define('DATA', ROOT . '/data');

error_reporting(E_ALL);
ini_set('display_errors', '0');
date_default_timezone_set('Europe/Berlin');

require __DIR__ . '/db.php';
require __DIR__ . '/schema.php';
require __DIR__ . '/policy.php';
require __DIR__ . '/push.php';
require __DIR__ . '/auth.php';
require __DIR__ . '/filters.php';
require __DIR__ . '/themes.php';
require __DIR__ . '/apps.php';

function cfg(bool $reload = false): ?array
{
    static $c = false;
    if ($c === false || $reload) {
        $f = DATA . '/config.php';
        $c = is_file($f) ? require $f : null;
    }
    return is_array($c) ? $c : null;
}

function installed(): bool
{
    return cfg() !== null;
}

function now(): string
{
    return date('Y-m-d H:i:s');
}

function today(): string
{
    return date('Y-m-d');
}

function app_boot(): void
{
    if (!installed()) {
        return;
    }
    date_default_timezone_set('Europe/Berlin');
    $tz = setting('timezone', 'Europe/Berlin');
    if (in_array($tz, timezone_identifiers_list(), true)) {
        date_default_timezone_set($tz);
    }
    // Updates: Migrationen automatisch nachziehen
    if ((int)setting('schema_version', 0) < max(array_keys(migrations()))) {
        migrate(db());
        setting('', null, true);
    }
}

function json_out($data, int $code = 200): never
{
    http_response_code($code);
    header('Content-Type: application/json; charset=utf-8');
    header('Cache-Control: no-store');
    echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}

function fail(string $msg, int $code = 400): never
{
    json_out(['ok' => false, 'error' => $msg], $code);
}

function body(): array
{
    static $b = null;
    if ($b === null) {
        $raw = file_get_contents('php://input') ?: '';
        $b = json_decode($raw, true);
        if (!is_array($b)) {
            $b = $_POST ?: [];
        }
    }
    return $b;
}

function client_ip(): string
{
    return substr((string)($_SERVER['REMOTE_ADDR'] ?? ''), 0, 45);
}

function is_https(): bool
{
    return (!empty($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off')
        || (($_SERVER['HTTP_X_FORWARDED_PROTO'] ?? '') === 'https')
        || ((int)($_SERVER['SERVER_PORT'] ?? 0) === 443);
}

function base_url(): string
{
    $host = $_SERVER['HTTP_HOST'] ?? 'localhost';
    $dir = rtrim(str_replace('\\', '/', dirname($_SERVER['SCRIPT_NAME'] ?? '/')), '/');
    return (is_https() ? 'https' : 'http') . '://' . $host . $dir;
}

function clean_domain(string $d): string
{
    $d = strtolower(trim($d));
    $d = preg_replace('#^[a-z]+://#', '', $d);
    $d = preg_replace('#[/?\#:].*$#', '', $d);
    $d = preg_replace('/^www\./', '', $d);
    if (!preg_match('/^(?=.{3,190}$)([a-z0-9-]+\.)+[a-z0-9-]{2,}$/', $d)) {
        return '';
    }
    return $d;
}

function valid_time(string $t): bool
{
    return (bool)preg_match('/^([01]\d|2[0-3]):[0-5]\d$|^24:00$/', $t);
}

/** Einfaches Rate-Limit (z. B. Login, Kopplung) */
function throttle_hit(string $key, int $max, int $window): bool
{
    $r = q1('SELECT n, until_at FROM throttle WHERE k=?', [$key]);
    $t = time();
    if (!$r || $r['until_at'] < $t) {
        db_put('throttle', ['k' => $key], ['n' => 1, 'until_at' => $t + $window]);
        return true;
    }
    if ($r['n'] >= $max) {
        return false;
    }
    q('UPDATE throttle SET n=n+1 WHERE k=?', [$key]);
    return true;
}

function audit(string $action, string $detail = ''): void
{
    db_insert('audit', ['user_id' => (int)($_SESSION['uid'] ?? 0), 'action' => $action, 'detail' => mb_substr($detail, 0, 2000), 'ip' => client_ip(), 'created_at' => now()]);
}

function add_event(int $childId, int $deviceId, string $type, string $level, string $title, string $detail = '', ?string $at = null): int
{
    return db_insert('events', [
        'child_id' => $childId, 'device_id' => $deviceId, 'type' => mb_substr($type, 0, 30), 'level' => $level,
        'title' => mb_substr($title, 0, 190), 'detail' => mb_substr($detail, 0, 1000), 'created_at' => $at ?: now(),
    ]);
}

function fmt_min(int $sec): string
{
    $m = intdiv(max(0, $sec), 60);
    return $m >= 60 ? intdiv($m, 60) . ' h ' . str_pad((string)($m % 60), 2, '0', STR_PAD_LEFT) . ' min' : $m . ' min';
}

/** Wartung: Offline-Warnungen, alte Daten löschen. Läuft per Cron oder höchstens 1×/Min. beim Seitenaufruf. */
function maintenance(bool $force = false): void
{
    $last = (int)setting('maint_last', 0);
    if (!$force && time() - $last < 60) {
        return;
    }
    set_setting('maint_last', time());
    // Geräte, die seit > 3 Min. nichts gemeldet haben
    $limit = date('Y-m-d H:i:s', time() - 180);
    foreach (qall('SELECT d.*, c.name AS child FROM devices d LEFT JOIN children c ON c.id=d.child_id WHERE d.revoked=0 AND d.offline_alerted=0 AND d.last_seen IS NOT NULL AND d.last_seen < ?', [$limit]) as $d) {
        q('UPDATE devices SET offline_alerted=1 WHERE id=?', [$d['id']]);
        $st = json_decode((string)$d['status'], true) ?: [];
        if (!empty($st['shutdown'])) {
            continue; // ordnungsgemäß heruntergefahren
        }
        add_event((int)$d['child_id'], (int)$d['id'], 'offline', 'info', 'Gerät offline: ' . $d['name'], 'Letzter Kontakt ' . date('d.m. H:i', strtotime($d['last_seen'])));
        if (setting('push_offline') === '1') {
            push_parents('Gerät offline', ($d['child'] ?? '') . ': ' . $d['name'] . ' meldet sich nicht mehr.', 'offline-' . $d['id']);
        }
    }
    // Aufbewahrung
    if (time() - (int)setting('cleanup_last', 0) > 86400) {
        set_setting('cleanup_last', time());
        $days = max(7, (int)setting('retention_days', 60));
        $cut = date('Y-m-d', strtotime("-$days days"));
        q('DELETE FROM usage_apps WHERE day < ?', [$cut]);
        q('DELETE FROM usage_web WHERE day < ?', [$cut]);
        q('DELETE FROM events WHERE created_at < ?', [$cut . ' 00:00:00']);
        q('DELETE FROM batches WHERE created_at < ?', [date('Y-m-d H:i:s', strtotime('-14 days'))]);
        q('DELETE FROM usage_hourly WHERE day < ?', [date('Y-m-d', strtotime('-400 days'))]);
        q('DELETE FROM commands WHERE created_at < ?', [date('Y-m-d H:i:s', strtotime('-7 days'))]);
        q('DELETE FROM site_grants WHERE until_at < ?', [now()]);
        q('DELETE FROM pair_codes WHERE expires_at < ?', [date('Y-m-d H:i:s', strtotime('-1 day'))]);
        q('DELETE FROM throttle WHERE until_at < ?', [time()]);
    }
}

app_boot();
