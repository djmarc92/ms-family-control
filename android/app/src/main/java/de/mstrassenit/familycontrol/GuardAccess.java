package de.mstrassenit.familycontrol;

import android.accessibilityservice.AccessibilityService;
import android.os.SystemClock;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;

import java.util.ArrayDeque;
import java.util.Arrays;
import java.util.HashSet;
import java.util.Locale;
import java.util.Set;

/**
 * Bedienungshilfe: meldet sofort, welche App geöffnet wird, und schützt die Einstellungen,
 * mit denen man den Kinderschutz abschalten könnte (App-Info, Deinstallieren, Geräteadmin, VPN, privates DNS …).
 */
public class GuardAccess extends AccessibilityService {
    static volatile GuardAccess inst;
    static volatile String lastPkg = "";
    static volatile long lastAt;
    long lastScan, lastTamperEvent;

    static final Set<String> IGNORE = new HashSet<>(Arrays.asList("com.android.systemui", "android"));
    static final Set<String> PROTECTED = new HashSet<>(Arrays.asList(
            "com.android.settings", "com.google.android.packageinstaller", "com.android.packageinstaller",
            "com.google.android.permissioncontroller", "com.android.permissioncontroller", "com.android.vpndialogs",
            "com.samsung.android.lool", "com.samsung.android.sm.devicesecurity", "com.miui.securitycenter", "com.miui.securitymanager",
            "com.coloros.safecenter", "com.huawei.systemmanager", "com.samsung.accessibility", "com.google.android.marvin.talkback"));
    // Begriffe, die zusammen mit dem App-Namen auf eine „gefährliche“ Seite hinweisen
    static final String[] DANGER = {"deinstall", "uninstall", "stopp erzwingen", "beenden erzwingen", "force stop", "deaktivieren", "deactivate",
            "verwenden", " use ", "trennen", "disconnect", "daten löschen", "speicher leeren", "clear data", "clear storage", "erlauben", "allow",
            "berechtigung", "permission", "ausschalten", "turn off", "entfernen", "remove", "einschränk", "restrict", "akku", "battery"};

    @Override
    protected void onServiceConnected() {
        inst = this;
        GuardService.ensure(this);
    }

    @Override
    public void onAccessibilityEvent(AccessibilityEvent e) {
        CharSequence p = e.getPackageName();
        if (p == null) return;
        String pkg = p.toString();
        if (e.getEventType() == AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED && !IGNORE.contains(pkg) && !pkg.equals(getPackageName())
                && !SysApps.isKeyboard(this, pkg)) {
            lastPkg = pkg;
            lastAt = System.currentTimeMillis();
        }
        if (PROTECTED.contains(pkg)) {
            long now = SystemClock.elapsedRealtime();
            if (now - lastScan < 350) return;
            lastScan = now;
            Cfg cfg = Cfg.get(this);
            if (!cfg.active() || Engine.get(this).settingsUnlocked()) return;
            String what = scan(getRootInActiveWindow());
            if (what != null) block(what);
        }
        if (inst == null) inst = this;
        GuardService.ensure(this);
    }

    /** Liefert eine Beschreibung, wenn die Seite den Schutz betrifft */
    String scan(AccessibilityNodeInfo root) {
        if (root == null) return null;
        StringBuilder all = new StringBuilder();
        ArrayDeque<AccessibilityNodeInfo> q = new ArrayDeque<>();
        q.add(root);
        int n = 0;
        while (!q.isEmpty() && n < 700) {
            AccessibilityNodeInfo x = q.poll();
            n++;
            if (x == null) continue;
            CharSequence t = x.getText(), d = x.getContentDescription();
            if (t != null) all.append(t).append(' ');
            if (d != null) all.append(d).append(' ');
            for (int i = 0; i < x.getChildCount(); i++) q.add(x.getChild(i));
        }
        String s = " " + all.toString().toLowerCase(Locale.GERMANY) + " ";
        if ((s.contains("privates dns") || s.contains("private dns")) && (s.contains("hostname") || s.contains("anbieter"))) return "Privates DNS";
        boolean ours = s.contains("ms family control") || s.contains("family control");
        if (!ours) return null;
        for (String k : DANGER) if (s.contains(k)) return "Familien-App";
        return null;
    }

    void block(String what) {
        performGlobalAction(GLOBAL_ACTION_BACK);
        performGlobalAction(GLOBAL_ACTION_HOME);
        Overlays.showProtect(this, what);
        long now = SystemClock.elapsedRealtime();
        if (now - lastTamperEvent > 5 * 60_000) {
            lastTamperEvent = now;
            Engine.get(this).addEvent("tamper", "Versuch, den Kinderschutz am Handy zu ändern (" + what + ")", "");
            GuardService.kick(this);
        }
    }

    static boolean home() {
        GuardAccess a = inst;
        if (a == null) return false;
        return a.performGlobalAction(GLOBAL_ACTION_HOME);
    }

    @Override
    public void onInterrupt() {}

    @Override
    public void onDestroy() {
        inst = null;
        super.onDestroy();
    }
}
