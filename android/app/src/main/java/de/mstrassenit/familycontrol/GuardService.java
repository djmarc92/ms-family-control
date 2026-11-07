package de.mstrassenit.familycontrol;

import android.app.AppOpsManager;
import android.app.KeyguardManager;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.app.admin.DevicePolicyManager;
import android.app.usage.UsageEvents;
import android.app.usage.UsageStatsManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.BatteryManager;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.IBinder;
import android.os.Looper;
import android.os.PowerManager;
import android.os.Process;
import android.os.SystemClock;
import android.provider.Settings;

import org.json.JSONObject;

import java.util.HashMap;
import java.util.Map;
import java.util.Set;

/**
 * Läuft dauerhaft im Vordergrund: zählt Bildschirmzeit, sperrt, zeigt Nachrichten, synchronisiert.
 */
public class GuardService extends Service {
    static volatile GuardService inst;
    static final String CH_STATUS = "status", CH_ALERT = "alerts";
    static final int NOTIF_ID = 1;

    final Handler main = new Handler(Looper.getMainLooper());
    HandlerThread worker;
    Handler work;
    Engine eng;
    Cfg cfg;

    long lastSyncAt, lastProtAt, lastNotifAt;
    volatile boolean syncing;
    String lastNotifText = "";
    // Vordergrund-App per Nutzungsstatistik
    long lastUsageQuery;
    String usagePkg = "";
    final Map<String, Long> eventThrottle = new HashMap<>();
    JSONObject lastProt = new JSONObject();

    public static void ensure(Context c) {
        if (!Cfg.get(c).active()) return;
        if (inst != null) return;
        try { c.startForegroundService(new Intent(c, GuardService.class)); } catch (Exception ignored) {}
    }

    /** Sofort synchronisieren (z. B. nach einer Anfrage) */
    public static void kick(Context c) {
        GuardService s = inst;
        if (s != null) s.lastSyncAt = 0;
        else ensure(c);
    }

    @Override
    public void onCreate() {
        super.onCreate();
        inst = this;
        cfg = Cfg.get(this);
        eng = Engine.get(this);
        Overlays.init(this);
        createChannels();
        if (android.os.Build.VERSION.SDK_INT >= 34) startForeground(NOTIF_ID, buildNotif("Kinderschutz aktiv", ""), ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        else startForeground(NOTIF_ID, buildNotif("Kinderschutz aktiv", ""));
        worker = new HandlerThread("msfc-sync");
        worker.start();
        work = new Handler(worker.getLooper());
        eng.addEvent("agent_start", "Familien-App am Handy gestartet", android.os.Build.MANUFACTURER + " " + android.os.Build.MODEL);
        main.post(tick);
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) { return START_STICKY; }

    @Override
    public IBinder onBind(Intent intent) { return null; }

    @Override
    public void onDestroy() {
        main.removeCallbacks(tick);
        if (worker != null) worker.quitSafely();
        Overlays.hideAll();
        eng.save();
        inst = null;
        super.onDestroy();
        // Neustart, solange der Schutz eingerichtet ist
        if (Cfg.get(this).active()) {
            try { startForegroundService(new Intent(this, GuardService.class)); } catch (Exception ignored) {}
        }
    }

    // =====================================================================
    final Runnable tick = new Runnable() {
        public void run() {
            try { step(); } catch (Throwable ignored) {}
            main.postDelayed(this, 1000);
        }
    };

    void step() {
        if (!cfg.active()) { Overlays.hideAll(); stopForeground(STOP_FOREGROUND_REMOVE); stopSelf(); return; }
        PowerManager pm = (PowerManager) getSystemService(POWER_SERVICE);
        KeyguardManager km = (KeyguardManager) getSystemService(KEYGUARD_SERVICE);
        boolean interactive = pm.isInteractive();
        boolean keyguard = km.isKeyguardLocked();
        boolean usable = interactive && !keyguard;

        String pkg = usable ? foreground() : "";
        String label = SysApps.label(this, pkg);
        boolean sys = SysApps.isSystem(this, pkg);

        Engine.Eval ev = eng.evaluate();
        eng.noteReason(ev.reason);
        boolean locked = ev.reason.length() > 0 && !eng.revoked && eng.policy() != null;

        if (locked && usable && !SysApps.lockAllowed(this, pkg)) {
            if (Overlays.canDraw(this)) Overlays.showLock(this, ev);
            else lockScreenFallback();
        } else {
            Overlays.hideLock();
        }
        boolean counting = usable && !locked && !Overlays.popupShown();
        eng.tick(pkg, label, sys, counting);

        if (usable && !locked && pkg.length() > 0) checkApp(pkg, label, sys);

        int w = eng.warnDue(ev);
        if (w > 0) {
            String t = ev.lockKind.equals("time") ? "⏰ Noch " + w + (w == 1 ? " Minute" : " Minuten") + " Bildschirmzeit" : "🌙 In " + w + (w == 1 ? " Minute" : " Minuten") + " ist Bettzeit";
            Overlays.showBanner(this, t);
            alert(t, "Danach wird das Handy gesperrt.", 10 + w);
        }

        handleNotices();

        long now = SystemClock.elapsedRealtime();
        int every = interactive ? eng.syncInterval() : 60;
        if (!syncing && now - lastSyncAt > every * 1000L) {
            lastSyncAt = now;
            syncing = true;
            work.post(new Runnable() {
                public void run() {
                    try { eng.sync(false); } finally { syncing = false; }
                    main.post(new Runnable() { public void run() { afterSync(); } });
                }
            });
        }
        if (now - lastProtAt > 20000) { lastProtAt = now; checkProtection(); }
        if (now - lastNotifAt > 30000 || w > 0) { lastNotifAt = now; updateNotif(ev); }
    }

    void afterSync() {
        if (eng.webFilterWanted() && !FilterVpn.running) FilterVpn.start(this);
        if (!eng.webFilterWanted() && FilterVpn.running) FilterVpn.stop(this);
        if (eng.revoked) Overlays.hideAll();
    }

    /** Ohne „Über anderen Apps“-Recht: Bildschirm per Geräteadministrator ausschalten */
    long lastLockNow;
    void lockScreenFallback() {
        if (SystemClock.elapsedRealtime() - lastLockNow < 8000) return;
        lastLockNow = SystemClock.elapsedRealtime();
        try {
            DevicePolicyManager dpm = (DevicePolicyManager) getSystemService(DEVICE_POLICY_SERVICE);
            if (dpm.isAdminActive(new ComponentName(this, AdminReceiver.class))) dpm.lockNow();
        } catch (Exception ignored) {}
    }

    // =====================================================================
    // Vordergrund-App
    // =====================================================================
    String foreground() {
        long now = System.currentTimeMillis();
        try {
            UsageStatsManager usm = (UsageStatsManager) getSystemService(USAGE_STATS_SERVICE);
            long from = lastUsageQuery == 0 ? now - 6 * 3600_000L : lastUsageQuery - 2000;
            UsageEvents ue = usm.queryEvents(from, now);
            UsageEvents.Event e = new UsageEvents.Event();
            while (ue.hasNextEvent()) {
                ue.getNextEvent(e);
                if (e.getEventType() == UsageEvents.Event.ACTIVITY_RESUMED) usagePkg = e.getPackageName();
            }
            lastUsageQuery = now;
        } catch (Exception ignored) {}
        String acc = GuardAccess.lastPkg;
        // Bedienungshilfe ist schneller – wenn sie gerade etwas Neues gemeldet hat, gilt das
        if (acc != null && acc.length() > 0 && now - GuardAccess.lastAt < 3000 && !acc.equals(usagePkg)) return acc;
        return usagePkg == null ? "" : usagePkg;
    }

    // =====================================================================
    // Gesperrte Apps / strenger Modus
    // =====================================================================
    void checkApp(String pkg, String label, boolean sys) {
        if (pkg.equals(getPackageName())) return;
        Map<String, String[]> blocked = eng.blockedApps();
        String[] b = blocked.get(pkg.toLowerCase());
        if (b != null) {
            SysApps.goHome(this);
            Overlays.showBlocked(this, b[1], b[0].length() > 0 ? b[0] : label, pkg);
            if (throttle("ba:" + pkg, 60000)) eng.addEvent("blocked_app", "Gesperrte App geöffnet: " + (b[0].length() > 0 ? b[0] : label), pkg);
            return;
        }
        if (eng.strict() && !sys) {
            Set<String> known = eng.knownApps();
            if (!known.contains(pkg.toLowerCase())) {
                SysApps.goHome(this);
                Overlays.showBlocked(this, "unknown", label, pkg);
                if (throttle("un:" + pkg, 10 * 60000)) eng.addEvent("unknown_app", label, pkg);
                lastSyncAt = 0;
            }
        }
    }

    boolean throttle(String key, long ms) {
        Long t = eventThrottle.get(key);
        long now = SystemClock.elapsedRealtime();
        if (t != null && now - t < ms) return false;
        eventThrottle.put(key, now);
        return true;
    }

    /** Vom Webfilter (anderer Thread): Seite wurde blockiert */
    static void onSiteBlocked(final Context c, final String domain, final String cat) {
        final GuardService s = inst;
        if (s == null) return;
        s.main.post(new Runnable() {
            public void run() {
                if (s.throttle("site:" + domain, 60000)) s.eng.addEvent("blocked_site", "Gesperrte Webseite: " + domain, cat == null ? "Eltern-Sperrliste" : s.eng.filters.name(cat));
                String fg = s.eng.fgPkg;
                if (SysApps.isBrowser(c, fg) && s.throttle("sitepop:" + domain, 15000)) Overlays.showWeb(c, domain, cat);
            }
        });
    }

    // =====================================================================
    // Hinweise vom Server (Nachrichten, Antworten auf Anfragen)
    // =====================================================================
    void handleNotices() {
        JSONObject n;
        while ((n = eng.pollNotice()) != null) {
            String t = n.optString("type");
            if (t.equals("message")) {
                Overlays.showMessage(this, n);
                alert("💬 Nachricht von " + n.optString("from", "Eltern"), n.optString("text"), 50);
            } else if (t.equals("request")) {
                boolean ok = n.optString("status").equals("approved");
                String what = n.optString("kind").equals("time") ? "Mehr Zeit" : n.optString("label", n.optString("domain"));
                String txt = ok ? "✅ " + what + " genehmigt" + (n.optInt("granted") > 0 ? " (+" + n.optInt("granted") + " min)" : "") : "❌ " + what + " abgelehnt";
                Overlays.showBanner(this, txt);
                alert(txt, ok ? "Viel Spaß!" : "Frag Mama oder Papa, warum.", 60);
                if (ok) Overlays.closePopup();
            } else if (t.equals("screen_off")) {
                alert("🔒 Deine Eltern haben das Handy gesperrt", "", 70);
                try {
                    DevicePolicyManager dpm = (DevicePolicyManager) getSystemService(DEVICE_POLICY_SERVICE);
                    if (dpm.isAdminActive(new ComponentName(this, AdminReceiver.class))) dpm.lockNow();
                } catch (Exception ignored) {}
            }
        }
    }

    // =====================================================================
    // Schutz-Zustand (Berechtigungen) – fehlt etwas, erfahren es die Eltern
    // =====================================================================
    static JSONObject protection(Context c) {
        JSONObject p = new JSONObject();
        try {
            AppOpsManager ao = (AppOpsManager) c.getSystemService(APP_OPS_SERVICE);
            p.put("usage", ao.unsafeCheckOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), c.getPackageName()) == AppOpsManager.MODE_ALLOWED);
            String en = Settings.Secure.getString(c.getContentResolver(), Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES);
            p.put("access", en != null && en.toLowerCase().contains(c.getPackageName().toLowerCase() + "/"));
            p.put("overlay", Settings.canDrawOverlays(c));
            DevicePolicyManager dpm = (DevicePolicyManager) c.getSystemService(DEVICE_POLICY_SERVICE);
            p.put("admin", dpm.isAdminActive(new ComponentName(c, AdminReceiver.class)));
            p.put("vpn", !Engine.get(c).webFilterWanted() || FilterVpn.running);
            PowerManager pm = (PowerManager) c.getSystemService(POWER_SERVICE);
            p.put("battery", pm.isIgnoringBatteryOptimizations(c.getPackageName()));
        } catch (Exception ignored) {}
        return p;
    }

    static final Map<String, String> PROT_NAMES = new HashMap<>();
    static {
        PROT_NAMES.put("usage", "Nutzungszugriff");
        PROT_NAMES.put("access", "Bedienungshilfe");
        PROT_NAMES.put("overlay", "Über anderen Apps anzeigen");
        PROT_NAMES.put("admin", "Geräteadministrator");
        PROT_NAMES.put("vpn", "Webfilter");
        PROT_NAMES.put("battery", "Akku-Ausnahme");
    }

    void checkProtection() {
        JSONObject p = protection(this);
        java.util.Iterator<String> it = p.keys();
        while (it.hasNext()) {
            String k = it.next();
            if (lastProt.optBoolean(k, false) && !p.optBoolean(k, true))
                eng.addEvent("tamper", "Schutz am Handy geschwächt: " + PROT_NAMES.get(k) + " ausgeschaltet", "");
        }
        lastProt = p;
        eng.prot = p;
        try {
            BatteryManager bm = (BatteryManager) getSystemService(BATTERY_SERVICE);
            eng.battery = bm.getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY);
        } catch (Exception ignored) {}
        if (eng.webFilterWanted() && !FilterVpn.running) FilterVpn.start(this);
    }

    // =====================================================================
    // Benachrichtigungen
    // =====================================================================
    void createChannels() {
        NotificationManager nm = getSystemService(NotificationManager.class);
        NotificationChannel s = new NotificationChannel(CH_STATUS, "Status", NotificationManager.IMPORTANCE_LOW);
        s.setShowBadge(false);
        nm.createNotificationChannel(s);
        NotificationChannel a = new NotificationChannel(CH_ALERT, "Hinweise & Nachrichten", NotificationManager.IMPORTANCE_HIGH);
        nm.createNotificationChannel(a);
    }

    Notification buildNotif(String title, String text) {
        PendingIntent pi = PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class), PendingIntent.FLAG_IMMUTABLE);
        return new Notification.Builder(this, CH_STATUS)
                .setSmallIcon(R.drawable.ic_stat)
                .setContentTitle(title)
                .setContentText(text)
                .setOngoing(true)
                .setShowWhen(false)
                .setContentIntent(pi)
                .build();
    }

    void updateNotif(Engine.Eval ev) {
        String title, text;
        if (eng.revoked) { title = "Gerät wurde entfernt"; text = "Bitte im Control Center neu koppeln"; }
        else if (ev.free) { title = "Freigegeben bis " + ev.until + " Uhr"; text = "Von Mama/Papa freigeschaltet"; }
        else if (ev.reason.length() > 0) { title = "🔒 Gesperrt"; text = ev.until.length() > 0 ? "Wieder ab " + ev.until + " Uhr" : "Frag Mama oder Papa"; }
        else if (ev.unlimited) { title = "Heute ohne Zeitlimit"; text = ev.end.equals("24:00") ? "" : "Bettzeit ab " + ev.end + " Uhr"; }
        else { title = "Noch " + Ui.dur(ev.remaining) + " Bildschirmzeit"; text = ev.end.equals("24:00") ? "" : "Bettzeit ab " + ev.end + " Uhr"; }
        String key = title + text;
        if (key.equals(lastNotifText)) return;
        lastNotifText = key;
        getSystemService(NotificationManager.class).notify(NOTIF_ID, buildNotif(title, text));
    }

    void alert(String title, String text, int id) {
        try {
            PendingIntent pi = PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class), PendingIntent.FLAG_IMMUTABLE);
            Notification n = new Notification.Builder(this, CH_ALERT).setSmallIcon(R.drawable.ic_stat).setContentTitle(title).setContentText(text)
                    .setStyle(new Notification.BigTextStyle().bigText(text)).setAutoCancel(true).setContentIntent(pi).build();
            getSystemService(NotificationManager.class).notify(100 + id, n);
        } catch (Exception ignored) {}
    }
}
