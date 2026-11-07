#!/usr/bin/env bash
# Lokaler APK-Build ohne Gradle (aapt + javac + dx + apksigner).
# Benötigt: android.jar (API 34), aapt, dalvik-exchange (dx), zipalign, apksigner, JDK.
# Für den normalen Weg: GitHub Actions (siehe README).
set -euo pipefail
cd "$(dirname "$0")"
ANDROID_JAR="${ANDROID_JAR:-/tmp/claude-0/sdk/android-34.jar}"
V=$(grep '^name=' version.properties | cut -d= -f2); C=$(grep '^code=' version.properties | cut -d= -f2)
M=app/src/main; O=out; rm -rf $O; mkdir -p $O/gen $O/obj
sed -e "s#<manifest #<manifest package=\"de.mstrassenit.familycontrol\" android:versionCode=\"$C\" android:versionName=\"$V\" #" \
    -e "s#<uses-permission android:name=\"android.permission.INTERNET\" />#<uses-sdk android:minSdkVersion=\"33\" android:targetSdkVersion=\"34\" />\n    <uses-permission android:name=\"android.permission.INTERNET\" />#" \
    -e 's/ tools:ignore="[^"]*"//' -e 's/ xmlns:tools="[^"]*"//' $M/AndroidManifest.xml > $O/AndroidManifest.xml
aapt package -f -m -J $O/gen -M $O/AndroidManifest.xml -S $M/res -A $M/assets -I "$ANDROID_JAR"
JAVA_TOOL_OPTIONS= javac -nowarn -Xlint:-options -encoding UTF-8 -source 8 -target 8 -bootclasspath "$ANDROID_JAR" -d $O/obj $(find $O/gen $M/java -name '*.java')
JAVA_TOOL_OPTIONS= dalvik-exchange --dex --min-sdk-version=26 --output=$O/classes.dex $O/obj
aapt package -f -M $O/AndroidManifest.xml -S $M/res -A $M/assets -I "$ANDROID_JAR" -F $O/unsigned.apk
(cd $O && aapt add unsigned.apk classes.dex >/dev/null)
zipalign -f -p 4 $O/unsigned.apk $O/aligned.apk
JAVA_TOOL_OPTIONS= apksigner sign --ks "${MSFC_KEYSTORE:-keys/release.jks}" --ks-pass "pass:${MSFC_KEYSTORE_PASSWORD:-android}" --key-pass "pass:${MSFC_KEY_PASSWORD:-android}" --ks-key-alias "${MSFC_KEY_ALIAS:-msfc}" --out $O/MSFamilyControl.apk $O/aligned.apk
echo "OK: $O/MSFamilyControl.apk ($V)"
