# MS Family Control 1.6.0 – Family Control Center

Bildschirmzeit & Jugendschutz für die **Windows-PCs und Android-Handys** der Kinder – gesteuert über einen eigenen kleinen Linux-Server mit Webinterface im Cyberpunk-Design, nutzbar als Webapp auf dem iPhone.

## ⬇ Download

Fertige Dateien unter **[Releases](../../releases/latest)**:

| Datei | Inhalt |
|---|---|
| `MS-Family-Control-Server-1.6.0.zip` | Server (PHP) inkl. `install.sh` – installiert alles Nötige auf Debian/Ubuntu |
| `MSFamilyControl-Setup-1.5.1.zip` | Windows-App für den Kinder-PC |
| `MSFamilyControl-Android-1.0.0.zip` | Android-App (APK, ab Android 13) |
| `MS-Family-Control-Browser-Erweiterungen-1.3.0.zip` | Erweiterungen für Chrome/Edge/Firefox |

Schnellstart: Server-ZIP entpacken → `sudo bash install.sh` → im Browser öffnen → Kinder anlegen → Geräte koppeln.

**Ordner im Repository:** `server/` · `windows/` (C#, mit mono baubar: `windows/build.sh`) · `extensions/` · `android/` (eigene README) · `install.sh`

## © Rechte

© 2026 Marc-André Straßen – **alle Rechte vorbehalten.** Der Quellcode ist öffentlich einsehbar, steht aber unter keiner Open-Source-Lizenz. Für Nutzung, Weitergabe oder Änderungen bitte vorher Kontakt aufnehmen.


```
ms-family-control/
├─ install.sh            ← Server-Installation (installiert alles Nötige selbst)
├─ server/               ← Webinterface + APIs (PHP 8.1+, ohne Composer, SQLite oder MariaDB)
│  └─ downloads/         ← MSFamilyControl-Setup.exe + Browser-Erweiterungen (fertig gebaut)
├─ windows/              ← Quellcode der Windows-App (C#, .NET Framework 4.8) + build.sh
└─ extensions/           ← Quellcode der Browser-Erweiterungen (Chrome/Edge MV3, Firefox) + build.sh
```

---

## 1 · Server installieren (Debian / Ubuntu)

```bash
unzip MS-Family-Control-1.0.0.zip && cd ms-family-control
sudo bash install.sh
```

Das Skript fragt nur Port (Standard **8080**), Installationsordner und Datenbank (SQLite oder MariaDB) und erledigt dann alles allein:

- installiert **Nginx, PHP-FPM** (bei zu alter Distribution automatisch PHP 8.3 aus der offiziellen Zusatzquelle) inkl. `sqlite3, mysql, curl, gd, mbstring, xml, zip`
- optional **MariaDB** inkl. Datenbank + Benutzer mit Zufallspasswort (wird im Assistenten automatisch eingetragen)
- kopiert die Dateien, setzt Rechte, sperrt `data/` und `lib/` im Webserver
- richtet den **Cronjob** (jede Minute: Offline-Warnungen, Aufräumen) und ggf. die **ufw-Firewall** ein
- zeigt am Ende Adresse + **Einrichtungscode** an

Danach `http://<server-ip>:8080/install.php` öffnen → Code eingeben → Eltern-Konto, Familienname, Bundesland anlegen. Fertig.

Ohne Rückfragen: `sudo PORT=8080 DB=sqlite bash install.sh --yes`
**Update:** neues Paket entpacken und `sudo bash install.sh` erneut ausführen – Daten, Uploads und Einstellungen bleiben erhalten, Datenbank-Migrationen laufen automatisch.
**aaPanel:** wird erkannt; dann nur Dateien kopieren + Cronjob, Website im Panel anlegen und `data/` + `lib/` per Nginx-Regel sperren (`location ~ ^/(data|lib)/ { deny all; }`).

### Cloudflare / HTTPS (für Push aufs iPhone)
Push-Benachrichtigungen funktionieren nur über HTTPS. Am einfachsten ein **Cloudflare-Tunnel** auf `http://localhost:8080` mit deiner Domain. Der Server erkennt HTTPS hinter Cloudflare automatisch (`X-Forwarded-Proto`), Cookies werden dann als „Secure“ gesetzt. In der Windows-App kannst du die Server-Adresse später im Eltern-Bereich auf `https://…` umstellen.

## 2 · iPhone-Webapp

Control Center in **Safari** öffnen → Teilen → **„Zum Home-Bildschirm“** → App vom Home-Bildschirm starten → *Einstellungen → Push aktivieren* (iOS 16.4+). Danach kommen aufs Handy:

- 🙋 „Aaron bittet um 30 Min. mehr Zeit“ → antippen → genehmigen (+15/+30/+60/andere) oder ablehnen
- ⏰ Zeit abgelaufen / Bettzeit, ⚠ Manipulationsversuche (App beendet, falsches Masterpasswort, Uhr verstellt, Browser-Erweiterung fehlt), optional 📴 PC offline

## 2b · Android-App auf dem Kinder-Handy (ab Android 13)

`MSFamilyControl.apk` liegt unter **Geräte & Download** (bzw. `server/downloads/`). Auf dem Handy installieren,
öffnen, Server-Adresse + Masterpasswort + Kopplungscode („Gerät koppeln“ → Tab „Android-Handy“) eingeben,
dann führt der Assistent durch die Berechtigungen. Kein Zurücksetzen des Handys nötig.
Gleiche Regeln wie am PC; Webfilter läuft als lokales VPN nur für DNS; SafeSearch/YouTube-Filter werden
erzwungen. Quellcode + GitHub-Build: Paket `MSFamilyControl-Android-Projekt` (README darin).
Im Geräte-Bereich zeigt jedes Handy, ob alle Schutz-Berechtigungen aktiv sind (sonst „⚠ fehlt …“).

## 3 · Windows-App auf dem Kinder-PC

1. Im Control Center: **Kinder-Profile** anlegen → **Geräte & Download → PC koppeln** → 8-stelliger Kopplungscode (1 Std. gültig).
2. `MSFamilyControl-Setup.exe` auf dem Kinder-PC mit einem **Administrator-/Eltern-Konto** starten.
   *Hinweis:* Die EXE ist nicht code-signiert → SmartScreen: „Weitere Informationen → Trotzdem ausführen“.
3. Assistent: **Server-URL mit Port** (z. B. `http://192.168.178.20:8080`) → Verbindung testen → Kopplungscode → **Masterpasswort** festlegen → Eltern-Konten auswählen, die nicht überwacht werden.
4. Die App installiert sich nach `C:\Program Files\MS Family Control`, läuft als **Windows-Dienst** (startet mit Windows) und startet in jeder Kinder-Sitzung automatisch die sichtbare App (Schild im Infobereich).

**Nicht schließbar:** Die Kinder-App ist gegen Beenden geschützt (Task-Manager → „Zugriff verweigert“). Wird sie trotzdem beendet, startet der Dienst sie sofort neu und meldet es aufs Handy. Reagiert sie bei aktiver Sperre nicht, trennt der Dienst die Windows-Sitzung. Beenden, Pausieren, Entsperren und Deinstallieren nur mit **Masterpasswort** (Tray → Eltern-Bereich, Startmenü oder Sperrbildschirm → „Eltern“). Das Masterpasswort lässt sich auch vom Handy aus ändern (Geräte → 🔑).

**Sperrbildschirme** (Vollbild auf allen Monitoren): „Ätsch Bätsch – Zeit is um! 🤣“, „Zeit fürs Bett 😴“, „Guten Morgen! Noch nicht“, „Pause! PC gesperrt ✋“ – jeweils mit **Mehr Zeit / Ausnahme anfragen** (Dauer, Grund, Nachricht), Herunterfahren, Abmelden. Vorher Vorwarnungen nach 15/5/1 Min. (einstellbar), gesperrte Apps werden sofort geschlossen („Ätsch Bätsch! 😜“). Nachrichten vom Handy („Essen ist fertig!“), Herunterfahren und Abmelden aus der Ferne.

**Offline:** Regeln, Zeitkonto, Bettzeit und Sperrlisten gelten weiter, Daten werden nachgereicht. Die Uhrzeit kommt vom Server (Verstellen der PC-Uhr bringt nichts und wird gemeldet).

## 4 · Browser-Erweiterungen (Chrome, Edge, Firefox)

Die Erweiterung blockiert gesperrte Seiten mit einer eigenen Seite „Ätsch Bätsch – Seite gesperrt!“ (inkl. **Freigabe anfragen**) und misst die Zeit pro Domain. Sie spricht nur mit der Windows-App auf demselben PC – keine Konfiguration nötig. Zusätzlich sperrt die Windows-App Seiten **systemweit** über die hosts-Datei.

- **Chrome/Edge:** im Windows-Konto des Kindes `chrome://extensions` bzw. `edge://extensions` → Entwicklermodus → „Entpackte Erweiterung laden“ → `C:\Program Files\MS Family Control\extensions\chrome-edge`. Feste ID: `lgocfcphplohbknohlaikliendonbbca`.
- **Firefox:** braucht eine von Mozilla signierte Datei – kostenlos und automatisch als „nicht gelistet“, Schritte in `extensions\firefox\ANLEITUNG.txt`. Die signierte `firefox-signed.xpi` wird dann per Richtlinie erzwungen installiert.
- Browser-Richtlinien (Inkognito/InPrivate, Gastmodus, neue Profile, DNS-over-HTTPS aus) gelten **nur in den Kinder-Konten** – Eltern-Konten und deren Erweiterungen bleiben unberührt. **Browser ohne aktive Erweiterung werden automatisch geschlossen** (abschaltbar), andere Browser (Brave, Opera, Tor …) sind standardmäßig als App gesperrt.

### Dashboard-Design (neu in 1.4)
**Einstellungen → Dashboard-Design**, pro Eltern-Konto: Anime/Cyberpunk (Original), Modern/Neon Tech, Family/Friendly (hell), Army/Tactical, Anime/Charaktere oder **Automatisch** (folgt hell/dunkel am iPhone/PC). Schaltet sofort um, ohne Neuladen.

### Designs pro Kind (neu in 1.3)
Menü **Designs (Kinder-PCs)** – getrennt vom Eltern-Dashboard (bleibt Cyberpunk):
- 5 Designs: **Anime / Cyberpunk** (Roboter, deine Originalvorlagen), **Modern / Neon Tech** (ohne Figuren), **Family / Friendly** (hell, Roboter), **Anime / Charaktere** (große Anime-Figuren), **Army / Tactical** (Warnfarben, HUD).
- Pro Kind ein Standard-Design, pro Ereignis optional ein anderes (Zeit aufgebraucht, Schlafenszeit, PC gesperrt, App, Spiel, Webseite, Elternnachricht), eigene Überschrift/Text mit Platzhaltern `{name}` `{until}` `{child}`, Akzentfarbe, Schriftgröße 80–160 %, Einblend-Animation an/aus.
- **Live-Vorschau** für 1366×768 bis 3840×2160, „Auf alle Kinder übertragen“, „Zurücksetzen“, Änderungen landen nach ca. 15 Sek. auf dem PC (auch der gerade offene Sperrbildschirm wechselt), offline gilt das zuletzt empfangene Design.
- Texte und Knöpfe werden immer live gezeichnet (Tastatur: Tab/Pfeile, Enter, Esc). Die Grafiken stecken in der EXE – nichts wird nachgeladen.
- Grenzen: Beim Design „Anime / Cyberpunk“ sind App-, Spiel- und Nachrichtenbild deine Originalvorlagen mit festen Bildtexten (dynamisch: Name/Nachricht). Eigene Hintergrundbilder/Logos hochladen sowie Import/Export von Theme-Paketen sind noch nicht eingebaut. Die Figuren stammen aus den mitgelieferten Bildausschnitten (836 px) und sind auf 4K-Bildschirmen etwas weicher.

### Nachrichten-Center (neu in 1.3)
Menü **Nachrichten**: an ein Kind, mehrere oder alle; Priorität „Wichtig“ (roter Hinweis); Verlauf mit Status ⏳ wartet auf PC → ✓ zugestellt → ✓✓ gelesen. Offline-PCs bekommen die Nachricht beim nächsten Start, doppelte Anzeigen werden verhindert.

### Vollbild-Hinweise (neu in 1.2)
- **App/Spiel gesperrt:** Startet das Kind eine gesperrte App, wird sie sofort beendet und es erscheint „Diese App ist nicht erlaubt“ bzw. „Dieses Spiel ist nicht erlaubt“ mit Namen. **Eltern fragen** schickt eine Freigabe-Anfrage (Dauer, Grund, Nachricht) als Push aufs Handy; genehmigt wird das ganze Programm (z. B. Launcher + Spiel) für die gewählte Zeit. Eigene Programme lassen sich unter App-Sperren als „Spiel“ markieren.
- **Nachricht von deinen Eltern:** Geräte → 💬 Nachricht. Erscheint im Vollbild über allem – auch über laufenden Spielen (randloses Vollbild) und dem Sperrbildschirm, mit Ton. **Verstanden** schickt dir eine Lesebestätigung (Push + Warnungen & Berichte), **Später lesen** erinnert nach 10 Min. erneut (auch über das Tray-Symbol abrufbar).
- Hinweis: Spiele im *exklusiven* Vollbildmodus werden beim Einblenden minimiert – das ist eine Windows-Grenze.

### Filterlisten nach Kategorie (neu in 1.1)
Unter **Webseiten-Sperren** per Haken: Pornografie, Betrug/Fake-Shops, Phishing, Fraud, Glücksspiel, Drogen, Ransomware, Krypto-Mining (Standard an) sowie Raubkopien, Torrent, Spam (optional) – zusammen ca. 1,7 Mio. Domains aus dem *Block List Project* (MIT-Lizenz). Der Server lädt die Listen per Cron automatisch (erstmals ca. 1 Min. nach der Installation, dann täglich), die Windows-App holt nur geänderte Listen und prüft lokal (~17 MB RAM). Falsch gesperrte Seiten unter **„Immer erlauben“** eintragen. Zusätzlich: **SafeSearch** für Google/Bing und **YouTube eingeschränkter Modus** (nur in Kinder-Konten).

### Testmodus für den eigenen PC (neu in 1.1)
App auf deinem PC installieren (Kinderprofil z. B. „Test“ anlegen und koppeln), dann im Control Center unter **Geräte** den Haken **🧪 Testmodus** setzen: Nach ca. 15 Sek. gelten alle Regeln auch für dein Eltern-Konto – Sperrbildschirm, Filter, App-Sperren, Anfragen. Haken raus → dein Konto ist wieder frei, gesetzte Browser-Richtlinien werden automatisch entfernt. Im Testmodus die Erweiterung auch in deinem Chrome laden, sonst wird Chrome geschlossen.

## 5 · Funktionen im Control Center

Abend gehört zum nächsten Tag (Bettzeit richtet sich nach morgen: Fr-Abend = Wochenende, So-Abend = Schultag, in den Ferien Ferien; abschaltbar unter Zeitlimits) · Neue Apps (Programme, die ein Kind zum ersten Mal nutzt, landen automatisch als „NEU“ in der App-Liste; Push aufs Handy mit Erlauben/Sperren; Panel „Neue Apps entdeckt“ im Dashboard; strenger Modus pro Kind: nur Programme aus der Liste laufen, Windows-Bordmittel bleiben erlaubt) · Dashboard (Profile mit Live-Status & Restzeit, 💬-Knopf auf jeder Kinder-Karte bzw. „Nachricht schreiben“ im Kinder-Fenster: Nachrichtenfenster im gewählten Dashboard-Design mit Schnellantworten, Verlauf mit Lesebestätigung und Live-Vorschau im PC-Design des Kindes, Nutzungszeit, Geräte, Sperrseiten, Warnungen, Diagramm Heute/7/30 Tage, Live-Aktivität, Schnell-Limits, App-Kacheln, Modus) · Kinder-Profile (Anime-/Emoji-Avatare oder eigenes Bild, Farbe; Sperren 30 min/1 h/bis morgen, +15/+30/+60, 1 h ohne Limit) · Nutzungsstatistiken (Top-Programme, Top-Webseiten mit 1-Klick-Sperre) · Webseiten-Sperren (alle oder je Kind, befristete Freigaben) · App-Sperren (je Kind, eigene .exe, zuletzt genutzte Programme) · Zeitlimits je Schultag/Wochenende/Ferien mit Erlaubt-ab/Bettzeit · Schulzeit/Ferienmodus (Auto/Schule/Ferien, **Schulferien + Feiertage per Bundesland importieren** über openholidaysapi.org, eigene Brückentage) · Anfragen · Warnungen & Berichte · Geräte · Einstellungen (Push, mehrere Eltern-Konten, Aufbewahrung, Protokoll).

## 6 · Grenzen (ehrlich)

- Das Kind braucht ein **Standard-Benutzerkonto**. Wer Administratorrechte hat, kann jeden Schutz entfernen.
- Strg+Alt+Entf, Herunterfahren und Barrierefreiheit werden bewusst nicht blockiert.
- hosts-Sperren gelten nur für exakte Domains (+ www/m); Subdomains sperrt die Erweiterung. VPNs/fremde Geräte werden nicht erfasst.
- Windows Defender meldet hosts-Änderungen in seltenen Fällen – dann „Zulassen“ wählen.
- Es werden keine Tastatureingaben, Inhalte, Screenshots, Kamera oder Mikrofon erfasst. Das Kind kann jederzeit über das Tray-Symbol sehen, was erfasst wird.

## 7 · Selbst bauen

- Windows-EXE: `bash windows/build.sh` (Linux mit `mono-devel`, erzeugt `windows/out/MSFamilyControl-Setup.exe`, ca. 1,7 MB, läuft auf Windows 10/11 ohne Zusatz-Runtime).
- Erweiterungen: `bash extensions/build.sh`. `extensions/keys/chrome-extension-key.pem` bestimmt die feste Erweiterungs-ID – **privat aufbewahren**.

## 8 · Updates – Einstellungen bleiben erhalten

- **Server:** neues ZIP entpacken, `sudo bash install.sh` erneut ausführen. `data/` (Konfiguration, Datenbank, Sitzungen) und `uploads/` (Profilbilder) werden nie überschrieben, Datenbank-Änderungen laufen automatisch. Bei MariaDB bleibt die Datenbank ohnehin unberührt.
- **Windows-App:** neue `MSFamilyControl-Setup.exe` als Administrator starten → **„Aktualisieren – alles behalten“** → Masterpasswort. Ersetzt nur das Programm; Kopplung, Masterpasswort, ausgenommene Eltern-Konten, Zeitkonto von heute und noch nicht gesendete Daten bleiben erhalten. („Neu einrichten“ koppelt dagegen neu.)

## 9 · Deinstallation

Windows: Startmenü → „MS Family Control deinstallieren“ (oder *Apps & Features*) → Masterpasswort. Entfernt Dienst, Sperren, Richtlinien und Dateien und meldet die Deinstallation ans Control Center.

---
Copyright © 2026 · Entworfen und entwickelt by Marc-André Straßen mit ❤️
