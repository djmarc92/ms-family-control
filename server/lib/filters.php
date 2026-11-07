<?php
declare(strict_types=1);

/*
 * Kategorie-Filterlisten (Block List Project, MIT-Lizenz, https://github.com/blocklistproject/Lists)
 * Der Server lädt die Listen täglich, die Windows-App holt sie bei Änderung und prüft lokal.
 */
const FILTER_SOURCE = 'https://raw.githubusercontent.com/blocklistproject/Lists/master/alt-version/%s-nl.txt';

function filter_catalog(): array
{
    // key => [Name, Beschreibung, Standard an?, Icon]
    return [
        'porn' => ['Pornografie & Erwachsenen-Inhalte', 'ca. 950.000 Seiten', true, '🔞'],
        'scam' => ['Betrug & Fake-Shops', 'Abzocke, Fake-Gewinnspiele', true, '🎭'],
        'phishing' => ['Phishing', 'gefälschte Login-/Bankseiten', true, '🎣'],
        'fraud' => ['Betrugsseiten (Fraud)', 'Kreditkarten-/Zahlungsbetrug', true, '💳'],
        'gambling' => ['Glücksspiel & Wetten', 'Casinos, Sportwetten, Lootboxen-Seiten', true, '🎰'],
        'drugs' => ['Drogen', 'Handel und Verherrlichung', true, '💊'],
        'ransomware' => ['Erpressungs-Software', 'bekannte Ransomware-Server', true, '🔐'],
        'crypto' => ['Krypto-Mining im Browser', 'heimliches Mining', true, '⛏'],
        'piracy' => ['Raubkopien & illegale Streams', 'kann auch harmlose Seiten treffen', false, '🏴‍☠️'],
        'torrent' => ['Torrent-Seiten', '', false, '🧲'],
        'abuse' => ['Missbrauch / Spam-Seiten', 'sehr groß, viele Treffer', false, '🚫'],
    ];
}

function filter_dir(): string
{
    $d = DATA . '/filters';
    if (!is_dir($d)) {
        @mkdir($d, 0750, true);
    }
    return $d;
}

/** Fehlende Kategorien anlegen (Standard-Auswahl) */
function filter_ensure(): void
{
    foreach (filter_catalog() as $k => $c) {
        if (!qv('SELECT 1 FROM filter_lists WHERE k=?', [$k])) {
            db_insert('filter_lists', ['k' => $k, 'enabled' => $c[2] ? 1 : 0]);
        }
    }
}

/** Lädt eine Liste herunter und speichert sie normalisiert (eine Domain pro Zeile) + gzip */
function filter_download(string $k): array
{
    if (!isset(filter_catalog()[$k])) {
        return ['ok' => false, 'error' => 'Unbekannte Liste'];
    }
    @set_time_limit(300);
    $tmp = tempnam(sys_get_temp_dir(), 'msfc');
    $fh = fopen($tmp, 'w');
    $ch = curl_init(sprintf(FILTER_SOURCE, $k));
    curl_setopt_array($ch, [CURLOPT_FILE => $fh, CURLOPT_TIMEOUT => 240, CURLOPT_FOLLOWLOCATION => true, CURLOPT_ENCODING => '', CURLOPT_USERAGENT => 'MSFamilyControl/' . APP_VERSION]);
    curl_exec($ch);
    $code = (int)curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
    $err = curl_error($ch);
    curl_close($ch);
    fclose($fh);
    if ($code !== 200) {
        @unlink($tmp);
        $msg = 'Download fehlgeschlagen (' . ($err ?: 'HTTP ' . $code) . ')';
        q('UPDATE filter_lists SET error=? WHERE k=?', [mb_substr($msg, 0, 250), $k]);
        return ['ok' => false, 'error' => $msg];
    }
    $dir = filter_dir();
    $in = fopen($tmp, 'r');
    $gz = gzopen("$dir/$k.txt.gz.tmp", 'wb6');
    $n = 0;
    while (($line = fgets($in)) !== false) {
        $line = strtolower(trim($line));
        if ($line === '' || $line[0] === '#' || $line[0] === '!') {
            continue;
        }
        if (preg_match('/^(?:0\.0\.0\.0|127\.0\.0\.1)\s+(\S+)/', $line, $m)) {
            $line = $m[1];
        }
        if (!preg_match('/^[a-z0-9][a-z0-9.-]*\.[a-z0-9-]{2,}$/', $line)) {
            continue;
        }
        gzwrite($gz, $line . "\n");
        $n++;
    }
    fclose($in);
    gzclose($gz);
    @unlink($tmp);
    if ($n < 10) {
        @unlink("$dir/$k.txt.gz.tmp");
        q('UPDATE filter_lists SET error=? WHERE k=?', ['Liste leer oder ungültig', $k]);
        return ['ok' => false, 'error' => 'Liste leer oder ungültig'];
    }
    rename("$dir/$k.txt.gz.tmp", "$dir/$k.txt.gz");
    $hash = substr(sha1_file("$dir/$k.txt.gz"), 0, 16) . '-' . $n;
    q('UPDATE filter_lists SET cnt=?, hash=?, updated_at=?, error=? WHERE k=?', [$n, $hash, now(), '', $k]);
    return ['ok' => true, 'count' => $n];
}

/** Täglich (Cron): aktivierte Listen aktualisieren */
function filter_update_all(bool $force = false): void
{
    filter_ensure();
    foreach (qall('SELECT * FROM filter_lists WHERE enabled=1') as $f) {
        $age = $f['updated_at'] ? time() - strtotime($f['updated_at']) : PHP_INT_MAX;
        if ($force || $age > 86400 || $f['hash'] === '' || !is_file(filter_dir() . '/' . $f['k'] . '.txt.gz')) {
            filter_download($f['k']);
        }
    }
}

function filters_for_device(): array
{
    $out = [];
    foreach (qall("SELECT k, cnt, hash FROM filter_lists WHERE enabled=1 AND hash<>''") as $f) {
        $c = filter_catalog()[$f['k']] ?? null;
        if ($c) {
            $out[] = ['key' => $f['k'], 'name' => $c[0], 'hash' => $f['hash'], 'count' => (int)$f['cnt']];
        }
    }
    return $out;
}
