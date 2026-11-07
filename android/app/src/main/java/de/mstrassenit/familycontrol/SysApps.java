package de.mstrassenit.familycontrol;

import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.net.Uri;
import android.provider.Telephony;
import android.telecom.TelecomManager;
import android.view.inputmethod.InputMethodInfo;
import android.view.inputmethod.InputMethodManager;

import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/** Welche Apps sind „System“ (nie als neue App melden), immer erlaubt, Browser, Telefon? */
public final class SysApps {
    private SysApps() {}

    static final Set<String> STATIC_SYS = new HashSet<>(Arrays.asList(
            "android", "com.android.systemui", "com.android.settings", "com.android.permissioncontroller", "com.google.android.permissioncontroller",
            "com.android.packageinstaller", "com.google.android.packageinstaller", "com.android.vpndialogs", "com.android.intentresolver",
            "com.android.documentsui", "com.google.android.documentsui", "com.android.providers.media", "com.google.android.providers.media.module",
            "com.android.phone", "com.android.server.telecom", "com.android.emergency", "com.google.android.apps.safetyhub", "com.android.stk",
            "com.samsung.android.incallui", "com.android.incallui", "com.android.camera", "com.android.camera2", "com.sec.android.app.camera",
            "com.google.android.GoogleCamera", "com.android.deskclock", "com.google.android.deskclock", "com.sec.android.app.clockpackage",
            "com.android.calculator2", "com.google.android.calculator", "com.sec.android.app.popupcalculator", "com.samsung.android.lool",
            "com.samsung.android.app.contacts", "com.google.android.contacts", "com.android.contacts", "com.android.dialer",
            "com.google.android.dialer", "com.samsung.android.dialer", "com.google.android.apps.messaging", "com.samsung.android.messaging",
            "com.android.mms", "com.google.android.gms", "com.google.android.setupwizard", "com.sec.android.app.launcher",
            "com.samsung.android.app.galaxyfinder", "com.samsung.android.honeyboard", "com.google.android.inputmethod.latin",
            "com.miui.home", "com.miui.securitycenter", "com.huawei.android.launcher", "com.oneplus.launcher"));

    static final Set<String> LOCK_ALLOWED_STATIC = new HashSet<>(Arrays.asList(
            "com.android.phone", "com.android.server.telecom", "com.android.emergency", "com.google.android.apps.safetyhub",
            "com.samsung.android.incallui", "com.android.incallui", "com.android.dialer", "com.google.android.dialer", "com.samsung.android.dialer",
            "com.google.android.contacts", "com.samsung.android.app.contacts", "com.android.contacts", "com.android.stk",
            // Wecker muss sich auch vor „Erlaubt ab“ ausschalten lassen
            "com.android.deskclock", "com.google.android.deskclock", "com.sec.android.app.clockpackage", "com.oneplus.deskclock", "com.android.alarmclock"));

    private static Set<String> dyn;
    private static Set<String> lockAllowed;
    private static Set<String> browsers;
    private static Set<String> keyboards = new HashSet<>();
    private static long builtAt;
    private static final Map<String, String> labels = new HashMap<>();

    static synchronized void build(Context c) {
        if (dyn != null && System.currentTimeMillis() - builtAt < 10 * 60_000) return;
        builtAt = System.currentTimeMillis();
        PackageManager pm = c.getPackageManager();
        Set<String> d = new HashSet<>(STATIC_SYS);
        d.add(c.getPackageName());
        Set<String> la = new HashSet<>(LOCK_ALLOWED_STATIC);
        la.add(c.getPackageName());
        try { // Startbildschirme
            for (ResolveInfo r : pm.queryIntentActivities(new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME), 0)) d.add(r.activityInfo.packageName);
        } catch (Exception ignored) {}
        try { // Tastaturen
            InputMethodManager imm = (InputMethodManager) c.getSystemService(Context.INPUT_METHOD_SERVICE);
            Set<String> kb = new HashSet<>();
            for (InputMethodInfo i : imm.getInputMethodList()) { d.add(i.getPackageName()); kb.add(i.getPackageName()); }
            keyboards = kb;
        } catch (Exception ignored) {}
        try {
            TelecomManager tm = (TelecomManager) c.getSystemService(Context.TELECOM_SERVICE);
            String dialer = tm.getDefaultDialerPackage();
            if (dialer != null) { d.add(dialer); la.add(dialer); }
        } catch (Exception ignored) {}
        try {
            String sms = Telephony.Sms.getDefaultSmsPackage(c);
            if (sms != null) { d.add(sms); la.add(sms); }
        } catch (Exception ignored) {}
        Set<String> br = new HashSet<>();
        try {
            for (ResolveInfo r : pm.queryIntentActivities(new Intent(Intent.ACTION_VIEW, Uri.parse("http://example.com")), PackageManager.MATCH_ALL)) br.add(r.activityInfo.packageName);
        } catch (Exception ignored) {}
        br.addAll(Arrays.asList("com.android.chrome", "org.mozilla.firefox", "com.microsoft.emmx", "com.sec.android.app.sbrowser", "com.brave.browser", "com.opera.browser",
                "com.duckduckgo.mobile.android", "com.google.android.googlequicksearchbox"));
        dyn = d;
        lockAllowed = la;
        browsers = br;
    }

    /** Infrastruktur-App (Startbildschirm, Tastatur, Telefon …) */
    public static boolean isSystem(Context c, String pkg) {
        if (pkg == null || pkg.length() == 0) return true;
        build(c);
        return dyn.contains(pkg);
    }

    public static boolean isKeyboard(Context c, String pkg) {
        build(c);
        return pkg != null && keyboards.contains(pkg);
    }

    /** Darf während einer Sperre benutzt werden (Telefon, SMS, Notruf) */
    public static boolean lockAllowed(Context c, String pkg) {
        build(c);
        return pkg != null && lockAllowed.contains(pkg);
    }

    public static boolean isBrowser(Context c, String pkg) {
        build(c);
        return pkg != null && browsers.contains(pkg);
    }

    public static synchronized String label(Context c, String pkg) {
        if (pkg == null || pkg.length() == 0) return "";
        String l = labels.get(pkg);
        if (l != null) return l;
        try {
            ApplicationInfo ai = c.getPackageManager().getApplicationInfo(pkg, 0);
            l = c.getPackageManager().getApplicationLabel(ai).toString();
        } catch (Exception e) {
            l = pkg;
        }
        labels.put(pkg, l);
        return l;
    }

    public static void openDialer(Context c) {
        try {
            c.startActivity(new Intent(Intent.ACTION_DIAL).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        } catch (Exception e) {
            try { c.startActivity(new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_APP_MESSAGING).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)); } catch (Exception ignored) {}
        }
    }

    public static void goHome(Context c) {
        if (GuardAccess.home()) return;
        try { c.startActivity(new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)); } catch (Exception ignored) {}
    }

    public static ComponentName accessComponent(Context c) { return new ComponentName(c, GuardAccess.class); }

    /** installierte, startbare Apps (für die Statusanzeige) */
    public static List<ResolveInfo> launchable(Context c) {
        return c.getPackageManager().queryIntentActivities(new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER), 0);
    }
}
