#!/usr/bin/env bash
# Baut MSFamilyControl-Setup.exe (.NET Framework 4.8, läuft auf Windows 10/11 ohne Zusatzinstallation)
# Benötigt: mono-devel (mcs). Unter Windows alternativ: build.cmd (csc.exe aus .NET Framework).
set -euo pipefail
cd "$(dirname "$0")"
bash ../extensions/build.sh >/dev/null
EXTID=$(cat ../extensions/keys/extension-id.txt)
mkdir -p obj out
cp ../extensions/dist/browser-extension-chrome-edge.zip res/ext-chromium.zip
cp ../extensions/dist/browser-extension-firefox.zip res/ext-firefox.zip
rm -rf obj/src && cp -r src obj/src
sed -i "s/EXTENSION_ID_PLACEHOLDER/$EXTID/" obj/src/Common.cs
R=""
R="-resource:res/app-tray.ico,app.ico"; for f in icon-128.png awake-robot.png sleeping-robot.png lock-scenery.jpg bed-scenery.jpg app-blocked-template.jpg game-blocked-template.jpg message-template.jpg army-app.jpg army-game.jpg army-access.jpg army-web.jpg ext-chromium.zip ext-firefox.zip; do R="$R -resource:res/$f,$f"; done
mcs -nologo -target:winexe -platform:anycpu -optimize+ -sdk:4.5 -langversion:7 \
  -out:out/MSFamilyControl-Setup.exe -win32icon:res/app.ico $R \
  -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.ServiceProcess.dll \
  -r:System.Web.Extensions.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -r:Microsoft.CSharp.dll \
  obj/src/*.cs
ls -la out
