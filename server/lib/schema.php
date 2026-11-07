<?php
declare(strict_types=1);

/**
 * Idempotente Migrationen. Jede Migration läuft genau einmal (settings.schema_version).
 * Platzhalter: {PK} Primärschlüssel, {ENGINE} Tabellen-Optionen.
 */
function migrations(): array
{
    return [
        1 => [
            "CREATE TABLE IF NOT EXISTS settings (k VARCHAR(64) PRIMARY KEY, v TEXT){ENGINE}",
            "CREATE TABLE IF NOT EXISTS users (id {PK}, name VARCHAR(80) NOT NULL, email VARCHAR(190) NOT NULL UNIQUE, pass_hash VARCHAR(255) NOT NULL, created_at VARCHAR(19) NOT NULL){ENGINE}",
            "CREATE TABLE IF NOT EXISTS children (id {PK}, name VARCHAR(60) NOT NULL, color VARCHAR(16) NOT NULL DEFAULT 'cyan', avatar VARCHAR(120) NOT NULL DEFAULT '', sort INT NOT NULL DEFAULT 0, lock_until VARCHAR(19) NULL, free_until VARCHAR(19) NULL, created_at VARCHAR(19) NOT NULL){ENGINE}",
            "CREATE TABLE IF NOT EXISTS rules (child_id INT NOT NULL, day_type VARCHAR(10) NOT NULL, limit_min INT NOT NULL DEFAULT 120, start_time VARCHAR(5) NOT NULL DEFAULT '07:00', end_time VARCHAR(5) NOT NULL DEFAULT '20:00', PRIMARY KEY(child_id, day_type)){ENGINE}",
            "CREATE TABLE IF NOT EXISTS devices (id {PK}, child_id INT NOT NULL, name VARCHAR(80) NOT NULL, token_hash VARCHAR(64) NOT NULL, os VARCHAR(120) NOT NULL DEFAULT '', version VARCHAR(20) NOT NULL DEFAULT '', last_seen VARCHAR(19) NULL, last_ip VARCHAR(45) NULL, status TEXT NULL, offline_alerted INT NOT NULL DEFAULT 0, master_rev INT NOT NULL DEFAULT 0, master_salt VARCHAR(64) NULL, master_hash VARCHAR(128) NULL, master_iter INT NOT NULL DEFAULT 0, revoked INT NOT NULL DEFAULT 0, created_at VARCHAR(19) NOT NULL){ENGINE}",
            "CREATE TABLE IF NOT EXISTS pair_codes (code VARCHAR(12) PRIMARY KEY, child_id INT NOT NULL, expires_at VARCHAR(19) NOT NULL, used_at VARCHAR(19) NULL){ENGINE}",
            "CREATE TABLE IF NOT EXISTS usage_hourly (child_id INT NOT NULL, day VARCHAR(10) NOT NULL, hour INT NOT NULL, sec INT NOT NULL DEFAULT 0, PRIMARY KEY(child_id, day, hour)){ENGINE}",
            "CREATE TABLE IF NOT EXISTS usage_apps (child_id INT NOT NULL, day VARCHAR(10) NOT NULL, app VARCHAR(120) NOT NULL, label VARCHAR(160) NOT NULL DEFAULT '', sec INT NOT NULL DEFAULT 0, PRIMARY KEY(child_id, day, app)){ENGINE}",
            "CREATE TABLE IF NOT EXISTS usage_web (child_id INT NOT NULL, day VARCHAR(10) NOT NULL, domain VARCHAR(190) NOT NULL, sec INT NOT NULL DEFAULT 0, PRIMARY KEY(child_id, day, domain)){ENGINE}",
            "CREATE TABLE IF NOT EXISTS batches (device_id INT NOT NULL, batch_id VARCHAR(40) NOT NULL, created_at VARCHAR(19) NOT NULL, PRIMARY KEY(device_id, batch_id)){ENGINE}",
            "CREATE TABLE IF NOT EXISTS events (id {PK}, child_id INT NOT NULL DEFAULT 0, device_id INT NOT NULL DEFAULT 0, type VARCHAR(30) NOT NULL, level VARCHAR(10) NOT NULL DEFAULT 'info', title VARCHAR(190) NOT NULL, detail TEXT NULL, seen INT NOT NULL DEFAULT 0, created_at VARCHAR(19) NOT NULL){ENGINE}",
            "CREATE TABLE IF NOT EXISTS blocked_sites (id {PK}, child_id INT NOT NULL DEFAULT 0, domain VARCHAR(190) NOT NULL, created_at VARCHAR(19) NOT NULL){ENGINE}",
            "CREATE TABLE IF NOT EXISTS apps (id {PK}, name VARCHAR(80) NOT NULL, exes VARCHAR(400) NOT NULL, icon VARCHAR(40) NOT NULL DEFAULT '', sort INT NOT NULL DEFAULT 100){ENGINE}",
            "CREATE TABLE IF NOT EXISTS app_blocks (app_id INT NOT NULL, child_id INT NOT NULL, PRIMARY KEY(app_id, child_id)){ENGINE}",
            "CREATE TABLE IF NOT EXISTS bonus (child_id INT NOT NULL, day VARCHAR(10) NOT NULL, minutes INT NOT NULL DEFAULT 0, PRIMARY KEY(child_id, day)){ENGINE}",
            "CREATE TABLE IF NOT EXISTS requests (id {PK}, child_id INT NOT NULL, device_id INT NOT NULL, local_id VARCHAR(40) NOT NULL DEFAULT '', kind VARCHAR(10) NOT NULL, minutes INT NOT NULL DEFAULT 0, reason VARCHAR(30) NOT NULL DEFAULT '', message VARCHAR(300) NOT NULL DEFAULT '', domain VARCHAR(190) NOT NULL DEFAULT '', status VARCHAR(10) NOT NULL DEFAULT 'pending', granted INT NOT NULL DEFAULT 0, decided_at VARCHAR(19) NULL, created_at VARCHAR(19) NOT NULL){ENGINE}",
            "CREATE TABLE IF NOT EXISTS site_grants (id {PK}, child_id INT NOT NULL, domain VARCHAR(190) NOT NULL, until_at VARCHAR(19) NOT NULL){ENGINE}",
            "CREATE TABLE IF NOT EXISTS push_subs (id {PK}, user_id INT NOT NULL, endpoint_hash VARCHAR(64) NOT NULL UNIQUE, endpoint TEXT NOT NULL, p256dh VARCHAR(200) NOT NULL, auth VARCHAR(100) NOT NULL, ua VARCHAR(200) NOT NULL DEFAULT '', created_at VARCHAR(19) NOT NULL){ENGINE}",
            "CREATE TABLE IF NOT EXISTS holidays (id {PK}, name VARCHAR(120) NOT NULL, start_date VARCHAR(10) NOT NULL, end_date VARCHAR(10) NOT NULL, source VARCHAR(20) NOT NULL DEFAULT 'manuell'){ENGINE}",
            "CREATE TABLE IF NOT EXISTS commands (id {PK}, device_id INT NOT NULL, type VARCHAR(20) NOT NULL, payload TEXT NULL, created_at VARCHAR(19) NOT NULL, delivered_at VARCHAR(19) NULL){ENGINE}",
            "CREATE TABLE IF NOT EXISTS audit (id {PK}, user_id INT NOT NULL DEFAULT 0, action VARCHAR(60) NOT NULL, detail TEXT NULL, ip VARCHAR(45) NULL, created_at VARCHAR(19) NOT NULL){ENGINE}",
            "CREATE TABLE IF NOT EXISTS throttle (k VARCHAR(100) PRIMARY KEY, n INT NOT NULL DEFAULT 0, until_at INT NOT NULL DEFAULT 0){ENGINE}",
            "CREATE INDEX IF NOT EXISTS ix_events_created ON events(created_at)",
            "CREATE INDEX IF NOT EXISTS ix_requests_status ON requests(status)",
        ],
        2 => [
            "CREATE TABLE IF NOT EXISTS filter_lists (k VARCHAR(30) PRIMARY KEY, enabled INT NOT NULL DEFAULT 0, cnt INT NOT NULL DEFAULT 0, hash VARCHAR(40) NOT NULL DEFAULT '', updated_at VARCHAR(19) NULL, error VARCHAR(255) NOT NULL DEFAULT ''){ENGINE}",
            "CREATE TABLE IF NOT EXISTS allow_sites (id {PK}, domain VARCHAR(190) NOT NULL, created_at VARCHAR(19) NOT NULL){ENGINE}",
        ],
        3 => [
            "ALTER TABLE devices ADD COLUMN test_mode INT NOT NULL DEFAULT 0",
        ],
        4 => [
            "ALTER TABLE requests ADD COLUMN label VARCHAR(160) NOT NULL DEFAULT ''",
        ],
        6 => [
            "ALTER TABLE users ADD COLUMN dash_theme VARCHAR(20) NOT NULL DEFAULT 'anime'",
        ],
        7 => [
            "ALTER TABLE apps ADD COLUMN is_new INT NOT NULL DEFAULT 0",
            "ALTER TABLE apps ADD COLUMN first_seen VARCHAR(19) NULL",
            "ALTER TABLE apps ADD COLUMN first_child INT NOT NULL DEFAULT 0",
            "ALTER TABLE children ADD COLUMN strict_apps INT NOT NULL DEFAULT 0",
        ],
        5 => [
            "CREATE TABLE IF NOT EXISTS child_themes (child_id INT PRIMARY KEY, theme VARCHAR(20) NOT NULL DEFAULT 'anime', accent VARCHAR(9) NOT NULL DEFAULT '', scale INT NOT NULL DEFAULT 100, anim INT NOT NULL DEFAULT 1, events TEXT NULL, texts TEXT NULL, updated_at VARCHAR(19) NULL){ENGINE}",
            "CREATE TABLE IF NOT EXISTS messages (id {PK}, child_id INT NOT NULL, device_id INT NOT NULL DEFAULT 0, sender VARCHAR(80) NOT NULL DEFAULT '', text VARCHAR(600) NOT NULL, priority VARCHAR(10) NOT NULL DEFAULT 'normal', created_at VARCHAR(19) NOT NULL, delivered_at VARCHAR(19) NULL, read_at VARCHAR(19) NULL){ENGINE}",
        ],
    ];
}

function migrate(PDO $pdo): int
{
    $all = migrations();
    ksort($all);
    $isMy = $pdo->getAttribute(PDO::ATTR_DRIVER_NAME) === 'mysql';
    $pk = $isMy ? 'INT AUTO_INCREMENT PRIMARY KEY' : 'INTEGER PRIMARY KEY AUTOINCREMENT';
    $engine = $isMy ? ' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci' : '';
    $pdo->exec(str_replace(['{PK}', '{ENGINE}'], [$pk, $engine], "CREATE TABLE IF NOT EXISTS settings (k VARCHAR(64) PRIMARY KEY, v TEXT){ENGINE}"));
    $cur = (int)($pdo->query("SELECT v FROM settings WHERE k='schema_version'")->fetchColumn() ?: 0);
    foreach ($all as $ver => $stmts) {
        if ($ver <= $cur) {
            continue;
        }
        foreach ($stmts as $sql) {
            $sql = str_replace(['{PK}', '{ENGINE}'], [$pk, $engine], $sql);
            if ($isMy && str_starts_with($sql, 'CREATE INDEX IF NOT EXISTS')) {
                // MySQL kennt kein IF NOT EXISTS bei Indizes
                $sql = str_replace('IF NOT EXISTS ', '', $sql);
                try {
                    $pdo->exec($sql);
                } catch (PDOException $e) {
                }
                continue;
            }
            $pdo->exec($sql);
        }
        $st = $pdo->prepare($isMy ? "INSERT INTO settings (k,v) VALUES ('schema_version',?) ON DUPLICATE KEY UPDATE v=VALUES(v)" : "INSERT INTO settings (k,v) VALUES ('schema_version',?) ON CONFLICT(k) DO UPDATE SET v=excluded.v");
        $st->execute([(string)$ver]);
        $cur = $ver;
    }
    return $cur;
}

/** Startdaten: bekannte Apps für App-Sperren */
function seed_defaults(): void
{
    if ((int)qv('SELECT COUNT(*) FROM apps') > 0) {
        return;
    }
    $apps = [
        ['Fortnite', 'FortniteClient-Win64-Shipping.exe,FortniteLauncher.exe', 'fortnite'],
        ['Minecraft', 'Minecraft.Windows.exe,MinecraftLauncher.exe,Minecraft.exe', 'minecraft'],
        ['Steam', 'steam.exe,steamwebhelper.exe', 'steam'],
        ['Discord', 'Discord.exe', 'discord'],
        ['Roblox', 'RobloxPlayerBeta.exe,RobloxPlayerLauncher.exe', 'roblox'],
        ['Epic Games', 'EpicGamesLauncher.exe', 'epic'],
        ['TikTok', 'TikTok.exe', 'tiktok'],
        ['Chrome', 'chrome.exe', 'chrome'],
        ['Edge', 'msedge.exe', 'edge'],
        ['Firefox', 'firefox.exe', 'firefox'],
        ['Andere Browser', 'brave.exe,opera.exe,vivaldi.exe,tor.exe,yandex.exe,waterfox.exe,librewolf.exe,chromium.exe', 'browser'],
        ['WhatsApp', 'WhatsApp.exe', 'whatsapp'],
    ];
    $i = 0;
    foreach ($apps as $a) {
        $id = db_insert('apps', ['name' => $a[0], 'exes' => $a[1], 'icon' => $a[2], 'sort' => $i++]);
        if ($a[0] === 'Andere Browser') {
            db_insert('app_blocks', ['app_id' => $id, 'child_id' => 0]);
        }
    }
    $defaults = [
        'family_name' => 'Familie', 'timezone' => 'Europe/Berlin', 'state' => 'NW', 'mode' => 'auto',
        'warn_minutes' => '15,5,1', 'enforce_extension' => '1', 'retention_days' => '60',
        'push_requests' => '1', 'push_tamper' => '1', 'push_offline' => '0', 'push_limit' => '1',
        'school_from' => '07:00', 'school_to' => '15:00',
    ];
    foreach ($defaults as $k => $v) {
        if (setting($k) === null) {
            set_setting($k, $v);
        }
    }
}
