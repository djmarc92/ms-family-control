#!/usr/bin/env bash
# =====================================================================================
#  MS Family Control – Server-Installation für Debian / Ubuntu
#  Installiert automatisch alles Nötige: Nginx, PHP-FPM (≥ 8.1) inkl. Erweiterungen,
#  optional MariaDB, Cronjob, Firewall-Freigabe – und richtet die Webseite ein.
#
#  Aufruf (als root):   sudo bash install.sh
#  Ohne Rückfragen:     sudo PORT=8080 DB=sqlite bash install.sh --yes
#  Update (gleicher Befehl): Programmdateien werden ersetzt, Daten bleiben erhalten.
#
#  Copyright © 2026 · Entworfen und entwickelt by Marc-André Straßen mit ❤️
# =====================================================================================
set -euo pipefail

C='\033[1;36m'; P='\033[1;35m'; G='\033[1;32m'; Y='\033[1;33m'; R='\033[1;31m'; N='\033[0m'
say()  { echo -e "${C}▸${N} $*"; }
ok()   { echo -e "${G}✔${N} $*"; }
warn() { echo -e "${Y}⚠${N} $*"; }
die()  { echo -e "${R}✖ $*${N}"; exit 1; }

YES=0; [[ "${1:-}" == "--yes" || "${1:-}" == "-y" ]] && YES=1
SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/server"
[[ -f "$SRC/index.php" ]] || die "Ordner 'server' nicht gefunden – install.sh bitte im entpackten Paket ausführen."
[[ $EUID -eq 0 ]] || die "Bitte als root ausführen:  sudo bash install.sh"
command -v apt-get >/dev/null || die "Dieses Skript unterstützt Debian/Ubuntu (apt). Für andere Systeme siehe README."

svc() { # svc start|reload|enable NAME – funktioniert mit und ohne systemd
  local act="$1" name="$2"
  if pidof systemd >/dev/null 2>&1 || [[ -d /run/systemd/system ]]; then
    case "$act" in enable) systemctl enable --now "$name" >/dev/null 2>&1 || true ;; *) systemctl "$act" "$name" >/dev/null 2>&1 || systemctl restart "$name" >/dev/null 2>&1 || true ;; esac
  else
    case "$act" in enable) service "$name" start >/dev/null 2>&1 || true ;; *) service "$name" "$act" >/dev/null 2>&1 || service "$name" restart >/dev/null 2>&1 || true ;; esac
  fi
}

ask() { # ask VAR "Frage" "Standard"
  local __v="$1" __q="$2" __d="$3" __a
  if [[ -n "${!__v:-}" ]]; then return; fi
  if [[ $YES -eq 1 ]]; then printf -v "$__v" '%s' "$__d"; return; fi
  read -r -p "$(echo -e "${P}?${N} $__q [${__d}]: ")" __a </dev/tty || true
  printf -v "$__v" '%s' "${__a:-$__d}"
}

echo -e "${P}"
cat <<'BANNER'
   ╔═══════════════════════════════════════════════════╗
   ║   MS FAMILY CONTROL  ·  Family Control Center      ║
   ║   PC-Nutzung & Jugendschutz – Server-Installation  ║
   ╚═══════════════════════════════════════════════════╝
BANNER
echo -e "${N}"

AAPANEL=0; [[ -d /www/server/panel ]] && AAPANEL=1
if [[ $AAPANEL -eq 1 ]]; then
  warn "aaPanel erkannt – Nginx/PHP werden NICHT per apt installiert (das macht das Panel)."
  ask TARGET "Zielordner (Website-Root im aaPanel)" "/www/wwwroot/family-control"
else
  ask TARGET "Installationsordner" "/var/www/ms-family-control"
  ask PORT "Port für das Webinterface (z. B. 8080; später Cloudflare davor)" "8080"
  ask DB "Datenbank: sqlite (einfach) oder mariadb" "sqlite"
fi
PORT="${PORT:-8080}"; DB="${DB:-sqlite}"
[[ "$PORT" =~ ^[0-9]+$ ]] && (( PORT>0 && PORT<65536 )) || die "Ungültiger Port: $PORT"

export DEBIAN_FRONTEND=noninteractive

# ----------------------------------------------------------------------------- Pakete
if [[ $AAPANEL -eq 0 ]]; then
  say "Paketquellen aktualisieren …"
  apt-get update -qq
  apt-get install -y -qq ca-certificates curl lsb-release gnupg unzip rsync cron >/dev/null

  # PHP-Version prüfen; bei zu alter Distribution (< 8.1) offizielle Zusatzquelle einbinden
  CAND=$(apt-cache policy php-fpm 2>/dev/null | awk '/Candidate:/{print $2}' | sed -E 's/^[0-9]+://; s/[^0-9.].*//')
  PHPV=""
  if [[ -n "$CAND" && "$CAND" != "(none)" ]]; then
    MAJ=${CAND%%.*}; MIN=$(echo "$CAND" | cut -d. -f2)
    if (( MAJ > 8 || (MAJ == 8 && MIN >= 1) )); then PHPV=""; else PHPV="8.3"; fi
  else PHPV="8.3"; fi
  if [[ -n "$PHPV" ]]; then
    say "PHP der Distribution ist zu alt – binde Zusatzquelle für PHP $PHPV ein …"
    . /etc/os-release
    if [[ "$ID" == "ubuntu" ]]; then
      apt-get install -y -qq software-properties-common >/dev/null
      add-apt-repository -y ppa:ondrej/php >/dev/null
    else
      curl -fsSL https://packages.sury.org/php/apt.gpg -o /usr/share/keyrings/sury-php.gpg
      echo "deb [signed-by=/usr/share/keyrings/sury-php.gpg] https://packages.sury.org/php/ $(lsb_release -sc) main" > /etc/apt/sources.list.d/sury-php.list
    fi
    apt-get update -qq
    PKGS="php$PHPV-fpm php$PHPV-cli php$PHPV-sqlite3 php$PHPV-mysql php$PHPV-curl php$PHPV-gd php$PHPV-mbstring php$PHPV-xml php$PHPV-zip"
  else
    PKGS="php-fpm php-cli php-sqlite3 php-mysql php-curl php-gd php-mbstring php-xml php-zip"
  fi
  say "Installiere Nginx und PHP …"
  apt-get install -y -qq nginx $PKGS >/dev/null
  if [[ "$DB" == "mariadb" || "$DB" == "mysql" ]]; then
    say "Installiere MariaDB …"
    apt-get install -y -qq mariadb-server >/dev/null
    svc enable mariadb
  fi
  ok "Pakete installiert"
fi

command -v php >/dev/null || die "PHP wurde nicht gefunden."
PHPVER=$(php -r 'echo PHP_MAJOR_VERSION.".".PHP_MINOR_VERSION;')
php -r 'exit(version_compare(PHP_VERSION,"8.1.0",">=")?0:1);' || die "PHP $PHPVER ist zu alt (mind. 8.1)."
ok "PHP $PHPVER"

# ----------------------------------------------------------------------------- Dateien
say "Kopiere Programmdateien nach $TARGET …"
mkdir -p "$TARGET"
rsync -a --delete --exclude 'data/' --exclude 'uploads/' "$SRC/" "$TARGET/"
mkdir -p "$TARGET/data/sessions" "$TARGET/uploads/avatars"
[[ -f "$TARGET/data/.htaccess" ]] || cp "$SRC/data/.htaccess" "$TARGET/data/.htaccess"
[[ -f "$TARGET/uploads/.htaccess" ]] || cp "$SRC/uploads/.htaccess" "$TARGET/uploads/.htaccess"
WEBUSER=www-data; [[ $AAPANEL -eq 1 ]] && WEBUSER=www
chown -R "$WEBUSER:$WEBUSER" "$TARGET"
find "$TARGET" -type d -exec chmod 755 {} \; ; find "$TARGET" -type f -exec chmod 644 {} \;
chmod 750 "$TARGET/data"
ok "Dateien bereit"

FIRST=0; [[ -f "$TARGET/data/config.php" ]] || FIRST=1

# ----------------------------------------------------------------------------- Datenbank (MariaDB)
if [[ $FIRST -eq 1 && ( "$DB" == "mariadb" || "$DB" == "mysql" ) ]]; then
  say "Lege MariaDB-Datenbank an …"
  DBPASS=$(head -c 24 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 24)
  mysql -u root <<SQL
CREATE DATABASE IF NOT EXISTS family_control CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS 'family'@'localhost' IDENTIFIED BY '$DBPASS';
ALTER USER 'family'@'localhost' IDENTIFIED BY '$DBPASS';
GRANT ALL PRIVILEGES ON family_control.* TO 'family'@'localhost';
FLUSH PRIVILEGES;
SQL
  cat > "$TARGET/data/install-preset.json" <<JSON
{"driver":"mysql","db_host":"127.0.0.1","db_port":"3306","db_name":"family_control","db_user":"family","db_pass":"$DBPASS"}
JSON
  chown "$WEBUSER:$WEBUSER" "$TARGET/data/install-preset.json"; chmod 600 "$TARGET/data/install-preset.json"
  ok "Datenbank family_control angelegt (Zugangsdaten werden im Assistenten automatisch eingetragen)"
fi

# ----------------------------------------------------------------------------- Nginx
if [[ $AAPANEL -eq 0 ]]; then
  SOCK=$(ls /run/php/php*-fpm.sock 2>/dev/null | sort -V | tail -1 || true)
  [[ -n "$SOCK" ]] || { svc enable "php$PHPVER-fpm"; sleep 1; SOCK=$(ls /run/php/php*-fpm.sock 2>/dev/null | sort -V | tail -1 || true); }
  [[ -n "$SOCK" ]] || die "PHP-FPM-Socket nicht gefunden."
  say "Richte Nginx auf Port $PORT ein …"
  cat > /etc/nginx/sites-available/ms-family-control <<NGINX
# MS Family Control – erzeugt von install.sh
server {
    listen $PORT;
$( [[ -f /proc/net/if_inet6 ]] && echo "    listen [::]:$PORT;" )
    server_name _;
    root $TARGET;
    index index.php;
    client_max_body_size 48m;
    server_tokens off;

    # Cloudflare / Reverse-Proxy auf demselben Rechner: echte Client-IP übernehmen
    set_real_ip_from 127.0.0.1;
    set_real_ip_from ::1;
    real_ip_header X-Forwarded-For;

    add_header X-Content-Type-Options nosniff always;
    add_header Referrer-Policy same-origin always;

    location ~ ^/(data|lib)(/|\$) { deny all; return 404; }
    location ~ ^/uploads/.*\.(php|phtml|phar)\$ { deny all; return 404; }
    location ~ /\.(?!well-known) { deny all; }
    location = /cron.php { deny all; return 404; }
    location = /sw.js { add_header Cache-Control "no-cache"; add_header Service-Worker-Allowed "/"; try_files \$uri =404; }
    location ~* \.webmanifest\$ { default_type application/manifest+json; }
    location ~* \.(css|js|png|jpg|jpeg|svg|webp|ico)\$ { expires 7d; access_log off; try_files \$uri =404; }
    location ^~ /downloads/ { add_header Content-Disposition "attachment"; try_files \$uri =404; }

    location / { try_files \$uri \$uri/ /index.php?\$query_string; }
    location ~ \.php\$ {
        try_files \$uri =404;
        include fastcgi_params;
        fastcgi_param SCRIPT_FILENAME \$document_root\$fastcgi_script_name;
        fastcgi_param HTTP_AUTHORIZATION \$http_authorization;
        fastcgi_pass unix:$SOCK;
        fastcgi_read_timeout 60s;
    }
}
NGINX
  ln -sf /etc/nginx/sites-available/ms-family-control /etc/nginx/sites-enabled/ms-family-control
  if [[ "$PORT" == "80" && -L /etc/nginx/sites-enabled/default ]]; then rm -f /etc/nginx/sites-enabled/default; fi
  if ! nginx -t >/dev/null 2>&1; then
    # Häufigste Ursache: Standard-Seite (Port 80 / IPv6) – deaktivieren und erneut prüfen
    rm -f /etc/nginx/sites-enabled/default
    nginx -t >/dev/null 2>&1 || { nginx -t; die "Nginx-Konfiguration fehlerhaft."; }
    warn "Nginx-Standardseite wurde deaktiviert."
  fi
  svc enable "$(basename "$SOCK" .sock)"
  svc enable nginx
  svc reload nginx
  ok "Nginx läuft auf Port $PORT"

  if command -v ufw >/dev/null && ufw status 2>/dev/null | grep -q "Status: active"; then
    ufw allow "$PORT/tcp" >/dev/null && ok "Firewall (ufw): Port $PORT freigegeben"
  fi
fi

# ----------------------------------------------------------------------------- Cron
PHPBIN=$(command -v php)
[[ $AAPANEL -eq 1 && -x /www/server/php/85/bin/php ]] && PHPBIN=/www/server/php/85/bin/php
echo "* * * * * $WEBUSER $PHPBIN $TARGET/cron.php >/dev/null 2>&1" > /etc/cron.d/ms-family-control
chmod 644 /etc/cron.d/ms-family-control
svc enable cron
ok "Cronjob eingerichtet (Offline-Warnungen, Aufräumen)"

# ----------------------------------------------------------------------------- Abschluss
IP=$(hostname -I 2>/dev/null | awk '{print $1}'); IP=${IP:-<server-ip>}
if [[ $FIRST -eq 1 ]]; then
  TOKEN=$(head -c 6 /dev/urandom | od -An -tx1 | tr -d ' \n' | tr 'a-f' 'A-F' | sed -E 's/(.{4})(.{4})(.{4})/\1-\2-\3/')
  echo "$TOKEN" > "$TARGET/data/setup-token.txt"; chown "$WEBUSER:$WEBUSER" "$TARGET/data/setup-token.txt"; chmod 600 "$TARGET/data/setup-token.txt"
fi
echo
echo -e "${G}═══════════════════════════════════════════════════════════════${N}"
echo -e "${G}  Fertig!${N}"
if [[ $AAPANEL -eq 1 ]]; then
  echo -e "  aaPanel: Website mit Root ${C}$TARGET${N} anlegen (PHP ≥ 8.1),"
  echo -e "  Zugriff auf /data und /lib sperren (siehe README) und dann /install.php öffnen."
else
  echo -e "  Webinterface:     ${C}http://$IP:$PORT/${N}"
fi
if [[ $FIRST -eq 1 ]]; then
  echo -e "  Einrichtung:      ${C}http://$IP:$PORT/install.php${N}"
  echo -e "  Einrichtungscode: ${P}$TOKEN${N}"
else
  echo -e "  Update eingespielt – Daten und Einstellungen sind erhalten geblieben."
fi
echo -e "  Windows-App:      im Control Center unter „Geräte & Download“"
echo -e "  iPhone-Push:      benötigt HTTPS – z. B. Cloudflare-Tunnel auf http://localhost:$PORT"
echo -e "${G}═══════════════════════════════════════════════════════════════${N}"
