<?php
declare(strict_types=1);

/** PDO-Verbindung (SQLite oder MariaDB/MySQL) – Konfiguration aus data/config.php */
function db(): PDO
{
    static $pdo = null;
    if ($pdo) {
        return $pdo;
    }
    $c = cfg();
    if (!$c) {
        throw new RuntimeException('Nicht installiert');
    }
    $pdo = db_connect($c['db']);
    return $pdo;
}

function db_connect(array $d): PDO
{
    $opt = [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
        PDO::ATTR_EMULATE_PREPARES => false,
    ];
    if (($d['driver'] ?? 'sqlite') === 'mysql') {
        $dsn = sprintf('mysql:host=%s;port=%d;dbname=%s;charset=utf8mb4', $d['host'], (int)($d['port'] ?? 3306), $d['name']);
        $pdo = new PDO($dsn, $d['user'], $d['pass'], $opt);
        $pdo->exec("SET time_zone = '+00:00'");
    } else {
        $pdo = new PDO('sqlite:' . $d['path'], null, null, $opt);
        $pdo->exec('PRAGMA journal_mode=WAL');
        $pdo->exec('PRAGMA busy_timeout=5000');
        $pdo->exec('PRAGMA foreign_keys=ON');
    }
    return $pdo;
}

function db_driver(): string
{
    return db()->getAttribute(PDO::ATTR_DRIVER_NAME) === 'mysql' ? 'mysql' : 'sqlite';
}

function q(string $sql, array $p = []): PDOStatement
{
    $st = db()->prepare($sql);
    $st->execute($p);
    return $st;
}

function q1(string $sql, array $p = []): ?array
{
    $r = q($sql, $p)->fetch();
    return $r === false ? null : $r;
}

function qv(string $sql, array $p = [])
{
    $r = q($sql, $p)->fetchColumn();
    return $r === false ? null : $r;
}

function qall(string $sql, array $p = []): array
{
    return q($sql, $p)->fetchAll();
}

function db_insert(string $table, array $row): int
{
    $cols = array_keys($row);
    $sql = 'INSERT INTO ' . $table . ' (' . implode(',', $cols) . ') VALUES (' . implode(',', array_fill(0, count($cols), '?')) . ')';
    q($sql, array_values($row));
    return (int)db()->lastInsertId();
}

/** Zählt $add zu Spalte $col hinzu (Upsert, portabel SQLite/MySQL) */
function db_add(string $table, array $keys, string $col, int $add, array $extra = []): void
{
    $all = $keys + $extra + [$col => $add];
    $cols = array_keys($all);
    $sql = 'INSERT INTO ' . $table . ' (' . implode(',', $cols) . ') VALUES (' . implode(',', array_fill(0, count($cols), '?')) . ')';
    $upd = ["$col=$col+" . (db_driver() === 'mysql' ? "VALUES($col)" : "excluded.$col")];
    foreach (array_keys($extra) as $e) {
        $upd[] = "$e=" . (db_driver() === 'mysql' ? "VALUES($e)" : "excluded.$e");
    }
    if (db_driver() === 'mysql') {
        $sql .= ' ON DUPLICATE KEY UPDATE ' . implode(',', $upd);
    } else {
        $sql .= ' ON CONFLICT(' . implode(',', array_keys($keys)) . ') DO UPDATE SET ' . implode(',', $upd);
    }
    q($sql, array_values($all));
}

/** Setzt Werte (Upsert) */
function db_put(string $table, array $keys, array $vals): void
{
    $all = $keys + $vals;
    $cols = array_keys($all);
    $sql = 'INSERT INTO ' . $table . ' (' . implode(',', $cols) . ') VALUES (' . implode(',', array_fill(0, count($cols), '?')) . ')';
    $upd = [];
    foreach (array_keys($vals) as $v) {
        $upd[] = "$v=" . (db_driver() === 'mysql' ? "VALUES($v)" : "excluded.$v");
    }
    if (db_driver() === 'mysql') {
        $sql .= ' ON DUPLICATE KEY UPDATE ' . implode(',', $upd);
    } else {
        $sql .= ' ON CONFLICT(' . implode(',', array_keys($keys)) . ') DO UPDATE SET ' . implode(',', $upd);
    }
    q($sql, array_values($all));
}

function setting(string $k, $default = null)
{
    static $cache = null;
    if ($cache === null) {
        $cache = [];
        try {
            foreach (qall('SELECT k,v FROM settings') as $r) {
                $cache[$r['k']] = $r['v'];
            }
        } catch (Throwable $e) {
        }
    }
    if (func_num_args() === 3) { // interner Cache-Reset
        $cache = null;
        return null;
    }
    return $cache[$k] ?? $default;
}

function set_setting(string $k, $v): void
{
    db_put('settings', ['k' => $k], ['v' => (string)$v]);
    setting('', null, true);
}
