<?php
declare(strict_types=1);

function session_boot(): void
{
    if (session_status() === PHP_SESSION_ACTIVE) {
        return;
    }
    session_name('MSFCSESS');
    session_set_cookie_params(['lifetime' => 60 * 60 * 24 * 60, 'path' => '/', 'httponly' => true, 'samesite' => 'Lax', 'secure' => is_https()]);
    ini_set('session.gc_maxlifetime', (string)(60 * 60 * 24 * 60));
    ini_set('session.use_strict_mode', '1');
    $dir = DATA . '/sessions';
    if (!is_dir($dir)) {
        @mkdir($dir, 0700, true);
    }
    if (is_writable($dir)) {
        session_save_path($dir);
    }
    session_start();
    if (empty($_SESSION['csrf'])) {
        $_SESSION['csrf'] = bin2hex(random_bytes(24));
    }
}

function current_user(): ?array
{
    if (empty($_SESSION['uid'])) {
        return null;
    }
    static $u = false;
    if ($u === false) {
        $u = q1('SELECT id, name, email, dash_theme FROM users WHERE id=?', [$_SESSION['uid']]);
    }
    return $u ?: null;
}

function require_user(): array
{
    $u = current_user();
    if (!$u) {
        fail('Bitte anmelden', 401);
    }
    return $u;
}

function check_csrf(): void
{
    $t = $_SERVER['HTTP_X_CSRF'] ?? '';
    if (!is_string($t) || !hash_equals((string)($_SESSION['csrf'] ?? ''), $t)) {
        fail('Sicherheitstoken ungültig – Seite neu laden', 419);
    }
}
