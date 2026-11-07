package de.mstrassenit.familycontrol;

import android.animation.ObjectAnimator;
import android.animation.ValueAnimator;
import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.PixelFormat;
import android.graphics.drawable.GradientDrawable;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.view.inputmethod.InputMethodManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import org.json.JSONObject;

import java.text.SimpleDateFormat;
import java.util.ArrayDeque;
import java.util.Date;
import java.util.Locale;

/**
 * Vollbild-Screens über allen Apps (auch über Spielen): Sperre, App/Spiel/Webseite gesperrt,
 * Nachricht der Eltern, geschützte Einstellungen, Warnhinweis.
 */
public final class Overlays {
    private Overlays() {}

    static final Handler H = new Handler(Looper.getMainLooper());
    static Context app;
    static WindowManager wm;

    // Sperre
    static View lockView;
    static String lockKey = "";
    static TextView lockStatus;
    // Einzel-Fenster (gesperrte App, Nachricht …)
    static View popView;
    static String popKind = "";
    static final ArrayDeque<JSONObject> msgQueue = new ArrayDeque<>();
    // Hinweis-Banner
    static View banner;

    static void init(Context c) {
        if (app == null) {
            app = c.getApplicationContext();
            wm = (WindowManager) app.getSystemService(Context.WINDOW_SERVICE);
        }
    }

    public static boolean canDraw(Context c) { return Settings.canDrawOverlays(c); }

    static WindowManager.LayoutParams full(boolean focusable) {
        WindowManager.LayoutParams lp = new WindowManager.LayoutParams(
                WindowManager.LayoutParams.MATCH_PARENT, WindowManager.LayoutParams.MATCH_PARENT,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN | WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED
                        | (focusable ? 0 : WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE),
                PixelFormat.TRANSLUCENT);
        lp.softInputMode = WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE;
        lp.gravity = Gravity.TOP;
        return lp;
    }

    static void add(View v, WindowManager.LayoutParams lp) {
        try { wm.addView(v, lp); } catch (Exception ignored) {}
    }

    static void remove(View v) {
        if (v == null) return;
        try { wm.removeViewImmediate(v); } catch (Exception ignored) {}
    }

    public static boolean lockShown() { return lockView != null; }
    public static boolean popupShown() { return popView != null; }

    // =====================================================================
    // Sperrbildschirm
    // =====================================================================
    public static void showLock(final Context c, final Engine.Eval ev) {
        init(c);
        if (!canDraw(c)) return;
        final Engine eng = Engine.get(c);
        JSONObject pol = eng.policy();
        String evKey = ev.reason.equals("time") ? "time_expired" : ev.reason.equals("bedtime") ? "bedtime" : "access_blocked";
        JSONObject th = pol == null ? null : pol.optJSONObject("theme");
        String key = ev.reason + "|" + ev.until + "|" + (th == null ? "" : th.optString("version")) + "|" + eng.childName();
        if (lockView != null && key.equals(lockKey)) {
            if (lockStatus != null && ev.reason.equals("time")) {
                JSONObject r = eng.openRequest("time", null);
                String t = r == null ? "" : reqText(r);
                if (!t.equals(lockStatus.getText().toString()) && t.length() > 0) lockStatus.setText(t);
            }
            return;
        }
        remove(lockView);
        lockKey = key;
        final Ui.Spec spec = Ui.Spec.of(pol, evKey);
        String label;
        switch (ev.reason) {
            case "time": label = "Bildschirmzeit"; break;
            case "bedtime": label = "Bettzeit"; break;
            case "school": label = "Schulzeit"; break;
            case "manual": label = "Von Mama & Papa gesperrt"; break;
            default: label = "Noch nicht freigegeben";
        }
        String img = spec.theme.equals("army") ? "army-access.jpg" : (spec.theme.equals("anime") || spec.theme.equals("family"))
                ? (ev.reason.equals("bedtime") ? "sleeping-robot.png" : "awake-robot.png") : null;
        String emoji = ev.reason.equals("bedtime") ? "🌙" : ev.reason.equals("time") ? "⏰" : ev.reason.equals("school") ? "🎒" : "🔒";
        final Screen s = new Screen(c, spec, label, img, emoji);
        String child = eng.childName();
        s.title(spec.fill(spec.title, "", ev.until, child));
        s.text(spec.fill(spec.text, "", ev.until, child));
        LinearLayout chips = Ui.row(c);
        chips.setGravity(Gravity.CENTER);
        if (ev.reason.equals("time")) s.chip(chips, "Heute genutzt: " + Ui.dur(ev.used));
        if (ev.reason.equals("bedtime") && ev.until.length() > 0) s.chip(chips, "Morgen wieder ab " + ev.until + " Uhr");
        if (ev.reason.equals("early")) s.chip(chips, "Erlaubt ab " + ev.until + " Uhr");
        if (ev.reason.equals("school") || ev.reason.equals("manual")) s.chip(chips, "Bis " + ev.until + " Uhr");
        s.box.addView(chips, Ui.lp(c, 14));
        final TextView status = s.status();
        if (ev.reason.equals("time")) {
            JSONObject r = eng.openRequest("time", null);
            if (r != null) status.setText(reqText(r));
            Button more = Ui.button(c, "⏱ Mehr Zeit anfragen", spec.pal, true);
            more.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { s.requestPanel("time", "", "", status); } });
            s.buttons.addView(more, Ui.lp(c, 0));
        }
        Button phone = Ui.button(c, "📞 Telefon / Nachrichten", spec.pal, false);
        phone.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { SysApps.openDialer(c); } });
        s.buttons.addView(phone, Ui.lp(c, 10));
        s.parentButton();
        lockView = s.root;
        lockStatus = status;
        add(lockView, full(true));
        bringPopupToFront();
    }

    public static void hideLock() {
        if (lockView == null) return;
        remove(lockView);
        lockStatus = null;
        lockView = null;
        lockKey = "";
    }

    static String reqText(JSONObject r) {
        String st = r.optString("status");
        if (st.equals("queued")) return "📨 Anfrage wird gesendet …";
        if (st.equals("pending")) return "⏳ Anfrage gesendet – warte auf Antwort von Mama oder Papa";
        if (st.equals("approved")) return "✅ Genehmigt" + (r.optInt("granted") > 0 ? ": +" + r.optInt("granted") + " min" : "");
        if (st.equals("denied")) return "❌ Leider abgelehnt";
        if (st.equals("error")) return "⚠ " + r.optString("error", "Fehler beim Senden");
        return "";
    }

    // =====================================================================
    // App / Spiel / neue App gesperrt
    // =====================================================================
    public static void showBlocked(final Context c, final String kind, final String name, final String pkg) {
        init(c);
        if (!canDraw(c)) return;
        if (popView != null && popKind.equals("app:" + pkg)) return;
        closePopup();
        final Engine eng = Engine.get(c);
        boolean game = kind.equals("game"), unknown = kind.equals("unknown");
        final Ui.Spec spec = Ui.Spec.of(eng.policy(), game ? "game_blocked" : "app_blocked");
        String img = spec.theme.equals("army") ? (game ? "army-game.jpg" : "army-app.jpg") : (spec.theme.equals("anime") || spec.theme.equals("family")) ? "awake-robot.png" : null;
        final Screen s = new Screen(c, spec, unknown ? "Neue App" : game ? "Spielzeit" : "App-Kontrolle", img, game ? "🎮" : "🧩");
        s.title(spec.fill(spec.title, name, "", eng.childName()));
        s.text(unknown ? "„" + name + "“ ist neu auf diesem Handy. Neue Apps müssen erst von deinen Eltern erlaubt werden – frag sie einfach!"
                : spec.fill(spec.text, name, "", eng.childName()));
        final TextView status = s.status();
        JSONObject r = eng.openRequest("app", pkg);
        if (r != null) status.setText(reqText(r));
        Button ask = Ui.button(c, "🙋 Eltern fragen", spec.pal, true);
        ask.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { s.requestPanel("app", pkg, name, status); } });
        s.buttons.addView(ask, Ui.lp(c, 0));
        Button ok = Ui.button(c, "OK", spec.pal, false);
        ok.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { closePopup(); } });
        s.buttons.addView(ok, Ui.lp(c, 10));
        s.parentButton();
        popKind = "app:" + pkg;
        popView = s.root;
        add(popView, full(true));
    }

    // =====================================================================
    // Webseite gesperrt
    // =====================================================================
    public static void showWeb(final Context c, final String domain, String category) {
        init(c);
        if (!canDraw(c)) return;
        if (popView != null && popKind.equals("web:" + domain)) return;
        if (popView != null && popKind.startsWith("msg")) return;
        closePopup();
        final Engine eng = Engine.get(c);
        final Ui.Spec spec = Ui.Spec.of(eng.policy(), "website_blocked");
        String img = spec.theme.equals("army") ? "army-web.jpg" : (spec.theme.equals("anime") || spec.theme.equals("family")) ? "awake-robot.png" : null;
        final Screen s = new Screen(c, spec, "Webfilter", img, "🌐");
        s.title(spec.fill(spec.title, domain, "", eng.childName()));
        s.text(spec.fill(spec.text, domain, "", eng.childName()));
        LinearLayout chips = Ui.row(c);
        chips.setGravity(Gravity.CENTER);
        s.chip(chips, domain);
        if (category != null) s.chip(chips, eng.filters.name(category));
        s.box.addView(chips, Ui.lp(c, 14));
        final TextView status = s.status();
        if (category == null) {
            Button ask = Ui.button(c, "🙋 Eltern fragen", spec.pal, true);
            ask.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { s.requestPanel("site", domain, domain, status); } });
            s.buttons.addView(ask, Ui.lp(c, 0));
        }
        Button ok = Ui.button(c, "OK", spec.pal, category != null);
        ok.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { closePopup(); } });
        s.buttons.addView(ok, Ui.lp(c, 10));
        popKind = "web:" + domain;
        popView = s.root;
        add(popView, full(true));
    }

    // =====================================================================
    // Nachricht der Eltern (auch über Spielen)
    // =====================================================================
    public static void showMessage(Context c, JSONObject n) {
        init(c);
        if (!canDraw(c)) return;
        if (popView != null && popKind.startsWith("msg")) { msgQueue.add(n); return; }
        closePopup();
        final Context cc = c;
        final Engine eng = Engine.get(c);
        final Ui.Spec spec = Ui.Spec.of(eng.policy(), "parent_message");
        final boolean high = n.optString("priority").equals("high");
        final int id = n.optInt("message_id");
        String img = spec.theme.equals("army") ? "army-access.jpg" : (spec.theme.equals("anime") || spec.theme.equals("family")) ? "awake-robot.png" : null;
        Screen s = new Screen(c, spec, high ? "❗ Wichtige Nachricht" : "Nachricht", img, "💬");
        s.title(spec.fill(spec.title, "", "", eng.childName()));
        TextView msg = Ui.text(c, n.optString("text"), 22, spec.pal.text, true);
        msg.setGravity(Gravity.CENTER);
        msg.setLineSpacing(0, 1.15f);
        int pad = Ui.dp(c, 18);
        msg.setPadding(pad, pad, pad, pad);
        msg.setBackground(Ui.round(spec.pal.card, Ui.dp(c, spec.pal.radius), high ? 0xFFFF3B5C : spec.pal.acc, Ui.dp(c, high ? 3 : 1.5f)));
        s.box.addView(msg, Ui.lp(c, 16));
        TextView from = Ui.text(c, "Von: " + n.optString("from", "Eltern") + " · " + new SimpleDateFormat("HH:mm", Locale.GERMANY).format(new Date()) + " Uhr", 13, spec.pal.muted, false);
        from.setGravity(Gravity.CENTER);
        s.box.addView(from, Ui.lp(c, 8));
        Button ok = Ui.button(c, "✓ Gelesen", spec.pal, true);
        ok.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                if (id > 0) eng.addEventRef("message_read", "Nachricht gelesen", id);
                GuardService.kick(cc);
                closePopup();
                JSONObject next = msgQueue.poll();
                if (next != null) showMessage(cc, next);
            }
        });
        s.buttons.addView(ok, Ui.lp(c, 0));
        popKind = "msg" + id;
        popView = s.root;
        add(popView, full(true));
    }

    // =====================================================================
    // Geschützte Einstellungen
    // =====================================================================
    public static void showProtect(final Context c, String what) {
        init(c);
        if (!canDraw(c)) return;
        if (popView != null && popKind.equals("protect")) return;
        closePopup();
        Engine eng = Engine.get(c);
        Ui.Spec spec = Ui.Spec.of(eng.policy(), "access_blocked");
        String img = spec.theme.equals("army") ? "army-access.jpg" : (spec.theme.equals("anime") || spec.theme.equals("family")) ? "awake-robot.png" : null;
        Screen s = new Screen(c, spec, "Familienschutz", img, "🛡");
        s.title("Geschützter Bereich");
        s.text("Diese Einstellung (" + what + ") können nur Mama oder Papa ändern.");
        Button ok = Ui.button(c, "OK", spec.pal, true);
        ok.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { closePopup(); } });
        s.buttons.addView(ok, Ui.lp(c, 0));
        s.parentButton();
        popKind = "protect";
        popView = s.root;
        add(popView, full(true));
    }

    public static void closePopup() {
        if (popView == null) return;
        hideKeyboard(popView);
        remove(popView);
        popView = null;
        popKind = "";
    }

    static void bringPopupToFront() {
        if (popView == null) return;
        View v = popView;
        remove(v);
        add(v, full(true));
    }

    // =====================================================================
    // Warn-Banner oben (nicht bedienbar, verschwindet von selbst)
    // =====================================================================
    public static void showBanner(Context c, String text) {
        init(c);
        if (!canDraw(c)) return;
        hideBanner();
        Ui.Spec spec = Ui.Spec.of(Engine.get(c).policy(), "time_expired");
        TextView t = Ui.text(c, text, 16, spec.pal.text, true);
        t.setGravity(Gravity.CENTER);
        int p = Ui.dp(c, 14);
        t.setPadding(p, p, p, p);
        t.setBackground(Ui.round(spec.pal.card | 0xFF000000, Ui.dp(c, spec.pal.radius), spec.pal.acc, Ui.dp(c, 2)));
        FrameLayout f = new FrameLayout(c);
        int m = Ui.dp(c, 12);
        f.setPadding(m, Ui.dp(c, 36), m, m);
        f.addView(t);
        WindowManager.LayoutParams lp = new WindowManager.LayoutParams(WindowManager.LayoutParams.MATCH_PARENT, WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                PixelFormat.TRANSLUCENT);
        lp.gravity = Gravity.TOP;
        banner = f;
        add(f, lp);
        H.postDelayed(new Runnable() { public void run() { hideBanner(); } }, 7000);
    }

    static void hideBanner() { remove(banner); banner = null; }

    public static void hideAll() {
        hideLock();
        closePopup();
        hideBanner();
        msgQueue.clear();
    }

    static void hideKeyboard(View v) {
        try { ((InputMethodManager) v.getContext().getSystemService(Context.INPUT_METHOD_SERVICE)).hideSoftInputFromWindow(v.getWindowToken(), 0); } catch (Exception ignored) {}
    }

    // =====================================================================
    // Baukasten für einen Vollbild-Screen
    // =====================================================================
    static final class Screen {
        final Context c;
        final Ui.Spec spec;
        final Ui.Pal p;
        final FrameLayout root;
        final LinearLayout box, buttons;
        LinearLayout panel;

        Screen(Context c, Ui.Spec spec, String label, String img, String emoji) {
            this.c = c;
            this.spec = spec;
            this.p = spec.pal;
            root = new FrameLayout(c) {
                @Override
                public boolean dispatchKeyEvent(KeyEvent e) {
                    if (e.getKeyCode() == KeyEvent.KEYCODE_BACK) return true; // Zurück-Taste wirkungslos
                    return super.dispatchKeyEvent(e);
                }
            };
            root.setFocusableInTouchMode(true);
            GradientDrawable bg = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM, new int[]{p.bg1, p.bg2});
            root.setBackground(bg);
            ScrollView sv = new ScrollView(c);
            sv.setFillViewport(true);
            root.addView(sv, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
            LinearLayout outer = Ui.col(c);
            outer.setGravity(Gravity.CENTER);
            int pad = Ui.dp(c, 22);
            outer.setPadding(pad, Ui.dp(c, 40), pad, Ui.dp(c, 30));
            sv.addView(outer, new ScrollView.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
            box = Ui.col(c);
            box.setGravity(Gravity.CENTER_HORIZONTAL);
            outer.addView(box, new LinearLayout.LayoutParams(Math.min(Ui.dp(c, 520), c.getResources().getDisplayMetrics().widthPixels - 2 * pad), ViewGroup.LayoutParams.WRAP_CONTENT));

            // Bild oder Symbol
            Bitmap b = img == null ? null : Ui.asset(c, img);
            View pic;
            if (b != null) {
                ImageView iv = new ImageView(c);
                iv.setImageBitmap(b);
                iv.setAdjustViewBounds(true);
                boolean wide = b.getWidth() > b.getHeight() * 1.3;
                iv.setScaleType(wide ? ImageView.ScaleType.CENTER_CROP : ImageView.ScaleType.FIT_CENTER);
                if (wide) iv.setBackground(Ui.round(0, Ui.dp(c, p.radius), p.acc, Ui.dp(c, 2)));
                if (wide) iv.setClipToOutline(true);
                LinearLayout.LayoutParams l = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, Ui.dp(c, wide ? 190 : 230));
                box.addView(iv, l);
                pic = iv;
            } else {
                TextView e = Ui.text(c, emoji, 64, p.text, false);
                e.setGravity(Gravity.CENTER);
                GradientDrawable ring = new GradientDrawable();
                ring.setShape(GradientDrawable.OVAL);
                ring.setColor(0x22FFFFFF & p.acc | 0x22000000);
                ring.setStroke(Ui.dp(c, 3), p.acc);
                e.setBackground(ring);
                int sz = Ui.dp(c, 150);
                box.addView(e, new LinearLayout.LayoutParams(sz, sz));
                pic = e;
            }
            if (spec.anim) {
                ObjectAnimator a = ObjectAnimator.ofFloat(pic, "translationY", 0f, -Ui.dp(c, 8));
                a.setDuration(1600);
                a.setRepeatMode(ValueAnimator.REVERSE);
                a.setRepeatCount(ValueAnimator.INFINITE);
                a.start();
            }
            TextView lab = Ui.text(c, ("Familienschutz · " + label).toUpperCase(Locale.GERMANY), 12, p.acc, true);
            lab.setLetterSpacing(0.15f);
            lab.setGravity(Gravity.CENTER);
            box.addView(lab, Ui.lp(c, 18));
            buttons = Ui.col(c);
        }

        void title(String t) {
            TextView v = Ui.text(c, p.caps ? t.toUpperCase(Locale.GERMANY) : t, 32, p.light ? p.acc : p.text, true);
            v.setGravity(Gravity.CENTER);
            if (!p.light) v.setShadowLayer(Ui.dp(c, 12), 0, 0, p.acc);
            if (p.key.equals("manga") || p.key.equals("army")) v.setShadowLayer(1, Ui.dp(c, 3), Ui.dp(c, 3), 0xFF000000);
            box.addView(v, Ui.lp(c, 6));
        }

        void text(String t) {
            TextView v = Ui.text(c, t, 17, p.light ? p.text : p.muted, false);
            v.setGravity(Gravity.CENTER);
            v.setLineSpacing(0, 1.15f);
            box.addView(v, Ui.lp(c, 10));
        }

        void chip(LinearLayout row, String t) {
            TextView v = Ui.text(c, t, 13, p.text, true);
            int h = Ui.dp(c, 12), vv = Ui.dp(c, 7);
            v.setPadding(h, vv, h, vv);
            v.setBackground(Ui.round(p.light ? 0xFFFFFFFF : 0x33000000, Ui.dp(c, 30), p.acc2, Ui.dp(c, 1.5f)));
            LinearLayout.LayoutParams l = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            l.leftMargin = l.rightMargin = Ui.dp(c, 4);
            row.addView(v, l);
        }

        TextView status() {
            TextView st = Ui.text(c, "", 15, p.acc2, true);
            st.setGravity(Gravity.CENTER);
            box.addView(st, Ui.lp(c, 14));
            panel = Ui.col(c);
            box.addView(panel, Ui.lp(c, 6));
            box.addView(buttons, Ui.lp(c, 14));
            return st;
        }

        void ensurePanel() {
            if (panel == null) { status(); }
        }

        /** „Eltern“-Knopf: Masterpasswort → Eltern-Menü */
        void parentButton() {
            ensurePanel();
            Button par = Ui.button(c, "👪 Eltern", p, false);
            par.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { passwordPanel(); } });
            buttons.addView(par, Ui.lp(c, 10));
            TextView foot = Ui.text(c, "MS Family Control", 11, p.muted, false);
            foot.setGravity(Gravity.CENTER);
            box.addView(foot, Ui.lp(c, 22));
        }

        void passwordPanel() {
            panel.removeAllViews();
            buttons.setVisibility(View.GONE);
            LinearLayout card = card();
            card.addView(Ui.text(c, "Masterpasswort", 16, p.text, true));
            final EditText pw = Ui.input(c, "Passwort eingeben", p, true);
            card.addView(pw, Ui.lp(c, 10));
            final TextView err = Ui.text(c, "", 13, 0xFFFF5470, true);
            card.addView(err, Ui.lp(c, 6));
            LinearLayout r = Ui.row(c);
            Button back = Ui.button(c, "Zurück", p, false);
            Button go = Ui.button(c, "Weiter", p, true);
            r.addView(Ui.weight(back, c, 0));
            r.addView(Ui.weight(go, c, 10));
            card.addView(r, Ui.lp(c, 10));
            panel.addView(card);
            back.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { hideKeyboard(pw); panel.removeAllViews(); buttons.setVisibility(View.VISIBLE); } });
            go.setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) {
                    String e = Engine.get(c).checkMaster(pw.getText().toString(), "Sperrbildschirm");
                    if (e != null) { err.setText(e); pw.setText(""); return; }
                    hideKeyboard(pw);
                    parentMenu();
                }
            });
            pw.requestFocus();
            H.postDelayed(new Runnable() {
                public void run() {
                    try { ((InputMethodManager) c.getSystemService(Context.INPUT_METHOD_SERVICE)).showSoftInput(pw, InputMethodManager.SHOW_IMPLICIT); } catch (Exception ignored) {}
                }
            }, 200);
        }

        void parentMenu() {
            panel.removeAllViews();
            LinearLayout card = card();
            card.addView(Ui.text(c, "👪 Eltern-Bereich", 18, p.text, true));
            final Engine eng = Engine.get(c);
            int[] mins = {15, 30, 60, 120};
            LinearLayout r = Ui.row(c);
            for (int i = 0; i < mins.length; i++) {
                final int m = mins[i];
                Button b = Ui.button(c, m >= 60 ? (m / 60) + " h" : m + " min", p, true);
                b.setOnClickListener(new View.OnClickListener() {
                    public void onClick(View v) {
                        eng.unlockFor(m);
                        hideLock();
                        closePopup();
                        GuardService.kick(c);
                    }
                });
                r.addView(Ui.weight(b, c, i == 0 ? 0 : 6));
            }
            card.addView(Ui.text(c, "Handy freigeben für …", 13, p.muted, false), Ui.lp(c, 10));
            card.addView(r, Ui.lp(c, 6));
            Button set = Ui.button(c, "⚙ Einstellungen 10 Minuten freigeben", p, false);
            set.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { eng.unlockSettings(10); closePopup(); } });
            card.addView(set, Ui.lp(c, 12));
            Button open = Ui.button(c, "📱 Familien-App öffnen", p, false);
            open.setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) {
                    eng.unlockSettings(10);
                    hideLock();
                    closePopup();
                    Intent i = new Intent(c, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK).putExtra("parent", true);
                    c.startActivity(i);
                }
            });
            card.addView(open, Ui.lp(c, 10));
            Button close = Ui.button(c, "Schließen", p, false);
            close.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { panel.removeAllViews(); buttons.setVisibility(View.VISIBLE); } });
            card.addView(close, Ui.lp(c, 10));
            panel.addView(card);
        }

        /** Anfrage stellen: Zeit (15/30/60) oder App/Webseite */
        void requestPanel(final String kind, final String domain, final String label, final TextView status) {
            panel.removeAllViews();
            buttons.setVisibility(View.GONE);
            LinearLayout card = card();
            final Engine eng = Engine.get(c);
            card.addView(Ui.text(c, kind.equals("time") ? "Wie viel Zeit brauchst du?" : "Warum brauchst du " + label + "?", 16, p.text, true));
            final int[] chosen = {30};
            final LinearLayout r = Ui.row(c);
            if (kind.equals("time")) {
                final int[] mins = {15, 30, 60};
                for (int i = 0; i < mins.length; i++) {
                    final int m = mins[i];
                    final Button b = Ui.button(c, "+" + m + " min", p, m == 30);
                    b.setOnClickListener(new View.OnClickListener() {
                        public void onClick(View v) {
                            chosen[0] = m;
                            for (int k = 0; k < r.getChildCount(); k++) {
                                Button x = (Button) r.getChildAt(k);
                                boolean on = x == b;
                                x.setTextColor(on ? p.btnText : p.text);
                                x.setBackground(Ui.round(on ? p.acc : p.btn2, Ui.dp(c, p.radius), on ? p.acc : p.border, Ui.dp(c, 1.5f)));
                            }
                        }
                    });
                    r.addView(Ui.weight(b, c, i == 0 ? 0 : 6));
                }
                card.addView(r, Ui.lp(c, 10));
            }
            final EditText reason = Ui.input(c, "Grund (optional), z. B. Hausaufgaben", p, false);
            card.addView(reason, Ui.lp(c, 10));
            LinearLayout br = Ui.row(c);
            Button back = Ui.button(c, "Zurück", p, false);
            Button send = Ui.button(c, "Senden", p, true);
            br.addView(Ui.weight(back, c, 0));
            br.addView(Ui.weight(send, c, 10));
            card.addView(br, Ui.lp(c, 12));
            panel.addView(card);
            back.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { hideKeyboard(reason); panel.removeAllViews(); buttons.setVisibility(View.VISIBLE); } });
            send.setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) {
                    hideKeyboard(reason);
                    String e = eng.addRequest(kind, kind.equals("time") ? chosen[0] : 0, reason.getText().toString(), domain, label);
                    status.setText(e.length() > 0 ? e : "📨 Anfrage gesendet – Mama oder Papa bekommen eine Nachricht aufs Handy.");
                    panel.removeAllViews();
                    buttons.setVisibility(View.VISIBLE);
                    GuardService.kick(c);
                }
            });
        }

        LinearLayout card() {
            LinearLayout card = Ui.col(c);
            int pd = Ui.dp(c, 16);
            card.setPadding(pd, pd, pd, pd);
            card.setBackground(Ui.round(p.card, Ui.dp(c, p.radius), p.acc, Ui.dp(c, 1.5f)));
            return card;
        }
    }
}
