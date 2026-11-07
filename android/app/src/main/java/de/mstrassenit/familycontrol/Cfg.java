package de.mstrassenit.familycontrol;

import android.content.Context;
import android.content.SharedPreferences;
import android.util.Base64;

import java.security.SecureRandom;

import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.PBEKeySpec;

/** Gespeicherte Einrichtung: Server, Geräte-Token, Masterpasswort (PBKDF2-SHA256 wie Server/Windows-App). */
public final class Cfg {
    public static final String VERSION = BuildInfo.VERSION;

    private static Cfg inst;
    private final SharedPreferences sp;

    public String server = "", token = "", childName = "", deviceName = "";
    public int deviceId;
    public String masterSalt = "", masterHash = "";
    public int masterIter = 120000, masterRev;
    public boolean setupDone, removed;

    public static synchronized Cfg get(Context c) {
        if (inst == null) inst = new Cfg(c.getApplicationContext());
        return inst;
    }

    private Cfg(Context c) {
        sp = c.getSharedPreferences("msfc", Context.MODE_PRIVATE);
        load();
    }

    public synchronized void load() {
        server = sp.getString("server", "");
        token = sp.getString("token", "");
        childName = sp.getString("child", "");
        deviceName = sp.getString("device_name", "");
        deviceId = sp.getInt("device_id", 0);
        masterSalt = sp.getString("m_salt", "");
        masterHash = sp.getString("m_hash", "");
        masterIter = sp.getInt("m_iter", 120000);
        masterRev = sp.getInt("m_rev", 0);
        setupDone = sp.getBoolean("setup_done", false);
        removed = sp.getBoolean("removed", false);
    }

    public synchronized void save() {
        sp.edit()
                .putString("server", server).putString("token", token).putString("child", childName)
                .putString("device_name", deviceName).putInt("device_id", deviceId)
                .putString("m_salt", masterSalt).putString("m_hash", masterHash).putInt("m_iter", masterIter).putInt("m_rev", masterRev)
                .putBoolean("setup_done", setupDone).putBoolean("removed", removed)
                .commit();
    }

    public boolean paired() { return token != null && token.length() == 64 && server.length() > 0; }

    /** Schutz aktiv = eingerichtet, gekoppelt und nicht von den Eltern entfernt */
    public boolean active() { return setupDone && paired() && !removed; }

    public synchronized void setMaster(String password) {
        byte[] salt = new byte[16];
        new SecureRandom().nextBytes(salt);
        masterIter = 120000;
        masterSalt = Base64.encodeToString(salt, Base64.NO_WRAP);
        masterHash = Base64.encodeToString(pbkdf2(password, salt, masterIter), Base64.NO_WRAP);
    }

    public boolean hasMaster() { return masterHash != null && masterHash.length() > 10; }

    public boolean checkMaster(String password) {
        if (!hasMaster() || password == null || password.length() == 0) return false;
        try {
            byte[] salt = Base64.decode(masterSalt, Base64.DEFAULT);
            byte[] h = pbkdf2(password, salt, masterIter);
            byte[] want = Base64.decode(masterHash, Base64.DEFAULT);
            if (h.length != want.length) return false;
            int diff = 0;
            for (int i = 0; i < h.length; i++) diff |= h[i] ^ want[i];
            return diff == 0;
        } catch (Exception e) {
            return false;
        }
    }

    static byte[] pbkdf2(String password, byte[] salt, int iter) {
        try {
            PBEKeySpec spec = new PBEKeySpec(password.toCharArray(), salt, iter, 256);
            return SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(spec).getEncoded();
        } catch (Exception e) {
            throw new RuntimeException(e);
        }
    }

    /** "192.168.1.5:8080" → "http://192.168.1.5:8080" (ohne / am Ende) */
    public static String normServer(String s) {
        s = s == null ? "" : s.trim();
        while (s.endsWith("/")) s = s.substring(0, s.length() - 1);
        if (s.endsWith("/index.php")) s = s.substring(0, s.length() - 10);
        if (s.length() > 0 && !s.startsWith("http://") && !s.startsWith("https://")) s = "http://" + s;
        return s;
    }
}
