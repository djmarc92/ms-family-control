package de.mstrassenit.familycontrol;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.text.InputType;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;

import org.json.JSONObject;

import java.io.InputStream;
import java.util.HashMap;
import java.util.Map;

/** Kleine UI-Helfer + Farbschemata der Kinder-Designs (wie am PC) */
public final class Ui {
    private Ui() {}

    public static int dp(Context c, float v) { return Math.round(TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v, c.getResources().getDisplayMetrics())); }

    public static GradientDrawable round(int color, float radiusPx, int strokeColor, int strokePx) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        g.setCornerRadius(radiusPx);
        if (strokePx > 0) g.setStroke(strokePx, strokeColor);
        return g;
    }

    public static TextView text(Context c, String s, float sp, int color, boolean bold) {
        TextView t = new TextView(c);
        t.setText(s);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp);
        t.setTextColor(color);
        if (bold) t.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        return t;
    }

    public static Button button(Context c, String s, Pal p, boolean primary) {
        Button b = new Button(c);
        b.setText(s);
        b.setAllCaps(false);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
        b.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        b.setTextColor(primary ? p.btnText : p.text);
        b.setBackground(round(primary ? p.acc : p.btn2, dp(c, p.radius), primary ? p.acc : p.border, dp(c, 1.5f)));
        b.setPadding(dp(c, 16), dp(c, 12), dp(c, 16), dp(c, 12));
        b.setMinHeight(dp(c, 52));
        return b;
    }

    public static EditText input(Context c, String hint, Pal p, boolean password) {
        EditText e = new EditText(c);
        e.setHint(hint);
        e.setSingleLine(true);
        e.setTextSize(TypedValue.COMPLEX_UNIT_SP, 17);
        e.setTextColor(p.text);
        e.setHintTextColor(p.muted);
        e.setBackground(round(p.input, dp(c, p.radius), p.border, dp(c, 1.5f)));
        e.setPadding(dp(c, 14), dp(c, 12), dp(c, 14), dp(c, 12));
        if (password) e.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        return e;
    }

    public static LinearLayout.LayoutParams lp(Context c, int topDp) {
        LinearLayout.LayoutParams l = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        l.topMargin = dp(c, topDp);
        return l;
    }

    public static LinearLayout col(Context c) {
        LinearLayout l = new LinearLayout(c);
        l.setOrientation(LinearLayout.VERTICAL);
        return l;
    }

    public static LinearLayout row(Context c) {
        LinearLayout l = new LinearLayout(c);
        l.setOrientation(LinearLayout.HORIZONTAL);
        l.setGravity(Gravity.CENTER_VERTICAL);
        return l;
    }

    public static View weight(View v, Context c, int leftDp) {
        LinearLayout.LayoutParams l = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        l.leftMargin = dp(c, leftDp);
        v.setLayoutParams(l);
        return v;
    }

    private static final Map<String, Bitmap> bmpCache = new HashMap<>();

    public static synchronized Bitmap asset(Context c, String name) {
        if (bmpCache.containsKey(name)) return bmpCache.get(name);
        Bitmap b = null;
        try {
            InputStream in = c.getAssets().open("img/" + name);
            b = BitmapFactory.decodeStream(in);
            in.close();
        } catch (Exception ignored) {}
        bmpCache.put(name, b);
        return b;
    }

    public static String dur(int sec) {
        int m = Math.max(0, sec) / 60;
        return m >= 60 ? (m / 60) + " h " + String.format(java.util.Locale.US, "%02d", m % 60) + " min" : m + " min";
    }

    /** Farbschema je Design */
    public static final class Pal {
        public String key;
        public int bg1, bg2, card, text, muted, acc, acc2, btn2, btnText, border, input;
        public float radius = 14;
        public boolean light, caps;

        public static Pal of(String theme, String accentHex) {
            Pal p = new Pal();
            p.key = theme == null ? "anime" : theme;
            switch (p.key) {
                case "neon":
                    p.bg1 = 0xFF03050F; p.bg2 = 0xFF0E1140; p.card = 0xD90A0F2A; p.text = 0xFFF2F6FF; p.muted = 0xFF9AA6D6;
                    p.acc = 0xFF00E5FF; p.acc2 = 0xFFA855F7; p.radius = 18; break;
                case "family":
                    p.bg1 = 0xFFE6F3FF; p.bg2 = 0xFFFFF3DC; p.card = 0xF7FFFFFF; p.text = 0xFF1D2B4F; p.muted = 0xFF5B6A8C;
                    p.acc = 0xFF2F80ED; p.acc2 = 0xFF27AE60; p.light = true; p.radius = 22; break;
                case "manga":
                    p.bg1 = 0xFF160626; p.bg2 = 0xFF4A0F60; p.card = 0xD9220A3A; p.text = 0xFFFFFFFF; p.muted = 0xFFE0B9F0;
                    p.acc = 0xFFFF4FD8; p.acc2 = 0xFFFFE14D; p.radius = 16; break;
                case "army":
                    p.bg1 = 0xFF121416; p.bg2 = 0xFF2E3326; p.card = 0xE61D2023; p.text = 0xFFF4F1E8; p.muted = 0xFFB7B09A;
                    p.acc = 0xFFF68C00; p.acc2 = 0xFFFFD23F; p.radius = 4; p.caps = true; break;
                default:
                    p.key = "anime";
                    p.bg1 = 0xFF060A1E; p.bg2 = 0xFF2A0F52; p.card = 0xD90C1837; p.text = 0xFFFFFFFF; p.muted = 0xFFA9B4E0;
                    p.acc = 0xFF1CE9FF; p.acc2 = 0xFFFF3FB4; p.radius = 14;
            }
            if (accentHex != null && accentHex.matches("^#[0-9a-fA-F]{6}$")) {
                try { p.acc = Color.parseColor(accentHex); } catch (Exception ignored) {}
            }
            p.btnText = p.light ? 0xFFFFFFFF : 0xFF06101E;
            if (p.key.equals("army")) p.btnText = 0xFF111111;
            p.btn2 = p.light ? 0xFFFFFFFF : 0x33FFFFFF;
            p.border = p.light ? 0xFFC9D8EE : 0x55FFFFFF;
            p.input = p.light ? 0xFFF4F8FF : 0x40000000;
            return p;
        }
    }

    /** Design + Texte für ein Ereignis aus der Server-Richtlinie (theme.events) */
    public static final class Spec {
        public Pal pal;
        public String title = "", text = "", theme = "anime";
        public boolean anim = true;

        public static Spec of(JSONObject policy, String ev) {
            Spec s = new Spec();
            JSONObject th = policy == null ? null : policy.optJSONObject("theme");
            String accent = "";
            if (th != null) {
                s.theme = th.optString("default", "anime");
                accent = th.optString("accent", "");
                s.anim = th.optBoolean("anim", true);
                JSONObject evs = th.optJSONObject("events");
                JSONObject e = evs == null ? null : evs.optJSONObject(ev);
                if (e != null) {
                    if (e.optString("theme").length() > 0) s.theme = e.optString("theme");
                    s.title = e.optString("title");
                    s.text = e.optString("text");
                    if (!e.optBoolean("custom", false)) s.text = pcToPhone(s.text);
                }
            }
            if (s.title.length() == 0) s.title = defTitle(ev);
            if (s.text.length() == 0) s.text = defText(ev);
            s.pal = Pal.of(s.theme, accent);
            return s;
        }

        public String fill(String t, String name, String until, String child) {
            return t.replace("{name}", name == null ? "" : name).replace("{until}", until == null ? "" : until).replace("{child}", child == null ? "" : child);
        }

        static String pcToPhone(String t) {
            return t.replace("an den PC", "ans Handy").replace("am PC", "am Handy").replace("den PC", "das Handy").replace("der PC", "das Handy").replace("PC", "Handy");
        }

        static String defTitle(String ev) {
            switch (ev) {
                case "time_expired": return "Ätsch Bätsch – Zeit is um!";
                case "bedtime": return "Zeit fürs Bett!";
                case "app_blocked": return "App gesperrt";
                case "game_blocked": return "Spiel gesperrt";
                case "website_blocked": return "Webseite gesperrt";
                case "parent_message": return "Nachricht von Mama & Papa";
                default: return "Gerade nicht erlaubt";
            }
        }

        static String defText(String ev) {
            switch (ev) {
                case "time_expired": return "Deine Bildschirmzeit ist für heute aufgebraucht.";
                case "bedtime": return "Gute Nacht! Morgen geht's weiter.";
                case "app_blocked": return "{name} ist gerade nicht erlaubt.";
                case "game_blocked": return "{name} ist gerade nicht erlaubt.";
                case "website_blocked": return "Diese Seite ist für dich gesperrt.";
                default: return "Das Handy ist gerade gesperrt.";
            }
        }
    }
}
