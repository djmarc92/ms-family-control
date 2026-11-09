<?php
declare(strict_types=1);

/**
 * Automatische Updates der Windows-App:
 * Die EXE unter downloads/ (aus dem Server-Paket oder im Portal hochgeladen) wird an alle PCs mit älterer Version verteilt.
 */

const WIN_APP_FILE = 'MSFamilyControl-Setup.exe';

/** Dateiversion aus einer Windows-EXE lesen (VS_FIXEDFILEINFO) – z. B. "1.6.0" */
function pe_version(string $file): string
{
    $d = @file_get_contents($file, false, null, 0, 16 * 1024 * 1024);
    if ($d === false || strncmp($d, 'MZ', 2) !== 0) {
        return '';
    }
    $sig = pack('V', 0xFEEF04BD);
    $off = 0;
    while (($i = strpos($d, $sig, $off)) !== false) {
        if (strlen($d) >= $i + 16) {
            $v = unpack('Vms/Vls', substr($d, $i + 8, 8));
            $parts = [$v['ms'] >> 16, $v['ms'] & 0xffff, $v['ls'] >> 16];
            if ($parts[0] > 0 || $parts[1] > 0) {
                return implode('.', $parts);
            }
        }
        $off = $i + 4;
    }
    return '';
}

/** Infos zur aktuellen Windows-App auf dem Server (zwischengespeichert, solange sich die Datei nicht ändert) */
function win_app_info(): ?array
{
    $f = ROOT . '/downloads/' . WIN_APP_FILE;
    if (!is_file($f)) {
        return null;
    }
    $key = filemtime($f) . ':' . filesize($f);
    $c = json_decode((string)setting('win_app_cache', ''), true);
    if (is_array($c) && ($c['key'] ?? '') === $key) {
        return $c;
    }
    $c = ['key' => $key, 'version' => pe_version($f), 'sha256' => hash_file('sha256', $f), 'size' => filesize($f), 'date' => date('Y-m-d H:i', filemtime($f))];
    set_setting('win_app_cache', json_encode($c));
    return $c;
}

/** Update-Hinweis für ein Gerät, falls der Server eine neuere Windows-App hat */
function update_for_device(array $device, string $deviceVersion): ?array
{
    if (setting('auto_update', '1') !== '1' || stripos((string)$device['os'], 'android') !== false) {
        return null;
    }
    $w = win_app_info();
    if (!$w || $w['version'] === '' || $deviceVersion === '' || version_compare($w['version'], $deviceVersion, '<=')) {
        return null;
    }
    return ['version' => $w['version'], 'sha256' => $w['sha256'], 'size' => $w['size']];
}

/** Hochgeladene EXE oder Setup-ZIP prüfen und als neue Windows-App ablegen */
function win_app_store_upload(string $tmp, string $name): array
{
    $exe = $tmp;
    $cleanup = null;
    if (preg_match('/\.zip$/i', $name)) {
        if (!class_exists('ZipArchive')) {
            fail('Auf dem Server fehlt PHP-Zip – bitte die EXE direkt hochladen.');
        }
        $z = new ZipArchive();
        if ($z->open($tmp) !== true) {
            fail('ZIP-Datei lässt sich nicht öffnen');
        }
        $data = null;
        for ($i = 0; $i < $z->numFiles; $i++) {
            if (preg_match('/MSFamilyControl[^\/]*\.exe$/i', (string)$z->getNameIndex($i))) {
                $data = $z->getFromIndex($i);
                break;
            }
        }
        $z->close();
        if ($data === null) {
            fail('Im ZIP ist keine MSFamilyControl-EXE');
        }
        $exe = tempnam(sys_get_temp_dir(), 'msfc');
        file_put_contents($exe, $data);
        $cleanup = $exe;
    }
    $head = (string)file_get_contents($exe, false, null, 0, 2);
    $ver = pe_version($exe);
    $all = (string)file_get_contents($exe);
    $isOurs = strpos($all, mb_convert_encoding('MS Family Control', 'UTF-16LE', 'UTF-8')) !== false;
    if ($head !== 'MZ' || $ver === '' || !$isOurs) {
        if ($cleanup) {
            @unlink($cleanup);
        }
        fail('Das ist keine MS-Family-Control-Windows-App (EXE oder Setup-ZIP erwartet).');
    }
    $dir = ROOT . '/downloads';
    if (!is_dir($dir)) {
        @mkdir($dir, 0755, true);
    }
    $target = $dir . '/' . WIN_APP_FILE;
    $tmpTarget = $target . '.new';
    if (!copy($exe, $tmpTarget) || !rename($tmpTarget, $target)) {
        fail('Datei konnte nicht gespeichert werden – Schreibrechte für downloads/ prüfen', 500);
    }
    @chmod($target, 0644);
    if ($cleanup) {
        @unlink($cleanup);
    }
    clearstatcache();
    return win_app_info() ?? [];
}
