<?php
declare(strict_types=1);

/*
 * Designs für die Kinder-PCs (getrennt vom Eltern-Dashboard, das immer Cyberpunk bleibt).
 * Pro Kind: Standard-Design + optional anderes Design je Ereignis + eigene Texte.
 */
const THEME_EVENTS = [
    'time_expired' => 'Bildschirmzeit aufgebraucht',
    'bedtime' => 'Schlafenszeit',
    'access_blocked' => 'PC gesperrt (Eltern / Schulzeit / zu früh)',
    'app_blocked' => 'App gesperrt',
    'game_blocked' => 'Spiel gesperrt',
    'website_blocked' => 'Webseite gesperrt',
    'parent_message' => 'Nachricht von den Eltern',
];

function theme_catalog(): array
{
    return [
        'anime' => ['name' => 'Anime / Cyberpunk', 'desc' => 'Dunkelblau, Neon-Cyan & Violett, Roboter-Maskottchen, Glas-Karten', 'bg' => '#050e23', 'accent' => '#05d8ff', 'secondary' => '#c12aff', 'text' => '#ffffff'],
        'neon' => ['name' => 'Modern / Neon Tech', 'desc' => 'Sachlich-futuristisch ohne Figuren, leuchtende Symbole', 'bg' => '#071222', 'accent' => '#00d6ff', 'secondary' => '#ab31f5', 'text' => '#eaf6ff'],
        'family' => ['name' => 'Family / Friendly', 'desc' => 'Hell und freundlich, runde Karten, Roboter-Maskottchen', 'bg' => '#e7f6ff', 'accent' => '#1469d8', 'secondary' => '#22ba94', 'text' => '#16223d'],
        'manga' => ['name' => 'Anime / Charaktere', 'desc' => 'Große Anime-Figuren, Neon-Pink & Cyan, HUD-Rahmen', 'bg' => '#120a2a', 'accent' => '#ff2d6f', 'secondary' => '#21e6ff', 'text' => '#ffffff'],
        'army' => ['name' => 'Army / Tactical', 'desc' => 'Dunkelgrau mit Warnfarben, HUD-Optik, robuste Karten', 'bg' => '#15191e', 'accent' => '#f6b800', 'secondary' => '#ed5339', 'text' => '#f4f1e8'],
    ];
}

/** Standardtexte; {name} = App/Spiel/Webseite, {until} = Uhrzeit, {child} = Kind */
function theme_default_texts(): array
{
    return [
        'time_expired' => ['title' => 'Ätsch Bätsch – Zeit ist um!', 'text' => 'Deine Bildschirmzeit ist für heute aufgebraucht.'],
        'bedtime' => ['title' => 'Zeit fürs Bett!', 'text' => 'Gute Nacht! Morgen kannst du wieder an den PC.'],
        'access_blocked' => ['title' => 'Zugriff nicht erlaubt!', 'text' => 'Der PC ist gerade durch deine Familienregeln gesperrt.'],
        'app_blocked' => ['title' => 'Diese App ist nicht erlaubt!', 'text' => 'Die Anwendung {name} wurde durch deine Familienregeln gesperrt.'],
        'game_blocked' => ['title' => 'Dieses Spiel ist nicht erlaubt!', 'text' => '{name} darf momentan nicht gestartet werden.'],
        'website_blocked' => ['title' => 'Diese Webseite ist nicht erlaubt!', 'text' => 'Der Zugriff auf {name} wurde durch deine Familienregeln gesperrt.'],
        'parent_message' => ['title' => 'Nachricht von Mama & Papa', 'text' => ''],
    ];
}

function child_theme(int $childId): array
{
    $r = q1('SELECT * FROM child_themes WHERE child_id=?', [$childId]);
    $cat = theme_catalog();
    $theme = $r && isset($cat[$r['theme']]) ? $r['theme'] : 'anime';
    $events = $r ? (json_decode((string)$r['events'], true) ?: []) : [];
    $texts = $r ? (json_decode((string)$r['texts'], true) ?: []) : [];
    $ev = [];
    foreach (THEME_EVENTS as $k => $_) {
        $ev[$k] = isset($events[$k], $cat[$events[$k]]) ? $events[$k] : '';
    }
    $tx = [];
    foreach (theme_default_texts() as $k => $d) {
        $tx[$k] = ['title' => (string)($texts[$k]['title'] ?? ''), 'text' => (string)($texts[$k]['text'] ?? '')];
    }
    return [
        'theme' => $theme,
        'accent' => $r && preg_match('/^#[0-9a-f]{6}$/i', (string)$r['accent']) ? $r['accent'] : '',
        'scale' => $r ? max(80, min(160, (int)$r['scale'])) : 100,
        'anim' => $r ? (int)$r['anim'] === 1 : true,
        'events' => $ev,
        'texts' => $tx,
        'version' => $r ? (string)$r['updated_at'] : '',
    ];
}

/** Aufgelöste Konfiguration für den Windows-Agenten (eigene Texte schon mit Standard gefüllt) */
function theme_for_device(int $childId): array
{
    $t = child_theme($childId);
    $def = theme_default_texts();
    $out = ['default' => $t['theme'], 'accent' => $t['accent'], 'scale' => $t['scale'], 'anim' => $t['anim'], 'version' => $t['version'], 'events' => []];
    foreach (THEME_EVENTS as $k => $_) {
        $out['events'][$k] = [
            'theme' => $t['events'][$k] ?: $t['theme'],
            'title' => $t['texts'][$k]['title'] !== '' ? $t['texts'][$k]['title'] : $def[$k]['title'],
            'text' => $t['texts'][$k]['text'] !== '' ? $t['texts'][$k]['text'] : $def[$k]['text'],
            'custom' => $t['texts'][$k]['title'] !== '' || $t['texts'][$k]['text'] !== '',
        ];
    }
    return $out;
}
