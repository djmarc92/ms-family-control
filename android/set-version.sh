#!/usr/bin/env bash
# Neue Version setzen: ./set-version.sh 1.0.1
set -e
cd "$(dirname "$0")"
V="$1"; [ -n "$V" ] || { echo "Aufruf: ./set-version.sh 1.0.1"; exit 1; }
C=$(( $(grep '^code=' version.properties | cut -d= -f2) + 1 ))
printf "code=%s\nname=%s\n" "$C" "$V" > version.properties
sed -i "s/VERSION = \".*\"/VERSION = \"$V\"/" app/src/main/java/de/mstrassenit/familycontrol/BuildInfo.java
echo "Version $V (Code $C) gesetzt."
