#!/usr/bin/env bash
# Baut die Browser-Erweiterungen (Chrome/Edge = MV3, Firefox = MV2) aus extensions/common
set -euo pipefail
cd "$(dirname "$0")"
KEY=$(cat keys/public-key.b64)
VER=1.3.0
rm -rf dist && mkdir -p dist/chrome-edge dist/firefox
for d in dist/chrome-edge dist/firefox; do cp common/* "$d"/; cp assets/* "$d"/; done
cat > dist/chrome-edge/manifest.json <<JSON
{
  "manifest_version": 3,
  "name": "MS Family Control",
  "short_name": "Family Control",
  "version": "$VER",
  "description": "Jugendschutz für Chrome & Edge – arbeitet mit der MS-Family-Control-App auf diesem PC.",
  "key": "$KEY",
  "permissions": ["tabs", "storage", "alarms", "webNavigation", "idle"],
  "host_permissions": ["<all_urls>"],
  "background": { "service_worker": "background.js" },
  "action": { "default_title": "MS Family Control", "default_popup": "popup.html", "default_icon": { "32": "icon-32.png", "128": "icon-128.png" } },
  "icons": { "32": "icon-32.png", "128": "icon-128.png" },
  "incognito": "spanning",
  "minimum_chrome_version": "110"
}
JSON
cat > dist/firefox/manifest.json <<JSON
{
  "manifest_version": 2,
  "name": "MS Family Control",
  "version": "$VER",
  "description": "Jugendschutz für Firefox – arbeitet mit der MS-Family-Control-App auf diesem PC.",
  "permissions": ["tabs", "storage", "alarms", "webNavigation", "idle", "<all_urls>"],
  "background": { "scripts": ["background.js"], "persistent": true },
  "browser_action": { "default_title": "MS Family Control", "default_popup": "popup.html", "default_icon": { "32": "icon-32.png", "128": "icon-128.png" } },
  "icons": { "32": "icon-32.png", "128": "icon-128.png" },
  "browser_specific_settings": { "gecko": { "id": "family-control@mstrassen-it.de", "strict_min_version": "109.0" } }
}
JSON
cat > dist/chrome-edge/ANLEITUNG.txt <<'TXT'
MS Family Control – Erweiterung für Google Chrome und Microsoft Edge
====================================================================
Einmalig im Windows-Konto des Kindes (als Elternteil daneben sitzen):

1. Chrome:  chrome://extensions   ·   Edge:  edge://extensions   öffnen
2. Oben rechts bzw. links „Entwicklermodus“ einschalten
3. „Entpackte Erweiterung laden“ → diesen Ordner wählen:
      C:\Program Files\MS Family Control\extensions\chrome-edge
4. Fertig. Das Symbol (Schild) erscheint in der Symbolleiste.

Die Erweiterungs-ID ist fest: EXTID
Die Windows-App schließt Chrome/Edge im Kinder-Konto automatisch, wenn diese Erweiterung
deaktiviert oder entfernt wurde. Eltern-Konten sind davon nicht betroffen.
TXT
sed -i "s/EXTID/$(cat keys/extension-id.txt)/" dist/chrome-edge/ANLEITUNG.txt
cat > dist/firefox/ANLEITUNG.txt <<'TXT'
MS Family Control – Erweiterung für Firefox
===========================================
Firefox installiert dauerhaft nur von Mozilla signierte Erweiterungen. Die Signatur ist kostenlos
und automatisch („nicht gelistet“, die Erweiterung erscheint NICHT öffentlich im Add-on-Store):

1. Kostenloses Konto auf https://addons.mozilla.org anlegen → Developer Hub → „Neues Add-on einreichen“
2. „Auf eigene Faust“ (On your own / unlisted) wählen und browser-extension-firefox.zip hochladen
3. Nach wenigen Minuten die signierte .xpi herunterladen
4. Datei umbenennen in  firefox-signed.xpi  und kopieren nach
      C:\Program Files\MS Family Control\extensions\firefox-signed.xpi
5. MS-Family-Control-Setup erneut ausführen („Neu einrichten“) – Firefox installiert die Erweiterung
   dann automatisch und erzwungen (Richtlinie), das Kind kann sie nicht entfernen.

Nur zum Ausprobieren: about:debugging → „Dieser Firefox“ → „Temporäres Add-on laden“ → manifest.json
(gilt nur bis zum Neustart von Firefox).

Solange die Erweiterung in Firefox nicht aktiv ist, schließt die Windows-App Firefox automatisch.
TXT
(cd dist/chrome-edge && zip -qr ../browser-extension-chrome-edge.zip .)
(cd dist/firefox && zip -qr ../browser-extension-firefox.zip .)
echo "OK – ID $(cat keys/extension-id.txt)"
