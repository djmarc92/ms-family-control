<?php
declare(strict_types=1);

/*
 * Web Push (RFC 8030/8291/8292) ohne Composer – VAPID (ES256) + aes128gcm.
 * Funktioniert mit iPhone-Webapps (iOS 16.4+, zum Home-Bildschirm hinzugefügt), Chrome, Edge, Firefox.
 */

function b64u_enc(string $s): string
{
    return rtrim(strtr(base64_encode($s), '+/', '-_'), '=');
}

function b64u_dec(string $s): string
{
    $s = strtr($s, '-_', '+/');
    return (string)base64_decode($s . str_repeat('=', (4 - strlen($s) % 4) % 4));
}

function ec_new_key()
{
    $k = openssl_pkey_new(['curve_name' => 'prime256v1', 'private_key_type' => OPENSSL_KEYTYPE_EC]);
    if (!$k) {
        throw new RuntimeException('OpenSSL kann keinen EC-Schlüssel erzeugen');
    }
    return $k;
}

function ec_raw_pub($key): string
{
    $d = openssl_pkey_get_details($key);
    return "\x04" . str_pad($d['ec']['x'], 32, "\0", STR_PAD_LEFT) . str_pad($d['ec']['y'], 32, "\0", STR_PAD_LEFT);
}

function ec_pub_pem(string $raw65): string
{
    $der = hex2bin('3059301306072a8648ce3d020106082a8648ce3d030107034200') . $raw65;
    return "-----BEGIN PUBLIC KEY-----\n" . chunk_split(base64_encode($der), 64, "\n") . "-----END PUBLIC KEY-----\n";
}

function vapid_keys(): array
{
    $pub = setting('vapid_public');
    $pem = setting('vapid_private');
    if (!$pub || !$pem) {
        $k = ec_new_key();
        openssl_pkey_export($k, $pem);
        $pub = b64u_enc(ec_raw_pub($k));
        set_setting('vapid_public', $pub);
        set_setting('vapid_private', $pem);
    }
    return [$pub, $pem];
}

function der_sig_to_raw(string $der): string
{
    $pos = 2;
    if (ord($der[1]) & 0x80) {
        $pos += ord($der[1]) & 0x7f;
    }
    $out = '';
    for ($i = 0; $i < 2; $i++) {
        $pos++; // 0x02
        $len = ord($der[$pos++]);
        $int = substr($der, $pos, $len);
        $pos += $len;
        $int = ltrim($int, "\0");
        $out .= str_pad($int, 32, "\0", STR_PAD_LEFT);
    }
    return $out;
}

function vapid_header(string $endpoint): string
{
    [$pub, $pem] = vapid_keys();
    $u = parse_url($endpoint);
    $aud = $u['scheme'] . '://' . $u['host'] . (isset($u['port']) ? ':' . $u['port'] : '');
    $sub = setting('push_subject') ?: 'mailto:admin@localhost.localdomain';
    $h = b64u_enc(json_encode(['typ' => 'JWT', 'alg' => 'ES256']));
    $p = b64u_enc(json_encode(['aud' => $aud, 'exp' => time() + 12 * 3600, 'sub' => $sub]));
    openssl_sign("$h.$p", $sig, $pem, OPENSSL_ALGO_SHA256);
    return 'vapid t=' . "$h.$p." . b64u_enc(der_sig_to_raw($sig)) . ', k=' . $pub;
}

/** aes128gcm-Verschlüsselung der Nutzlast (RFC 8291) */
function push_encrypt(string $payload, string $uaPubB64, string $authB64): string
{
    $uaPub = b64u_dec($uaPubB64);
    $auth = b64u_dec($authB64);
    if (strlen($uaPub) !== 65 || strlen($auth) < 16) {
        throw new InvalidArgumentException('Ungültige Push-Schlüssel');
    }
    $eph = ec_new_key();
    $asPub = ec_raw_pub($eph);
    $shared = openssl_pkey_derive(ec_pub_pem($uaPub), $eph, 32);
    if ($shared === false) {
        throw new RuntimeException('ECDH fehlgeschlagen');
    }
    $ikm = hash_hkdf('sha256', $shared, 32, "WebPush: info\0" . $uaPub . $asPub, $auth);
    $salt = random_bytes(16);
    $cek = hash_hkdf('sha256', $ikm, 16, "Content-Encoding: aes128gcm\0", $salt);
    $nonce = hash_hkdf('sha256', $ikm, 12, "Content-Encoding: nonce\0", $salt);
    $tag = '';
    $ct = openssl_encrypt($payload . "\x02", 'aes-128-gcm', $cek, OPENSSL_RAW_DATA, $nonce, $tag, '', 16);
    return $salt . pack('N', 4096) . chr(65) . $asPub . $ct . $tag;
}

/** Sendet an ein Abo. Rückgabe: HTTP-Status (0 = Netzwerkfehler) */
function push_send(array $sub, array $msg): int
{
    $body = push_encrypt(json_encode($msg, JSON_UNESCAPED_UNICODE), $sub['p256dh'], $sub['auth']);
    $headers = [
        'Authorization: ' . vapid_header($sub['endpoint']),
        'Content-Encoding: aes128gcm',
        'Content-Type: application/octet-stream',
        'TTL: 86400',
        'Urgency: high',
    ];
    if (!empty($msg['tag'])) {
        $headers[] = 'Topic: ' . substr(preg_replace('/[^A-Za-z0-9_-]/', '', (string)$msg['tag']), 0, 32);
    }
    $ch = curl_init($sub['endpoint']);
    curl_setopt_array($ch, [CURLOPT_POST => true, CURLOPT_POSTFIELDS => $body, CURLOPT_HTTPHEADER => $headers, CURLOPT_RETURNTRANSFER => true, CURLOPT_TIMEOUT => 10]);
    curl_exec($ch);
    $code = (int)curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
    curl_close($ch);
    return $code;
}

/** Benachrichtigt alle Eltern-Geräte (iPhone-Webapp usw.) */
function push_parents(string $title, string $body, string $tag = '', string $url = './', array $actions = []): int
{
    $sent = 0;
    try {
        foreach (qall('SELECT * FROM push_subs') as $s) {
            try {
                $code = push_send($s, ['title' => $title, 'body' => $body, 'tag' => $tag, 'url' => $url] + ($actions ? ['actions' => $actions] : []));
            } catch (Throwable $e) {
                $code = 0;
            }
            if ($code === 404 || $code === 410) {
                q('DELETE FROM push_subs WHERE id=?', [$s['id']]);
            } elseif ($code >= 200 && $code < 300) {
                $sent++;
            }
        }
    } catch (Throwable $e) {
    }
    return $sent;
}
