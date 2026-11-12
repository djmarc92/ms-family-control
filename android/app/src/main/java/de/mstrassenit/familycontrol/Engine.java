package de.mstrassenit.familycontrol;

import android.content.Context;
import android.os.SystemClock;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Calendar;
import java.util.Date;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.UUID;

/**
 * Regel-Engine – gleiche Logik wie Server (policy.php) und Windows-App (Engine.cs).
 * Zählt Nutzung, wertet Regeln aus, synchronisiert mit dem Server, hält alles offline vor.
 */
public final class Engine {
    private static Engine inst;
    public static synchronized Engine get(Context c) {
        if (inst == null) inst = new Engine(c.getApplicationContext());
        return inst;
    }

    final Context ctx;
    final Cfg cfg;
    public final FilterStore filters;

    // --- Zustand (alles unter "this" synchronisiert) ---
    JSONObject policy;
    Batch current = new Batch();
    List<Batch> pending = new ArrayList<>();
    List<JSONObject> events = new ArrayList<>();
    List<JSONObject> requests = new ArrayList<>();
    final ArrayDeque<JSONObject> notices = new ArrayDeque<>();
    String serverUsedDay = "";
    int serverUsed;
    long localFreeUntil, settingsUnlockedUntil;
    String warnedDay = "";
    Set<Integer> warned = new HashSet<>();
    String lastReason = "";
    // Server-Zeit (gegen verstellte Uhr)
    long anchorServer, anchorElapsed;
    // Verbindung
    public long lastSyncOk, offlineSince;
    public String syncError = "";
    public boolean revoked;
    long lastSave;
    int masterFails;
    long masterBlockedUntil;
    // Live-Status für den Server
    public String fgPkg = "", fgLabel = "";
    public boolean fgSys;
    public JSONObject prot = new JSONObject();
    public int battery = -1;
    long activeSince;

    private Engine(Context c) {
        ctx = c;
        cfg = Cfg.get(c);
        filters = new FilterStore(c);
        load();
    }

    // ================= Zeit =================
    public synchronized long now() {
        if (anchorServer == 0) return System.currentTimeMillis();
        return anchorServer + (SystemClock.elapsedRealtime() - anchorElapsed);
    }

    static String ymd(Calendar c) { return String.format(Locale.US, "%04d-%02d-%02d", c.get(Calendar.YEAR), c.get(Calendar.MONTH) + 1, c.get(Calendar.DAY_OF_MONTH)); }

    static int hm(String t) {
        if (t == null || t.length() < 4) return 0;
        String[] p = t.split(":");
        try { return Integer.parseInt(p[0]) * 60 + (p.length > 1 ? Integer.parseInt(p[1]) : 0); } catch (Exception e) { return 0; }
    }

    static long parseDT(String s) {
        if (s == null || s.length() < 10 || s.equals("null")) return 0;
        try { return new SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US).parse(s.replace('T', ' ').substring(0, Math.min(19, s.length()))).getTime(); } catch (Exception e) { return 0; }
    }

    static String hhmm(long ms) { return new SimpleDateFormat("HH:mm", Locale.GERMANY).format(new Date(ms)); }

    // ================= Regeln =================
    public static class Eval {
        public String reason = "";      // "", manual, early, bedtime, school, time
        public String until = "";
        public int used, limitMin = 1440, bonus;
        public int remaining = -1;      // Sekunden, -1 = unbegrenzt
        public boolean unlimited = true;
        public String start = "00:00", end = "24:00", dayType = "", nightType = "";
        public int secondsToLock = Integer.MAX_VALUE;
        public String lockKind = "";
        public boolean free;            // von Eltern freigegeben
    }

    String dayType(Calendar c) {
        String mode = policy.optString("mode", "auto");
        if (mode.equals("holiday")) return "holiday";
        String d = ymd(c);
        if (mode.equals("auto")) {
            JSONArray hs = policy.optJSONArray("holidays");
            if (hs != null) for (int i = 0; i < hs.length(); i++) {
                JSONArray h = hs.optJSONArray(i);
                if (h != null && h.length() >= 2 && d.compareTo(h.optString(0)) >= 0 && d.compareTo(h.optString(1)) <= 0) return "holiday";
            }
        }
        int dow = c.get(Calendar.DAY_OF_WEEK);
        return (dow == Calendar.SATURDAY || dow == Calendar.SUNDAY) ? "weekend" : "school";
    }

    int usedToday(String day) {
        int used = serverUsedDay.equals(day) ? serverUsed : 0;
        for (Batch b : pending) if (b.day.equals(day)) used += b.total();
        if (current.day.equals(day)) used += current.total();
        return used;
    }

    public synchronized Eval evaluate() {
        Eval e = new Eval();
        if (policy == null) return e;
        JSONObject today = policy.optJSONObject("today");
        if (today == null) return e;
        long nowMs = now();
        Calendar c = Calendar.getInstance();
        c.setTimeInMillis(nowMs);
        String day = ymd(c);
        boolean same = today.optString("day").equals(day);
        String type = same ? today.optString("day_type", "school") : dayType(c);
        JSONObject rules = policy.optJSONObject("rules");
        JSONObject rule = rules != null ? rules.optJSONObject(type) : null;
        int limit = rule != null ? rule.optInt("limit_min", 1440) : today.optInt("limit_min", 1440);
        String start = rule != null ? rule.optString("start_time", "00:00") : today.optString("start", "00:00");
        // Abend richtet sich nach dem nächsten Tag (Fr-Abend = Wochenende, So-Abend = Schultag)
        String night = type;
        if (policy.optBoolean("evening_next_day", false)) {
            Calendar t = (Calendar) c.clone();
            t.add(Calendar.DAY_OF_MONTH, 1);
            night = dayType(t);
        }
        JSONObject nrule = rules != null ? rules.optJSONObject(night) : null;
        String end = nrule != null ? nrule.optString("end_time", "24:00") : today.optString("end", "24:00");
        int bonus = same ? today.optInt("bonus_min", 0) : 0;
        e.used = usedToday(day);
        e.limitMin = limit; e.bonus = bonus; e.start = start; e.end = end; e.dayType = type; e.nightType = night;
        e.unlimited = limit >= 1440;
        e.remaining = e.unlimited ? -1 : Math.max(0, (limit + bonus) * 60 - e.used);
        long freeUntil = parseDT(today.optString("free_until", ""));
        long lockUntil = parseDT(today.optString("lock_until", ""));
        int nowMin = c.get(Calendar.HOUR_OF_DAY) * 60 + c.get(Calendar.MINUTE);
        int secToEnd = end.equals("24:00") ? Integer.MAX_VALUE : (hm(end) * 60 - (nowMin * 60 + c.get(Calendar.SECOND)));

        if (localFreeUntil > nowMs) { e.free = true; e.until = hhmm(localFreeUntil); return e; }
        if (freeUntil > nowMs) { e.free = true; e.until = hhmm(freeUntil); return e; }
        if (lockUntil > nowMs) { e.reason = "manual"; e.until = hhmm(lockUntil); return e; }
        if (nowMin < hm(start)) { e.reason = "early"; e.until = start; return e; }
        if (!end.equals("24:00") && nowMin >= hm(end)) {
            e.reason = "bedtime";
            JSONObject tr = rules != null ? rules.optJSONObject(night) : null; // morgen ab …
            e.until = tr != null ? tr.optString("start_time", "") : "";
            return e;
        }
        JSONObject school = policy.optJSONObject("school");
        if (type.equals("school") && school != null && school.optBoolean("lock") && nowMin >= hm(school.optString("from", "07:00")) && nowMin < hm(school.optString("to", "15:00"))) {
            e.reason = "school"; e.until = school.optString("to", "15:00"); return e;
        }
        if (!e.unlimited && e.remaining <= 0) { e.reason = "time"; return e; }
        e.secondsToLock = Math.min(e.unlimited ? Integer.MAX_VALUE : e.remaining, secToEnd);
        e.lockKind = (!e.unlimited && e.remaining <= secToEnd) ? "time" : "bedtime";
        return e;
    }

    // ================= Nutzung zählen =================
    /** Jede Sekunde vom GuardService aufgerufen */
    public synchronized void tick(String pkg, String label, boolean sys, boolean counting) {
        fgPkg = pkg == null ? "" : pkg;
        fgLabel = label == null ? "" : label;
        fgSys = sys;
        long nowMs = now();
        Calendar c = Calendar.getInstance();
        c.setTimeInMillis(nowMs);
        String day = ymd(c);
        if (!current.day.equals(day)) rotate();
        if (counting) {
            if (activeSince == 0) activeSince = nowMs;
            current.add(c.get(Calendar.HOUR_OF_DAY), pkg, label, sys);
        } else {
            activeSince = 0;
        }
        if (SystemClock.elapsedRealtime() - lastSave > 20000) save();
    }

    void rotate() {
        if (!current.empty()) pending.add(current);
        if (pending.size() > 400) pending.subList(0, pending.size() - 400).clear();
        Calendar c = Calendar.getInstance();
        c.setTimeInMillis(now());
        current = new Batch();
        current.day = ymd(c);
    }

    // ================= Ereignisse / Anfragen =================
    public synchronized void addEvent(String type, String title, String detail) {
        try {
            JSONObject e = new JSONObject();
            e.put("type", type); e.put("title", title); e.put("detail", detail == null ? "" : detail);
            e.put("ts", new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.US).format(new Date(now())));
            events.add(e);
            if (events.size() > 300) events.subList(0, events.size() - 300).clear();
        } catch (Exception ignored) {}
    }

    /** Ein-/Ausschalt-Protokoll (Uhrzeit des Handys, gesperrte Zeit?) */
    public synchronized void power(String kind, String how) {
        try {
            Eval ev = evaluate();
            boolean locked = kind.equals("on") && ev.reason.length() > 0 && policy != null;
            String reason = locked ? ev.reason : "";
            String rt = reason.equals("bedtime") ? "der Bettzeit" : reason.equals("early") ? "der Sperrzeit (noch zu früh)" : reason.equals("school") ? "der Schulzeit"
                    : reason.equals("manual") ? "einer Sperre durch die Eltern" : reason.equals("time") ? "aufgebrauchter Bildschirmzeit" : "";
            Date d = new Date();
            JSONObject e = new JSONObject();
            e.put("type", kind.equals("off") ? "power_off" : locked ? "power_on" : "power_on_ok");
            e.put("title", "Handy " + how + (locked ? " während " + rt : ""));
            e.put("detail", "Handy-Uhr: " + new SimpleDateFormat("EEEE, dd.MM.yyyy · HH:mm:ss", Locale.GERMANY).format(d) + " Uhr · " + cfg.deviceName);
            e.put("ts", new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.US).format(new Date(now())));
            e.put("pc_time", new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.US).format(d));
            e.put("how", how); e.put("power", kind); e.put("locked", locked); e.put("reason", reason);
            events.add(e);
            save();
        } catch (Exception ignored) {}
    }

    public synchronized void addEventRef(String type, String title, int ref) {
        addEvent(type, title, "");
        try { events.get(events.size() - 1).put("ref", ref); } catch (Exception ignored) {}
    }

    /** kind: time | site | app */
    public synchronized String addRequest(String kind, int minutes, String reason, String domain, String label) {
        // gleiche offene Anfrage nicht doppelt
        for (JSONObject r : requests) {
            String st = r.optString("status");
            if ((st.equals("queued") || st.equals("pending")) && r.optString("kind").equals(kind) && r.optString("domain").equals(domain == null ? "" : domain)) return "Anfrage läuft schon – deine Eltern wurden benachrichtigt.";
        }
        try {
            JSONObject r = new JSONObject();
            r.put("local_id", UUID.randomUUID().toString());
            r.put("kind", kind); r.put("minutes", minutes); r.put("reason", reason == null ? "" : reason);
            r.put("domain", domain == null ? "" : domain); r.put("label", label == null ? "" : label);
            r.put("status", "queued"); r.put("created", System.currentTimeMillis());
            requests.add(r);
        } catch (Exception ignored) {}
        save();
        return "";
    }

    public synchronized JSONObject openRequest(String kind, String domain) {
        for (int i = requests.size() - 1; i >= 0; i--) {
            JSONObject r = requests.get(i);
            if (r.optString("kind").equals(kind) && (domain == null || r.optString("domain").equals(domain))
                    && System.currentTimeMillis() - r.optLong("created") < 3 * 3600_000L) return r;
        }
        return null;
    }

    public synchronized JSONObject pollNotice() { return notices.poll(); }

    // ================= Masterpasswort =================
    /** null = ok, sonst Fehlermeldung */
    public synchronized String checkMaster(String pw, String where) {
        if (masterBlockedUntil > System.currentTimeMillis())
            return "Zu viele Fehlversuche. Bitte " + (int) Math.ceil((masterBlockedUntil - System.currentTimeMillis()) / 60000.0) + " Minuten warten.";
        if (!cfg.checkMaster(pw)) {
            masterFails++;
            if (masterFails >= 5) { masterBlockedUntil = System.currentTimeMillis() + 5 * 60000; masterFails = 0; }
            if (masterFails == 3 || masterBlockedUntil > System.currentTimeMillis()) addEvent("master_fail", "Falsches Masterpasswort am Handy eingegeben", where);
            return "Masterpasswort falsch";
        }
        masterFails = 0;
        return null;
    }

    public synchronized void unlockFor(int minutes) {
        localFreeUntil = now() + minutes * 60_000L;
        addEvent("parent_unlock", "Am Handy mit Masterpasswort freigegeben (" + minutes + " min)", "bis " + hhmm(localFreeUntil));
        save();
    }

    public synchronized boolean settingsUnlocked() { return settingsUnlockedUntil > System.currentTimeMillis(); }

    public synchronized void unlockSettings(int minutes) {
        settingsUnlockedUntil = System.currentTimeMillis() + minutes * 60_000L;
        addEvent("parent_unlock", "Einstellungen am Handy " + minutes + " min freigegeben", "");
    }

    // ================= Policy-Hilfen =================
    public synchronized JSONObject policy() { return policy; }
    public synchronized String childName() { return policy != null && policy.optJSONObject("child") != null ? policy.optJSONObject("child").optString("name", cfg.childName) : cfg.childName; }

    public synchronized boolean strict() { return policy != null && policy.optBoolean("strict_apps", false); }

    /** Paket → [Name, Art(app|game)] der gesperrten Apps (Freigaben sind serverseitig schon herausgerechnet) */
    public synchronized Map<String, String[]> blockedApps() {
        Map<String, String[]> m = new HashMap<>();
        if (policy == null) return m;
        JSONArray a = policy.optJSONArray("blocked_apps");
        if (a != null) for (int i = 0; i < a.length(); i++) {
            JSONObject o = a.optJSONObject(i);
            if (o != null) m.put(o.optString("exe").toLowerCase(Locale.ROOT), new String[]{o.optString("name"), o.optString("kind", "app")});
        }
        return m;
    }

    public synchronized Set<String> knownApps() {
        Set<String> s = new HashSet<>();
        if (policy == null) return s;
        addAll(s, policy.optJSONArray("known_apps"));
        addAll(s, policy.optJSONArray("app_grants"));
        return s;
    }

    static void addAll(Set<String> s, JSONArray a) {
        if (a != null) for (int i = 0; i < a.length(); i++) s.add(a.optString(i).toLowerCase(Locale.ROOT));
    }

    // ================= Webseiten (für den Filter) =================
    public static final int ALLOW = 0, BLOCK_SITE = 1, BLOCK_FILTER = 2;

    static boolean matches(String host, String d) { return d.length() > 0 && (host.equals(d) || host.endsWith("." + d)); }

    /** Ergebnis: [Art, Domain/Kategorie-Schlüssel] */
    public synchronized String[] checkHost(String host) {
        if (policy == null || host.length() < 4) return null;
        JSONArray al = policy.optJSONArray("allow_sites");
        if (al != null) for (int i = 0; i < al.length(); i++) if (matches(host, al.optString(i))) return null;
        JSONArray gr = policy.optJSONArray("site_grants");
        long n = now();
        if (gr != null) for (int i = 0; i < gr.length(); i++) {
            JSONObject g = gr.optJSONObject(i);
            if (g == null) continue;
            long until = parseDT(g.optString("until_at", g.optString("until", "")));
            if (until > n && matches(host, g.optString("domain"))) return null;
        }
        JSONArray bl = policy.optJSONArray("blocked_sites");
        if (bl != null) for (int i = 0; i < bl.length(); i++) {
            String d = bl.optString(i);
            if (matches(host, d)) return new String[]{"site", d};
        }
        return null; // Filterlisten prüft FilterVpn direkt (ohne Lock, sehr viele Einträge)
    }

    public synchronized boolean safeSearch() { return policy != null && policy.optBoolean("safesearch", false); }
    public synchronized int youtubeRestrict() { return policy == null ? 0 : policy.optInt("youtube_restrict", 0); }

    public synchronized boolean webFilterWanted() {
        if (policy == null) return false;
        JSONArray f = policy.optJSONArray("filters"), b = policy.optJSONArray("blocked_sites");
        return safeSearch() || youtubeRestrict() > 0 || (f != null && f.length() > 0) || (b != null && b.length() > 0);
    }

    // ================= Sync =================
    /** Läuft im Hintergrund-Thread */
    public void sync(boolean slow) {
        if (!cfg.paired()) return;
        int timeout = slow ? 30000 : 15000;
        // 1) offene Anfragen
        List<JSONObject> toSend = new ArrayList<>();
        synchronized (this) { for (JSONObject r : requests) if (r.optString("status").equals("queued")) toSend.add(r); }
        for (JSONObject r : toSend) {
            try {
                JSONObject b = new JSONObject();
                b.put("local_id", r.optString("local_id")); b.put("kind", r.optString("kind")); b.put("minutes", r.optInt("minutes"));
                b.put("reason", r.optString("reason")); b.put("message", ""); b.put("domain", r.optString("domain")); b.put("label", r.optString("label"));
                JSONObject res = Net.call(cfg.server, "request", b, cfg.token, timeout);
                synchronized (this) { r.put("server_id", res.optInt("id")); r.put("status", res.optString("status", "pending")); }
            } catch (Net.ApiException ex) {
                synchronized (this) { try { r.put("status", "error"); r.put("error", ex.getMessage()); } catch (Exception ignored) {} }
            } catch (Exception ex) {
                break; // offline
            }
        }
        // 2) Nutzung + Status
        List<Batch> sending;
        List<JSONObject> evs;
        JSONObject body = new JSONObject();
        synchronized (this) {
            rotate();
            sending = new ArrayList<>(pending.subList(0, Math.min(150, pending.size())));
            evs = new ArrayList<>(events.subList(0, Math.min(50, events.size())));
            Eval ev = evaluate();
            try {
                JSONArray ba = new JSONArray();
                for (Batch b : sending) ba.put(b.toJson());
                JSONArray ea = new JSONArray();
                for (JSONObject e : evs) ea.put(e);
                JSONObject st = new JSONObject();
                st.put("platform", "android");
                st.put("user", "");
                st.put("app", fgPkg); st.put("app_label", fgLabel); st.put("app_sys", fgSys);
                st.put("domain", "");
                st.put("locked", ev.reason.length() > 0); st.put("reason", ev.reason);
                st.put("idle", 0); st.put("shutdown", false);
                st.put("active_since", activeSince == 0 ? "" : new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.US).format(new Date(activeSince)));
                st.put("offline_sec", offlineSince == 0 ? 0 : (int) ((System.currentTimeMillis() - offlineSince) / 1000));
                st.put("prot", prot);
                if (battery >= 0) st.put("battery", battery);
                body.put("batches", ba); body.put("events", ea); body.put("status", st);
                body.put("version", Cfg.VERSION); body.put("master_rev", cfg.masterRev);
            } catch (Exception ignored) {}
        }
        JSONObject pol;
        try {
            pol = Net.call(cfg.server, "sync", body, cfg.token, timeout);
        } catch (Net.ApiException ex) {
            synchronized (this) {
                syncError = ex.getMessage();
                if (ex.status == 401 || ex.status == 410) { revoked = true; pending.removeAll(sending); events.removeAll(evs); }
            }
            markOffline();
            return;
        } catch (Exception ex) {
            synchronized (this) { syncError = "Server nicht erreichbar"; }
            markOffline();
            return;
        }
        synchronized (this) {
            revoked = false; syncError = "";
            pending.removeAll(sending);
            events.removeAll(evs);
            lastSyncOk = System.currentTimeMillis(); offlineSince = 0;
            if (pol.optBoolean("unassigned")) { policy = null; save(); return; }
            // Serverzeit verankern
            try {
                Date st = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ssXXX", Locale.US).parse(pol.optString("server_time"));
                long wall = System.currentTimeMillis();
                if (anchorServer != 0 && Math.abs(now() - st.getTime()) > 180_000 && Math.abs(wall - st.getTime()) > 180_000)
                    addEvent("time_jump", "Uhrzeit am Handy verstellt", "Abweichung " + ((wall - st.getTime()) / 60000) + " min");
                anchorServer = st.getTime(); anchorElapsed = SystemClock.elapsedRealtime();
            } catch (Exception ignored) {}
            policy = pol;
            JSONObject today = pol.optJSONObject("today");
            if (today != null) { serverUsedDay = today.optString("day"); serverUsed = today.optInt("used_sec"); }
            // Anfragen-Status
            JSONArray rq = pol.optJSONArray("requests");
            if (rq != null) for (int i = 0; i < rq.length(); i++) {
                JSONObject d = rq.optJSONObject(i);
                if (d == null) continue;
                for (JSONObject lr : requests) {
                    if (!lr.optString("local_id").equals(d.optString("local_id"))) continue;
                    String ns = d.optString("status");
                    try {
                        if (!lr.optString("status").equals(ns) && (ns.equals("approved") || ns.equals("denied"))) {
                            JSONObject n = new JSONObject();
                            n.put("type", "request"); n.put("status", ns); n.put("granted", d.optInt("granted")); n.put("kind", lr.optString("kind"));
                            n.put("domain", lr.optString("domain")); n.put("label", lr.optString("label"));
                            notices.add(n);
                        }
                        lr.put("status", ns); lr.put("granted", d.optInt("granted"));
                    } catch (Exception ignored) {}
                }
            }
            Iterator<JSONObject> it = requests.iterator();
            while (it.hasNext()) if (System.currentTimeMillis() - it.next().optLong("created") > 24 * 3600_000L) it.remove();
            // Befehle
            JSONArray cmds = pol.optJSONArray("commands");
            if (cmds != null) for (int i = 0; i < cmds.length(); i++) {
                JSONObject c = cmds.optJSONObject(i);
                if (c == null) continue;
                JSONObject p = c.optJSONObject("payload");
                if (p == null) p = new JSONObject();
                try {
                    JSONObject n = new JSONObject();
                    String t = c.optString("type");
                    if (t.equals("message")) {
                        n.put("type", "message"); n.put("text", p.optString("text")); n.put("from", p.optString("from", "Eltern"));
                        n.put("message_id", p.optInt("message_id")); n.put("priority", p.optString("priority", "normal"));
                        notices.add(n);
                    } else if (t.equals("shutdown") || t.equals("logoff")) {
                        n.put("type", "screen_off"); n.put("from", p.optString("from", "Eltern"));
                        notices.add(n);
                    }
                } catch (Exception ignored) {}
            }
            // Neues Masterpasswort (von den Eltern im Control Center gesetzt)
            JSONObject m = pol.optJSONObject("master");
            if (m != null && m.optInt("rev") > cfg.masterRev && m.optString("hash").length() > 0) {
                cfg.masterSalt = m.optString("salt"); cfg.masterHash = m.optString("hash"); cfg.masterIter = m.optInt("iter", 120000); cfg.masterRev = m.optInt("rev");
                cfg.save();
            }
            JSONObject ch = pol.optJSONObject("child");
            if (ch != null && !ch.optString("name").equals(cfg.childName)) { cfg.childName = ch.optString("name"); cfg.save(); }
            save();
        }
        try { filters.sync(pol.optJSONArray("filters"), cfg.server, cfg.token); } catch (Exception ignored) {}
    }

    synchronized void markOffline() { if (offlineSince == 0) offlineSince = System.currentTimeMillis(); }

    public synchronized int syncInterval() {
        int s = policy == null ? 15 : policy.optInt("sync_interval", 15);
        return Math.max(10, Math.min(120, s));
    }

    /** Warnungen vor dem Sperren: liefert Minuten oder 0 */
    public synchronized int warnDue(Eval e) {
        if (e.reason.length() > 0 || e.free || e.secondsToLock == Integer.MAX_VALUE) return 0;
        Calendar c = Calendar.getInstance();
        c.setTimeInMillis(now());
        String day = ymd(c);
        if (!day.equals(warnedDay)) { warnedDay = day; warned.clear(); }
        JSONArray wm = policy == null ? null : policy.optJSONArray("warn_minutes");
        int[] mins = {15, 5, 1};
        if (wm != null && wm.length() > 0) { mins = new int[wm.length()]; for (int i = 0; i < wm.length(); i++) mins[i] = wm.optInt(i); }
        for (int m : mins) {
            int key = m * 10 + (e.lockKind.equals("time") ? 1 : 2);
            if (m > 0 && e.secondsToLock <= m * 60 && e.secondsToLock > (m * 60 - 90) && !warned.contains(key)) {
                warned.add(key);
                return m;
            }
        }
        return 0;
    }

    /** Sperrgrund-Wechsel → Ereignis für die Eltern */
    public synchronized void noteReason(String reason) {
        if (!reason.equals(lastReason)) {
            String n = childName();
            if (reason.length() > 0) {
                String t = reason.equals("time") ? n + ": Bildschirmzeit aufgebraucht (Handy)" : reason.equals("bedtime") ? n + ": Bettzeit – Handy gesperrt"
                        : reason.equals("manual") ? n + ": Handy von Eltern gesperrt" : reason.equals("school") ? n + ": Schulzeit – Handy gesperrt" : n + ": Handy noch nicht freigegeben";
                addEvent("lock", t, "");
            }
            lastReason = reason;
        }
    }

    // ================= Persistenz =================
    File stateFile() { return new File(ctx.getFilesDir(), "state.json"); }

    public synchronized void save() {
        lastSave = SystemClock.elapsedRealtime();
        try {
            JSONObject d = new JSONObject();
            if (policy != null) d.put("policy", policy);
            d.put("server_used_day", serverUsedDay); d.put("server_used", serverUsed);
            d.put("current", current.toJson());
            JSONArray pa = new JSONArray();
            for (Batch b : pending) pa.put(b.toJson());
            d.put("pending", pa);
            JSONArray ea = new JSONArray();
            for (JSONObject e : events) ea.put(e);
            d.put("events", ea);
            JSONArray ra = new JSONArray();
            for (JSONObject r : requests) ra.put(r);
            d.put("requests", ra);
            d.put("local_free_until", localFreeUntil);
            d.put("warned_day", warnedDay);
            JSONArray w = new JSONArray();
            for (int x : warned) w.put(x);
            d.put("warned", w);
            d.put("last_reason", lastReason);
            File tmp = new File(ctx.getFilesDir(), "state.tmp");
            FileOutputStream fo = new FileOutputStream(tmp);
            fo.write(d.toString().getBytes(StandardCharsets.UTF_8));
            fo.getFD().sync();
            fo.close();
            tmp.renameTo(stateFile());
        } catch (Exception ignored) {}
    }

    synchronized void load() {
        try {
            File f = stateFile();
            if (!f.exists()) return;
            JSONObject d = new JSONObject(new String(Net.readAll(new java.io.FileInputStream(f)), StandardCharsets.UTF_8));
            policy = d.optJSONObject("policy");
            serverUsedDay = d.optString("server_used_day"); serverUsed = d.optInt("server_used");
            JSONObject c = d.optJSONObject("current");
            if (c != null) current = Batch.fromJson(c);
            JSONArray pa = d.optJSONArray("pending");
            if (pa != null) for (int i = 0; i < pa.length(); i++) pending.add(Batch.fromJson(pa.getJSONObject(i)));
            JSONArray ea = d.optJSONArray("events");
            if (ea != null) for (int i = 0; i < ea.length(); i++) events.add(ea.getJSONObject(i));
            JSONArray ra = d.optJSONArray("requests");
            if (ra != null) for (int i = 0; i < ra.length(); i++) requests.add(ra.getJSONObject(i));
            localFreeUntil = d.optLong("local_free_until");
            warnedDay = d.optString("warned_day");
            JSONArray w = d.optJSONArray("warned");
            if (w != null) for (int i = 0; i < w.length(); i++) warned.add(w.getInt(i));
            lastReason = d.optString("last_reason");
        } catch (Exception ignored) {}
    }

    /** Alles löschen (Entkoppeln) */
    public synchronized void reset() {
        policy = null; pending.clear(); events.clear(); requests.clear(); notices.clear();
        current = new Batch(); serverUsed = 0; serverUsedDay = ""; localFreeUntil = 0; lastReason = "";
        stateFile().delete();
        filters.clear();
    }

    // ================= Nutzungspaket =================
    static final class Batch {
        String id = UUID.randomUUID().toString().replace("-", "");
        String day = new SimpleDateFormat("yyyy-MM-dd", Locale.US).format(new Date());
        final Map<Integer, Integer> hours = new HashMap<>();
        final Map<String, Object[]> apps = new HashMap<>(); // pkg → [label, sec, sys]

        int total() { int t = 0; for (int v : hours.values()) t += v; return t; }
        boolean empty() { return hours.isEmpty(); }

        void add(int hour, String pkg, String label, boolean sys) {
            Integer h = hours.get(hour);
            hours.put(hour, (h == null ? 0 : h) + 1);
            if (pkg == null || pkg.length() == 0) return;
            Object[] a = apps.get(pkg);
            if (a == null) { a = new Object[]{label == null ? pkg : label, 0, sys}; apps.put(pkg, a); }
            a[1] = (Integer) a[1] + 1;
        }

        JSONObject toJson() {
            JSONObject o = new JSONObject();
            try {
                o.put("id", id); o.put("day", day);
                JSONObject h = new JSONObject();
                for (Map.Entry<Integer, Integer> e : hours.entrySet()) h.put(String.valueOf(e.getKey()), e.getValue());
                o.put("hours", h);
                JSONObject a = new JSONObject();
                for (Map.Entry<String, Object[]> e : apps.entrySet()) {
                    JSONObject x = new JSONObject();
                    x.put("label", e.getValue()[0]); x.put("sec", e.getValue()[1]);
                    if (Boolean.TRUE.equals(e.getValue()[2])) x.put("sys", 1);
                    a.put(e.getKey(), x);
                }
                o.put("apps", a);
                o.put("web", new JSONObject());
            } catch (Exception ignored) {}
            return o;
        }

        static Batch fromJson(JSONObject d) {
            Batch b = new Batch();
            b.id = d.optString("id", b.id); b.day = d.optString("day", b.day);
            JSONObject h = d.optJSONObject("hours");
            if (h != null) { Iterator<String> k = h.keys(); while (k.hasNext()) { String s = k.next(); b.hours.put(Integer.parseInt(s), h.optInt(s)); } }
            JSONObject a = d.optJSONObject("apps");
            if (a != null) { Iterator<String> k = a.keys(); while (k.hasNext()) { String s = k.next(); JSONObject x = a.optJSONObject(s); if (x != null) b.apps.put(s, new Object[]{x.optString("label"), x.optInt("sec"), x.optInt("sys") == 1}); } }
            return b;
        }
    }
}
