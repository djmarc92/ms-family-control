package de.mstrassenit.familycontrol;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.app.admin.DevicePolicyManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.net.VpnService;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import org.json.JSONObject;

/** Einrichtung (Assistent), Status fürs Kind und Eltern-Bereich */
public class MainActivity extends Activity {
    static final int REQ_VPN = 11, REQ_NOTIF = 12;

    Cfg cfg;
    Engine eng;
    SharedPreferences sp;
    Ui.Pal p;
    LinearLayout box;
    final Handler h = new Handler(Looper.getMainLooper());
    boolean parentMode;
    String screen = "";

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        cfg = Cfg.get(this);
        eng = Engine.get(this);
        sp = getSharedPreferences("msfc_ui", MODE_PRIVATE);
        parentMode = getIntent() != null && getIntent().getBooleanExtra("parent", false);
        getWindow().setStatusBarColor(0xFF060A1E);
        getWindow().setNavigationBarColor(0xFF060A1E);
    }

    @Override
    protected void onNewIntent(Intent i) {
        super.onNewIntent(i);
        if (i.getBooleanExtra("parent", false)) parentMode = true;
    }

    @Override
    protected void onResume() {
        super.onResume();
        render();
        h.removeCallbacks(refresh);
        h.postDelayed(refresh, 5000);
    }

    @Override
    protected void onStop() {
        super.onStop();
        parentMode = false; // Eltern-Bereich nie offen liegen lassen
    }

    @Override
    protected void onPause() {
        super.onPause();
        h.removeCallbacks(refresh);
    }

    final Runnable refresh = new Runnable() {
        public void run() {
            if (screen.equals("status") || screen.equals("perm")) render();
            h.postDelayed(this, 5000);
        }
    };

    // =====================================================================
    void base() {
        JSONObject pol = eng.policy();
        String theme = "anime";
        if (pol != null && pol.optJSONObject("theme") != null) theme = pol.optJSONObject("theme").optString("default", "anime");
        p = Ui.Pal.of(theme, null);
        ScrollView sv = new ScrollView(this);
        sv.setFillViewport(true);
        sv.setBackground(new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM, new int[]{p.bg1, p.bg2}));
        box = Ui.col(this);
        int pad = Ui.dp(this, 20);
        box.setPadding(pad, Ui.dp(this, 28), pad, Ui.dp(this, 28));
        sv.addView(box, new ScrollView.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        setContentView(sv);
        LinearLayout head = Ui.row(this);
        ImageView ic = new ImageView(this);
        ic.setImageResource(R.mipmap.ic_launcher);
        head.addView(ic, new LinearLayout.LayoutParams(Ui.dp(this, 46), Ui.dp(this, 46)));
        LinearLayout t = Ui.col(this);
        t.addView(Ui.text(this, "MS Family Control", 20, p.light ? p.acc : p.text, true));
        t.addView(Ui.text(this, cfg.childName.length() > 0 ? "Familienschutz · " + cfg.childName : "Familienschutz fürs Handy", 13, p.muted, false));
        LinearLayout.LayoutParams tl = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        tl.leftMargin = Ui.dp(this, 12);
        head.addView(t, tl);
        box.addView(head);
    }

    LinearLayout card() {
        LinearLayout c = Ui.col(this);
        int pd = Ui.dp(this, 16);
        c.setPadding(pd, pd, pd, pd);
        c.setBackground(Ui.round(p.card, Ui.dp(this, p.radius), p.light ? p.border : 0x44FFFFFF, Ui.dp(this, 1)));
        box.addView(c, Ui.lp(this, 16));
        return c;
    }

    TextView h2(LinearLayout c, String s) { TextView t = Ui.text(this, s, 18, p.light ? p.acc : p.text, true); c.addView(t); return t; }
    TextView para(LinearLayout c, String s) { TextView t = Ui.text(this, s, 14, p.light ? p.text : p.muted, false); t.setLineSpacing(0, 1.15f); c.addView(t, Ui.lp(this, 6)); return t; }
    Button btn(LinearLayout c, String s, boolean primary, View.OnClickListener l) { Button b = Ui.button(this, s, p, primary); b.setOnClickListener(l); c.addView(b, Ui.lp(this, 10)); return b; }
    TextView err(LinearLayout c) { TextView t = Ui.text(this, "", 14, 0xFFFF5470, true); c.addView(t, Ui.lp(this, 8)); return t; }

    void render() {
        if (!cfg.setupDone || !cfg.paired()) { wizard(); return; }
        if (cfg.removed) { removedScreen(); return; }
        if (parentMode) { parentScreen(); return; }
        statusScreen();
    }

    // =====================================================================
    // Einrichtungs-Assistent
    // =====================================================================
    int step() { return sp.getInt("wiz", 0); }
    void setStep(int s) { sp.edit().putInt("wiz", s).apply(); render(); }

    void wizard() {
        screen = "wizard";
        base();
        int s = step();
        if (s >= 2 && !cfg.hasMaster()) s = 1;
        if (s >= 3 && !cfg.paired()) s = 2;
        LinearLayout dots = Ui.row(this);
        String[] names = {"Server", "Passwort", "Koppeln", "Rechte"};
        for (int i = 0; i < 4; i++) {
            TextView d = Ui.text(this, (i < s ? "✓ " : (i + 1) + " ") + names[i], 12, i == s ? p.btnText : p.text, true);
            d.setGravity(Gravity.CENTER);
            int pp = Ui.dp(this, 6);
            d.setPadding(pp, pp, pp, pp);
            d.setBackground(Ui.round(i == s ? p.acc : 0x22FFFFFF, Ui.dp(this, 20), p.acc, i == s ? 0 : Ui.dp(this, 1)));
            dots.addView(Ui.weight(d, this, i == 0 ? 0 : 6));
        }
        box.addView(dots, Ui.lp(this, 18));
        if (s == 0) stepServer();
        else if (s == 1) stepMaster();
        else if (s == 2) stepPair();
        else { screen = "perm"; permissions(true); }
    }

    void stepServer() {
        LinearLayout c = card();
        h2(c, "1 · Server-Adresse");
        para(c, "Die Adresse deines Family-Control-Servers – so wie sie im Control Center unter „Geräte & Download“ steht, z. B. http://192.168.1.10:8080 oder https://family.deine-domain.de");
        final EditText url = Ui.input(this, "http://…", p, false);
        url.setText(cfg.server);
        url.setInputType(android.text.InputType.TYPE_TEXT_VARIATION_URI | android.text.InputType.TYPE_CLASS_TEXT);
        c.addView(url, Ui.lp(this, 12));
        final TextView e = err(c);
        final Button go = btn(c, "Verbindung prüfen", true, null);
        go.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                final String srv = Cfg.normServer(url.getText().toString());
                if (srv.length() < 10) { e.setText("Bitte eine Adresse eingeben"); return; }
                go.setEnabled(false);
                e.setTextColor(p.muted);
                e.setText("Verbinde …");
                new Thread(new Runnable() {
                    public void run() {
                        String msg;
                        boolean ok = false;
                        try {
                            JSONObject r = Net.call(srv, "ping", null, null, 10000);
                            msg = "✓ Verbunden: " + r.optString("family", "Familie") + " · Server " + r.optString("version");
                            ok = true;
                        } catch (Exception ex) {
                            msg = "✗ " + (ex instanceof Net.ApiException ? ex.getMessage() : "Server nicht erreichbar – Adresse und WLAN prüfen");
                        }
                        final String m = msg;
                        final boolean okk = ok;
                        runOnUiThread(new Runnable() {
                            public void run() {
                                go.setEnabled(true);
                                e.setTextColor(okk ? 0xFF2EE88A : 0xFFFF5470);
                                e.setText(m);
                                if (okk) {
                                    cfg.server = srv;
                                    cfg.save();
                                    h.postDelayed(new Runnable() { public void run() { setStep(1); } }, 700);
                                }
                            }
                        });
                    }
                }).start();
            }
        });
    }

    void stepMaster() {
        LinearLayout c = card();
        h2(c, "2 · Masterpasswort");
        para(c, "Damit können nur Mama oder Papa am Handy entsperren, Einstellungen ändern oder die App entfernen. Das Kind darf es nicht kennen. Du kannst es später auch im Control Center ändern.");
        final EditText a = Ui.input(this, "Masterpasswort (mind. 6 Zeichen)", p, true);
        final EditText b2 = Ui.input(this, "Wiederholen", p, true);
        c.addView(a, Ui.lp(this, 12));
        c.addView(b2, Ui.lp(this, 8));
        final TextView e = err(c);
        btn(c, "Weiter", true, new View.OnClickListener() {
            public void onClick(View v) {
                String x = a.getText().toString(), y = b2.getText().toString();
                if (x.length() < 6) { e.setText("Mindestens 6 Zeichen"); return; }
                if (!x.equals(y)) { e.setText("Die Passwörter stimmen nicht überein"); return; }
                cfg.setMaster(x);
                cfg.save();
                setStep(2);
            }
        });
        btn(c, "Zurück", false, new View.OnClickListener() { public void onClick(View v) { setStep(0); } });
    }

    void stepPair() {
        LinearLayout c = card();
        h2(c, "3 · Mit Kinderprofil koppeln");
        para(c, "Im Control Center: Geräte & Download → „Gerät koppeln“ → Kind wählen. Den 8-stelligen Code hier eingeben (1 Stunde gültig).");
        final EditText code = Ui.input(this, "Kopplungscode, z. B. AB12-CD34", p, false);
        code.setInputType(android.text.InputType.TYPE_CLASS_TEXT | android.text.InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS);
        final EditText name = Ui.input(this, "Gerätename", p, false);
        name.setText(cfg.deviceName.length() > 0 ? cfg.deviceName : (Build.MANUFACTURER.substring(0, 1).toUpperCase() + Build.MANUFACTURER.substring(1) + " " + Build.MODEL));
        c.addView(code, Ui.lp(this, 12));
        c.addView(name, Ui.lp(this, 8));
        final TextView e = err(c);
        final Button go = btn(c, "Koppeln", true, null);
        go.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                final String cd = code.getText().toString().replaceAll("[^A-Za-z0-9]", "").toUpperCase();
                final String nm = name.getText().toString().trim();
                if (cd.length() < 6) { e.setText("Bitte den Code eingeben"); return; }
                go.setEnabled(false);
                e.setTextColor(p.muted);
                e.setText("Kopple …");
                new Thread(new Runnable() {
                    public void run() {
                        String msg = null;
                        try {
                            JSONObject b = new JSONObject();
                            b.put("code", cd);
                            b.put("name", nm.length() > 0 ? nm : Build.MODEL);
                            b.put("os", "Android " + Build.VERSION.RELEASE + " · " + Build.MANUFACTURER + " " + Build.MODEL);
                            b.put("version", Cfg.VERSION);
                            JSONObject r = Net.call(cfg.server, "pair", b, null, 15000);
                            eng.reset();
                            cfg.token = r.optString("token");
                            cfg.deviceId = r.optInt("device_id");
                            cfg.deviceName = nm;
                            JSONObject ch = r.optJSONObject("child");
                            cfg.childName = ch != null ? ch.optString("name") : "";
                            cfg.removed = false;
                            cfg.save();
                            eng.sync(true);
                        } catch (Exception ex) {
                            msg = ex instanceof Net.ApiException ? ex.getMessage() : "Server nicht erreichbar";
                        }
                        final String m = msg;
                        runOnUiThread(new Runnable() {
                            public void run() {
                                go.setEnabled(true);
                                if (m != null) { e.setTextColor(0xFFFF5470); e.setText("✗ " + m); return; }
                                setStep(3);
                            }
                        });
                    }
                }).start();
            }
        });
        btn(c, "Zurück", false, new View.OnClickListener() { public void onClick(View v) { setStep(1); } });
    }

    // =====================================================================
    // Berechtigungen (Assistent Schritt 4 und Eltern-Bereich)
    // =====================================================================
    void permissions(final boolean wizard) {
        final JSONObject pr = GuardService.protection(this);
        boolean notif = checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED;
        LinearLayout c = card();
        h2(c, wizard ? "4 · Berechtigungen" : "Berechtigungen");
        para(c, "Tippe nacheinander auf jeden Punkt und schalte die Berechtigung für „MS Family Control“ ein. Danach hierher zurückkehren.");
        perm(c, "Benachrichtigungen", "Für Hinweise wie „Noch 5 Minuten“ und Nachrichten.", notif, new Runnable() {
            public void run() { requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, REQ_NOTIF); }
        });
        perm(c, "Nutzungszugriff", "Zählt die Bildschirmzeit pro App.", pr.optBoolean("usage"), new Runnable() {
            public void run() { if (!open(new Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS, Uri.parse("package:" + getPackageName())))) open(new Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS)); }
        });
        perm(c, "Über anderen Apps anzeigen", "Für die Sperrbildschirme – auch über Spielen.", pr.optBoolean("overlay"), new Runnable() {
            public void run() { if (!open(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:" + getPackageName())))) open(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION)); }
        });
        LinearLayout acc = perm(c, "Bedienungshilfe", "Erkennt sofort, welche App geöffnet wird, und schützt die Einstellungen. Ist der Schalter grau? Dann zuerst den Punkt darunter.", pr.optBoolean("access"), new Runnable() {
            public void run() {
                Intent i = new Intent("android.settings.ACCESSIBILITY_DETAILS_SETTINGS");
                i.putExtra(Intent.EXTRA_COMPONENT_NAME, SysApps.accessComponent(MainActivity.this).flattenToString());
                if (!open(i)) open(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS));
            }
        });
        if (!pr.optBoolean("access")) {
            Button ri = Ui.button(this, "Eingeschränkte Einstellungen zulassen (App-Info → ⋮ oben rechts)", p, false);
            ri.setTextSize(13);
            ri.setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) { open(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + getPackageName()))); }
            });
            acc.addView(ri, Ui.lp(this, 8));
        }
        perm(c, "Geräteadministrator", "Verhindert das Deinstallieren ohne Masterpasswort.", pr.optBoolean("admin"), new Runnable() {
            public void run() {
                Intent i = new Intent(DevicePolicyManager.ACTION_ADD_DEVICE_ADMIN);
                i.putExtra(DevicePolicyManager.EXTRA_DEVICE_ADMIN, new ComponentName(MainActivity.this, AdminReceiver.class));
                i.putExtra(DevicePolicyManager.EXTRA_ADD_EXPLANATION, "Damit die Familien-App nicht ohne Masterpasswort entfernt werden kann.");
                open(i);
            }
        });
        boolean vpnOk = VpnService.prepare(this) == null;
        perm(c, "Webfilter", "Sperrt Porno-, Betrugs- und gesperrte Seiten in allen Browsern und erzwingt SafeSearch. Läuft nur auf dem Handy (kein fremder Server).", vpnOk && (FilterVpn.running || !eng.webFilterWanted()), new Runnable() {
            public void run() {
                Intent i = VpnService.prepare(MainActivity.this);
                if (i != null) startActivityForResult(i, REQ_VPN);
                else { FilterVpn.start(MainActivity.this); h.postDelayed(new Runnable() { public void run() { render(); } }, 800); }
            }
        });
        perm(c, "Akku nicht einschränken", "Damit Android die App nicht im Hintergrund beendet.", pr.optBoolean("battery"), new Runnable() {
            public void run() { open(new Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:" + getPackageName()))); }
        });
        LinearLayout tip = card();
        h2(tip, "💡 Extra-Tipp: Webfilter dauerhaft");
        para(tip, "Einstellungen → Verbindungen/Netzwerk → VPN → MS Family Control → ⚙ → „Durchgehend aktives VPN“ und „Verbindungen ohne VPN blockieren“ einschalten. Dann kann der Filter nicht umgangen werden.\n\n„Privates DNS“ (unter Verbindungen → Weitere Einstellungen) auf „Automatisch“ oder „Aus“ lassen – ein eigener DNS-Anbieter würde den Filter umgehen. Danach ist diese Einstellung für das Kind gesperrt.");
        btn(tip, "VPN-Einstellungen öffnen", false, new View.OnClickListener() { public void onClick(View v) { open(new Intent(Settings.ACTION_VPN_SETTINGS)); } });

        final boolean essentials = pr.optBoolean("usage") && pr.optBoolean("overlay");
        LinearLayout f = card();
        if (wizard) {
            para(f, essentials ? (pr.optBoolean("access") && pr.optBoolean("admin") ? "Alles bereit! 🎉" : "Die wichtigsten Rechte sind da. Bedienungshilfe und Geräteadministrator werden dringend empfohlen – ohne sie ist der Schutz leichter zu umgehen.")
                    : "Mindestens Nutzungszugriff und „Über anderen Apps“ sind nötig.");
            Button done = btn(f, "✓ Fertig – Schutz starten", true, new View.OnClickListener() {
                public void onClick(View v) {
                    cfg.setupDone = true;
                    cfg.removed = false;
                    cfg.save();
                    sp.edit().putInt("wiz", 0).apply();
                    GuardService.ensure(MainActivity.this);
                    if (eng.webFilterWanted()) FilterVpn.start(MainActivity.this);
                    parentMode = false;
                    render();
                }
            });
            done.setEnabled(essentials);
            done.setAlpha(essentials ? 1f : 0.5f);
            btn(f, "Zurück", false, new View.OnClickListener() { public void onClick(View v) { setStep(2); } });
        } else {
            btn(f, "Zurück", false, new View.OnClickListener() { public void onClick(View v) { parentScreen(); } });
        }
    }

    LinearLayout perm(LinearLayout parent, String title, String desc, boolean ok, final Runnable action) {
        LinearLayout r = Ui.col(this);
        int pd = Ui.dp(this, 12);
        r.setPadding(pd, pd, pd, pd);
        r.setBackground(Ui.round(p.light ? 0xFFF6F9FF : 0x22000000, Ui.dp(this, p.radius), ok ? 0xFF2EE88A : 0xFFFF5470, Ui.dp(this, 1.5f)));
        LinearLayout top = Ui.row(this);
        TextView t = Ui.text(this, (ok ? "✅ " : "⚠️ ") + title, 16, p.text, true);
        top.addView(Ui.weight(t, this, 0));
        if (!ok) {
            Button b = Ui.button(this, "Einrichten", p, true);
            b.setTextSize(14);
            b.setMinHeight(Ui.dp(this, 40));
            b.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { action.run(); } });
            top.addView(b);
        }
        r.addView(top);
        TextView d = Ui.text(this, desc, 13, p.muted, false);
        r.addView(d, Ui.lp(this, 4));
        parent.addView(r, Ui.lp(this, 10));
        return r;
    }

    boolean open(Intent i) {
        eng.unlockSettings(10); // eigene Schritte nicht vom Einstellungs-Schutz blockieren lassen
        try { startActivity(i); return true; } catch (Exception e) { return false; }
    }

    @Override
    protected void onActivityResult(int req, int res, Intent data) {
        super.onActivityResult(req, res, data);
        if (req == REQ_VPN && res == RESULT_OK) FilterVpn.start(this);
        h.postDelayed(new Runnable() { public void run() { render(); } }, 600);
    }

    @Override
    public void onRequestPermissionsResult(int req, String[] perms, int[] res) { render(); }

    // =====================================================================
    // Status fürs Kind
    // =====================================================================
    void statusScreen() {
        screen = "status";
        GuardService.ensure(this);
        base();
        Engine.Eval ev = eng.evaluate();
        LinearLayout c = card();
        c.setGravity(Gravity.CENTER_HORIZONTAL);
        String big, small;
        if (eng.policy() == null) { big = "Verbinde …"; small = eng.syncError.length() > 0 ? eng.syncError : "Warte auf den Server"; }
        else if (ev.free) { big = "Freigegeben"; small = "bis " + ev.until + " Uhr"; }
        else if (ev.reason.length() > 0) { big = "🔒 Gesperrt"; small = ev.until.length() > 0 ? "wieder ab " + ev.until + " Uhr" : "frag Mama oder Papa"; }
        else if (ev.unlimited) { big = "Ohne Limit"; small = "heute"; }
        else { big = Ui.dur(ev.remaining); small = "Bildschirmzeit übrig"; }
        TextView b = Ui.text(this, big, 40, p.light ? p.acc : p.text, true);
        b.setGravity(Gravity.CENTER);
        if (!p.light) b.setShadowLayer(Ui.dp(this, 10), 0, 0, p.acc);
        c.addView(b);
        TextView s = Ui.text(this, small, 15, p.muted, false);
        s.setGravity(Gravity.CENTER);
        c.addView(s, Ui.lp(this, 2));
        if (eng.policy() != null) {
            JSONObject today = eng.policy().optJSONObject("today");
            String dl = today != null ? today.optString("day_label", "") : "";
            para(c, dl + " · erlaubt " + ev.start + "–" + ev.end + " Uhr · heute genutzt " + Ui.dur(ev.used)).setGravity(Gravity.CENTER);
        }
        final TextView st = Ui.text(this, "", 14, p.acc2, true);
        st.setGravity(Gravity.CENTER);
        c.addView(st, Ui.lp(this, 8));
        JSONObject r = eng.openRequest("time", null);
        if (r != null) st.setText(Overlays.reqText(r));
        if (!ev.unlimited && eng.policy() != null) {
            LinearLayout rr = Ui.row(this);
            int[] mins = {15, 30, 60};
            for (int i = 0; i < mins.length; i++) {
                final int m = mins[i];
                Button bb = Ui.button(this, "+" + m + " min", p, false);
                bb.setOnClickListener(new View.OnClickListener() {
                    public void onClick(View v) {
                        String e = eng.addRequest("time", m, "", "", "");
                        st.setText(e.length() > 0 ? e : "📨 Anfrage über +" + m + " min gesendet");
                        GuardService.kick(MainActivity.this);
                    }
                });
                rr.addView(Ui.weight(bb, this, i == 0 ? 0 : 8));
            }
            para(c, "Mehr Zeit bei Mama oder Papa anfragen:");
            c.addView(rr, Ui.lp(this, 6));
        }
        // Schutz-Zustand
        JSONObject pr = GuardService.protection(this);
        StringBuilder miss = new StringBuilder();
        java.util.Iterator<String> it = pr.keys();
        while (it.hasNext()) { String k = it.next(); if (!pr.optBoolean(k, true)) miss.append(miss.length() > 0 ? ", " : "").append(GuardService.PROT_NAMES.get(k)); }
        if (miss.length() > 0) {
            LinearLayout w = card();
            w.setBackground(Ui.round(p.card, Ui.dp(this, p.radius), 0xFFFF5470, Ui.dp(this, 2)));
            h2(w, "⚠ Schutz unvollständig");
            para(w, "Es fehlt: " + miss + ". Mama oder Papa können das im Eltern-Bereich beheben.");
        }
        LinearLayout info = card();
        String sync = eng.lastSyncOk == 0 ? "noch nicht verbunden" : "zuletzt vor " + Math.max(0, (System.currentTimeMillis() - eng.lastSyncOk) / 1000) + " s";
        para(info, (eng.syncError.length() > 0 ? "⚠ " + eng.syncError + " · " : "🟢 Verbunden · ") + sync + "\nServer: " + cfg.server + "\nApp-Version " + Cfg.VERSION + (eng.revoked ? "\n⚠ Gerät wurde im Control Center entfernt – bitte neu koppeln." : ""));
        btn(info, "👪 Eltern-Bereich", false, new View.OnClickListener() { public void onClick(View v) { askMaster(); } });
    }

    void askMaster() {
        final EditText pw = Ui.input(this, "Masterpasswort", Ui.Pal.of("family", null), true);
        new AlertDialog.Builder(this).setTitle("Eltern-Bereich").setView(pw)
                .setPositiveButton("OK", new android.content.DialogInterface.OnClickListener() {
                    public void onClick(android.content.DialogInterface d, int w) {
                        String e = eng.checkMaster(pw.getText().toString(), "Familien-App");
                        if (e != null) { new AlertDialog.Builder(MainActivity.this).setMessage(e).setPositiveButton("OK", null).show(); return; }
                        parentMode = true;
                        eng.unlockSettings(10);
                        render();
                    }
                }).setNegativeButton("Abbrechen", null).show();
    }

    // =====================================================================
    // Eltern-Bereich
    // =====================================================================
    void parentScreen() {
        screen = "parent";
        base();
        LinearLayout c = card();
        h2(c, "👪 Eltern-Bereich");
        para(c, "Einstellungen und App-Info sind jetzt 10 Minuten freigegeben.");
        LinearLayout rr = Ui.row(this);
        int[] mins = {15, 30, 60, 120};
        for (int i = 0; i < mins.length; i++) {
            final int m = mins[i];
            Button bb = Ui.button(this, m >= 60 ? (m / 60) + " h" : m + " min", p, true);
            bb.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { eng.unlockFor(m); Overlays.hideLock(); toast("Freigegeben für " + (m >= 60 ? (m / 60) + " h" : m + " min")); } });
            rr.addView(Ui.weight(bb, this, i == 0 ? 0 : 6));
        }
        para(c, "Handy freigeben für …");
        c.addView(rr, Ui.lp(this, 6));
        btn(c, "🔐 Berechtigungen prüfen", false, new View.OnClickListener() { public void onClick(View v) { screen = "perm"; base(); permissions(false); } });
        btn(c, "🔑 Masterpasswort ändern", false, new View.OnClickListener() { public void onClick(View v) { changeMaster(); } });
        LinearLayout d = card();
        h2(d, "Gerät");
        para(d, "Kind: " + cfg.childName + "\nGerät: " + cfg.deviceName + "\nServer: " + cfg.server + "\nWebfilter: " + (FilterVpn.running ? "aktiv · " + eng.filters.count() + " Einträge" : "aus"));
        btn(d, "🔗 Neu koppeln / Server ändern", false, new View.OnClickListener() {
            public void onClick(View v) {
                confirm("Neu koppeln?", "Das Handy wird vom aktuellen Kinderprofil getrennt. Danach Server und neuen Kopplungscode eingeben.", new Runnable() {
                    public void run() {
                        cfg.token = "";
                        cfg.setupDone = false;
                        cfg.save();
                        eng.reset();
                        Overlays.hideAll();
                        sp.edit().putInt("wiz", 0).apply();
                        parentMode = false;
                        render();
                    }
                });
            }
        });
        btn(d, "🗑 Schutz entfernen & deinstallieren", false, new View.OnClickListener() {
            public void onClick(View v) {
                confirm("Kinderschutz entfernen?", "Alle Sperren werden aufgehoben und die App kann deinstalliert werden. Im Control Center wird das als Ereignis angezeigt.", new Runnable() { public void run() { removeProtection(); } });
            }
        });
        btn(d, "← Zurück zur Übersicht", false, new View.OnClickListener() { public void onClick(View v) { parentMode = false; render(); } });
    }

    void changeMaster() {
        LinearLayout l = Ui.col(this);
        int pd = Ui.dp(this, 16);
        l.setPadding(pd, pd, pd, 0);
        Ui.Pal lp = Ui.Pal.of("family", null);
        final EditText a = Ui.input(this, "Neues Masterpasswort", lp, true);
        final EditText b = Ui.input(this, "Wiederholen", lp, true);
        l.addView(a);
        l.addView(b, Ui.lp(this, 8));
        new AlertDialog.Builder(this).setTitle("Masterpasswort ändern").setView(l)
                .setPositiveButton("Speichern", new android.content.DialogInterface.OnClickListener() {
                    public void onClick(android.content.DialogInterface d, int w) {
                        String x = a.getText().toString();
                        if (x.length() < 6 || !x.equals(b.getText().toString())) { toast("Mind. 6 Zeichen und beide gleich"); return; }
                        cfg.setMaster(x);
                        cfg.save();
                        toast("Masterpasswort geändert (nur auf diesem Handy)");
                    }
                }).setNegativeButton("Abbrechen", null).show();
    }

    void removeProtection() {
        eng.addEvent("uninstall", "Kinderschutz am Handy mit Masterpasswort entfernt", cfg.deviceName);
        new Thread(new Runnable() { public void run() { eng.sync(true); } }).start();
        cfg.removed = true;
        cfg.save();
        Overlays.hideAll();
        FilterVpn.stop(this);
        try { stopService(new Intent(this, GuardService.class)); } catch (Exception ignored) {}
        try {
            DevicePolicyManager dpm = (DevicePolicyManager) getSystemService(DEVICE_POLICY_SERVICE);
            dpm.removeActiveAdmin(new ComponentName(this, AdminReceiver.class));
        } catch (Exception ignored) {}
        try { if (GuardAccess.inst != null) GuardAccess.inst.disableSelf(); } catch (Exception ignored) {}
        h.postDelayed(new Runnable() {
            public void run() {
                render();
                try { startActivity(new Intent(Intent.ACTION_DELETE, Uri.parse("package:" + getPackageName()))); } catch (Exception ignored) {}
            }
        }, 2500);
    }

    void removedScreen() {
        screen = "removed";
        base();
        LinearLayout c = card();
        h2(c, "Kinderschutz ist entfernt");
        para(c, "Die App kann jetzt deinstalliert werden. Zum erneuten Einrichten unten tippen.");
        btn(c, "App deinstallieren", true, new View.OnClickListener() {
            public void onClick(View v) { try { startActivity(new Intent(Intent.ACTION_DELETE, Uri.parse("package:" + getPackageName()))); } catch (Exception ignored) {} }
        });
        btn(c, "Neu einrichten", false, new View.OnClickListener() {
            public void onClick(View v) {
                cfg.removed = false; cfg.setupDone = false; cfg.token = ""; cfg.save();
                eng.reset();
                sp.edit().putInt("wiz", 0).apply();
                render();
            }
        });
    }

    void confirm(String title, String msg, final Runnable ok) {
        new AlertDialog.Builder(this).setTitle(title).setMessage(msg)
                .setPositiveButton("Ja", new android.content.DialogInterface.OnClickListener() { public void onClick(android.content.DialogInterface d, int w) { ok.run(); } })
                .setNegativeButton("Abbrechen", null).show();
    }

    void toast(String s) { android.widget.Toast.makeText(this, s, android.widget.Toast.LENGTH_SHORT).show(); }
}
