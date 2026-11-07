<?php
declare(strict_types=1);

/**
 * Neue Programme erkennen
 * - automatisch in die App-Liste aufnehmen (als „neu“ markiert)
 * - Eltern per Push informieren (Erlauben / Sperren)
 * - strenger Modus: neue Programme sind für Kinder im strengen Modus sofort gesperrt
 */

/** Windows-Bordmittel, die nie als „neue App“ gelten und im strengen Modus nie gesperrt werden */
const SYSTEM_EXES = [
    'unbekannt', 'msfamilycontrol.exe', 'explorer.exe', 'searchhost.exe', 'searchapp.exe', 'searchui.exe', 'startmenuexperiencehost.exe',
    'shellexperiencehost.exe', 'applicationframehost.exe', 'systemsettings.exe', 'textinputhost.exe', 'lockapp.exe', 'logonui.exe',
    'taskmgr.exe', 'control.exe', 'rundll32.exe', 'dllhost.exe', 'consent.exe', 'sihost.exe', 'ctfmon.exe', 'conhost.exe',
    'openwith.exe', 'pickerhost.exe', 'credentialuibroker.exe', 'useroobebroker.exe', 'werfault.exe', 'smartscreen.exe',
    'notepad.exe', 'calc.exe', 'calculatorapp.exe', 'mspaint.exe', 'snippingtool.exe', 'screenclippinghost.exe', 'screensketch.exe',
    'photos.exe', 'microsoft.photos.exe', 'systemsettingsadminflows.exe', 'securityhealthhost.exe', 'securityhealthtray.exe',
    'windowsinternal.composableshell.experiences.textinput.inputapp.exe', 'mmc.exe', 'msiexec.exe', 'wuauclt.exe', 'gamebar.exe',
    'gamebarftserver.exe', 'widgets.exe', 'phoneexperiencehost.exe', 'quickassist.exe',
];

function app_is_system(string $exe): bool
{
    return in_array(strtolower($exe), SYSTEM_EXES, true);
}

/** exe (klein) => app_id für alle Programme der Liste */
function app_exe_map(bool $reload = false): array
{
    static $m = null;
    if ($m === null || $reload) {
        $m = [];
        foreach (qall('SELECT id, exes FROM apps') as $a) {
            foreach (explode(',', (string)$a['exes']) as $e) {
                $e = strtolower(trim($e));
                if ($e !== '') {
                    $m[$e] = (int)$a['id'];
                }
            }
        }
    }
    return $m;
}

function app_nice_name(string $exe, string $label): string
{
    $label = trim(preg_replace('/\s+/', ' ', strip_tags($label)));
    if ($label === '' || strcasecmp($label, $exe) === 0) {
        $label = preg_replace('/\.exe$/i', '', $exe);
        $label = ucfirst(str_replace(['_', '-'], ' ', $label));
    }
    return mb_substr($label, 0, 80);
}

/**
 * Vom PC gemeldete Programme prüfen und Unbekannte aufnehmen.
 * @param array $found exe => Anzeigename
 * @param bool $attempt true = im strengen Modus beim Start blockiert
 */
function apps_discover(int $cid, int $deviceId, array $found, bool $attempt = false): array
{
    $child = q1('SELECT id, name, strict_apps FROM children WHERE id=?', [$cid]);
    if (!$child) {
        return [];
    }
    $strict = (int)$child['strict_apps'] === 1;
    if (!$strict && !$attempt && setting('auto_add_apps', '1') !== '1') {
        return []; // automatische Aufnahme ausgeschaltet
    }
    $known = app_exe_map();
    $added = [];
    foreach (array_slice($found, 0, 30, true) as $exe => $label) {
        $exe = strtolower(trim((string)$exe));
        $isExe = (bool)preg_match('/^[a-z0-9 ._()+\-]{1,110}\.exe$/', $exe);
        $isPkg = (bool)preg_match('/^[a-z][a-z0-9_]*(\.[a-z0-9_]+){1,8}$/', $exe) && strlen($exe) <= 120; // Android-Paketname
        if ($exe === '' || isset($known[$exe]) || app_is_system($exe) || (!$isExe && !$isPkg)) {
            continue;
        }
        $name = app_nice_name($exe, (string)$label);
        $id = db_insert('apps', ['name' => $name, 'exes' => $exe, 'icon' => '', 'sort' => 300, 'is_new' => 1, 'first_seen' => now(), 'first_child' => $cid]);
        $known[$exe] = $id;
        // strenger Modus: für alle Kinder mit strengem Modus sofort gesperrt
        foreach (qall('SELECT id FROM children WHERE strict_apps=1') as $sc) {
            if (!qv('SELECT 1 FROM app_blocks WHERE app_id=? AND child_id=?', [$id, $sc['id']])) {
                db_insert('app_blocks', ['app_id' => $id, 'child_id' => (int)$sc['id']]);
            }
        }
        $title = $strict ? $child['name'] . ' wollte eine neue App öffnen: ' . $name . ' (gesperrt)' : $child['name'] . ' nutzt zum ersten Mal: ' . $name;
        add_event($cid, $deviceId, 'new_app', $strict ? 'warn' : 'info', $title, $exe);
        if (setting('push_new_app', '1') === '1') {
            push_parents(($strict ? '🔒 ' : '🆕 ') . 'Neue App bei ' . $child['name'], $name . ' (' . $exe . ')' . ($strict ? ' wurde blockiert.' : '') . ' Erlauben oder sperren?', 'newapp-' . $id, './#apps', [
                ['action' => 'allow', 'title' => '✓ Erlauben', 'url' => './#newapp/' . $id . '/allow'],
                ['action' => 'block', 'title' => '⛔ Sperren', 'url' => './#newapp/' . $id . '/block'],
            ]);
        }
        $added[] = $id;
    }
    if ($added) {
        app_exe_map(true);
    }
    return $added;
}

/** Android-Paketnamen bekannter Apps (werden einmalig zum Katalog ergänzt) */
const ANDROID_APPS = [
    'Fortnite' => ['com.epicgames.fortnite'], 'Minecraft' => ['com.mojang.minecraftpe'], 'Steam' => ['com.valvesoftware.android.steam.community'],
    'Discord' => ['com.discord'], 'Roblox' => ['com.roblox.client'], 'Epic Games' => ['com.epicgames.portal'],
    'TikTok' => ['com.zhiliaoapp.musically', 'com.ss.android.ugc.trill', 'com.zhiliaoapp.musically.go'], 'Chrome' => ['com.android.chrome'],
    'Edge' => ['com.microsoft.emmx'], 'Firefox' => ['org.mozilla.firefox', 'org.mozilla.firefox_beta', 'org.mozilla.focus'],
    'Andere Browser' => ['com.brave.browser', 'com.opera.browser', 'com.opera.mini.native', 'com.opera.gx', 'com.vivaldi.browser', 'org.torproject.torbrowser', 'com.yandex.browser', 'com.kiwibrowser.browser', 'com.uc.browser.en', 'org.mozilla.fennec_fdroid'],
    'WhatsApp' => ['com.whatsapp'],
];
const ANDROID_NEW_APPS = [ // Name, Pakete, Symbol (game = Spiel)
    ['YouTube', 'com.google.android.youtube', ''], ['Instagram', 'com.instagram.android', ''], ['Snapchat', 'com.snapchat.android', ''],
    ['Brawl Stars', 'com.supercell.brawlstars', 'game'], ['Clash Royale', 'com.supercell.clashroyale', 'game'], ['Clash of Clans', 'com.supercell.clashofclans', 'game'],
    ['Netflix', 'com.netflix.mediaclient', ''], ['Twitch', 'tv.twitch.android.app', ''], ['Telegram', 'org.telegram.messenger', ''],
    ['Pokémon GO', 'com.nianticlabs.pokemongo', 'game'], ['Subway Surfers', 'com.kiloo.subwaysurf', 'game'], ['Stumble Guys', 'com.kitkagames.fallbuddies', 'game'],
];

function apps_android_seed(): void
{
    if (setting('android_seed', '0') === '1') {
        return;
    }
    set_setting('android_seed', '1');
    foreach (ANDROID_APPS as $name => $pkgs) {
        $a = q1('SELECT id, exes FROM apps WHERE name=?', [$name]);
        if (!$a) {
            continue;
        }
        $ex = array_filter(array_map('trim', explode(',', (string)$a['exes'])));
        $lower = array_map('strtolower', $ex);
        foreach ($pkgs as $p) {
            if (!in_array($p, $lower, true)) {
                $ex[] = $p;
            }
        }
        q('UPDATE apps SET exes=? WHERE id=?', [mb_substr(implode(',', $ex), 0, 1000), $a['id']]);
    }
    $known = app_exe_map(true);
    $sort = (int)qv('SELECT COALESCE(MAX(sort),0) FROM apps') + 1;
    foreach (ANDROID_NEW_APPS as [$name, $pkgs, $icon]) {
        if (isset($known[explode(',', $pkgs)[0]]) || qv('SELECT 1 FROM apps WHERE name=?', [$name])) {
            continue;
        }
        db_insert('apps', ['name' => $name, 'exes' => $pkgs, 'icon' => $icon, 'sort' => $sort++]);
    }
    app_exe_map(true);
}

/** Entscheidung der Eltern zu einer (neuen) App: allow / block, für ein Kind oder alle (child_id 0) */
function app_decide(int $appId, string $decision, int $childId = 0): array
{
    $app = q1('SELECT * FROM apps WHERE id=?', [$appId]);
    if (!$app) {
        fail('App nicht gefunden', 404);
    }
    if ($decision === 'allow') {
        if ($childId > 0) {
            q('DELETE FROM app_blocks WHERE app_id=? AND child_id=?', [$appId, $childId]);
        } else {
            q('DELETE FROM app_blocks WHERE app_id=?', [$appId]);
        }
    } elseif ($decision === 'block') {
        if ($childId > 0) {
            if (!qv('SELECT 1 FROM app_blocks WHERE app_id=? AND child_id=?', [$appId, $childId])) {
                db_insert('app_blocks', ['app_id' => $appId, 'child_id' => $childId]);
            }
        } else {
            q('DELETE FROM app_blocks WHERE app_id=? AND child_id<>0', [$appId]);
            if (!qv('SELECT 1 FROM app_blocks WHERE app_id=? AND child_id=0', [$appId])) {
                db_insert('app_blocks', ['app_id' => $appId, 'child_id' => 0]);
            }
        }
    } elseif ($decision !== 'seen') {
        fail('Unbekannte Entscheidung');
    }
    q('UPDATE apps SET is_new=0 WHERE id=?', [$appId]);
    audit('app_decide', $app['name'] . ' → ' . $decision . ($childId ? " (Kind $childId)" : ''));
    return $app;
}

/** Neu entdeckte, noch nicht entschiedene Apps (für Dashboard / App-Sperren) */
function new_apps_list(): array
{
    $out = [];
    foreach (qall('SELECT a.id, a.name, a.exes, a.first_seen, a.first_child, c.name AS child FROM apps a LEFT JOIN children c ON c.id=a.first_child WHERE a.is_new=1 ORDER BY a.id DESC LIMIT 30') as $a) {
        $blocked = array_map('intval', array_column(qall('SELECT child_id FROM app_blocks WHERE app_id=?', [$a['id']]), 'child_id'));
        $sec = (int)qv('SELECT COALESCE(SUM(sec),0) FROM usage_apps WHERE app=?', [strtolower($a['exes'])]);
        $out[] = ['id' => (int)$a['id'], 'name' => $a['name'], 'exe' => $a['exes'], 'first_seen' => $a['first_seen'], 'child' => $a['child'] ?? '', 'child_id' => (int)$a['first_child'], 'blocked_for' => $blocked, 'sec' => $sec];
    }
    return $out;
}

/** Alle Programm-Dateien der Liste (für den strengen Modus am PC) */
function known_exes(): array
{
    return array_keys(app_exe_map());
}

function app_grants_for(int $childId): array
{
    return array_map(fn($g) => substr($g['domain'], 4), qall("SELECT domain FROM site_grants WHERE child_id=? AND until_at > ? AND domain LIKE 'app:%'", [$childId, now()]));
}
