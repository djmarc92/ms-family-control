# MS Family Control – Android-App

Kinderschutz fürs Android-Handy (ab Android 13), passend zum MS-Family-Control-Server (ab 1.6.0).
Gleiche Regeln wie am Windows-PC: Zeitlimits, Bettzeit (Fr-Abend = Wochenende, So-Abend = Schultag),
gesperrte Apps & Spiele, strenger Modus, Webfilter (Porno/Betrug/… + SafeSearch), „Mehr Zeit anfragen“,
Nachrichten der Eltern als Vollbild – im Design des Kindes.

## APK bauen

**Fertige APK:** unter *Releases* dieses Repositories.

**Selbst bauen (GitHub Actions):** Repository forken → Actions aktivieren → der Workflow
„Android-APK bauen“ erzeugt die APK als Artifact. Ohne eigene Secrets wird ein Test-Schlüssel erzeugt –
für den Dauereinsatz einen eigenen Schlüssel anlegen und als Secrets hinterlegen
(`MSFC_KEYSTORE_BASE64`, `MSFC_KEYSTORE_PASSWORD`, `MSFC_KEY_PASSWORD`, Alias `msfc`), damit sich
Updates drüberinstallieren lassen.

```bash
keytool -genkeypair -keystore release.jks -alias msfc -keyalg RSA -keysize 3072 -validity 15000
base64 -w0 release.jks   # → Secret MSFC_KEYSTORE_BASE64
```

**Lokal:** `./build-local.sh` (aapt, dalvik-exchange, zipalign, apksigner, `ANDROID_JAR` = android.jar API 34;
Schlüssel unter `keys/release.jks` oder per `MSFC_KEYSTORE`).

Version erhöhen: `./set-version.sh 1.0.1`

## Einrichtung am Kinder-Handy

1. APK aufs Handy laden (Control Center → Geräte & Download) und installieren.
2. App öffnen → Server-Adresse → Masterpasswort → Kopplungscode („Gerät koppeln“ im Control Center).
3. Berechtigungen nacheinander einschalten: Benachrichtigungen, Nutzungszugriff, Über anderen Apps,
   Bedienungshilfe, Geräteadministrator, Webfilter (VPN), Akku.
   **Android 13+:** Ist der Schalter der Bedienungshilfe grau, zuerst App-Info → ⋮ (oben rechts) →
   „Eingeschränkte Einstellungen zulassen“.
4. Empfohlen: VPN-Einstellungen → MS Family Control → „Durchgehend aktives VPN“ +
   „Verbindungen ohne VPN blockieren“. „Privates DNS“ auf Automatisch/Aus lassen.

## Was das Kind nicht kann
- App deinstallieren (Geräteadministrator) oder App-Info/Berechtigungen/VPN/Bedienungshilfe der
  Familien-App öffnen → Sperrbildschirm „Geschützter Bereich“, die Eltern bekommen eine Meldung.
- Fehlt eine Berechtigung, steht das im Control Center am Gerät („Schutz: fehlt …“) + Push.

## Grenzen (ohne Zurücksetzen des Handys)
- Über den **abgesicherten Modus** von Android lässt sich jede normale App deinstallieren – das Gerät
  meldet sich dann aber nicht mehr bzw. als „Schutz geschwächt“.
- Während einer Sperre bleiben Telefon, SMS-App, Kontakte und Wecker benutzbar (Notfall, Eltern anrufen).
- Webseiten-Statistik: Android meldet gesperrte Seiten, aber keine Verweildauer pro Webseite.
