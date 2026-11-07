package de.mstrassenit.familycontrol;

import android.content.Context;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.BufferedInputStream;
import java.io.BufferedOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;
import java.util.zip.GZIPInputStream;

/**
 * Kategorie-Filterlisten (Porno, Betrug, Phishing …) vom eigenen Server.
 * Gespeichert als sortierte 64-Bit-Hashes (FNV-1a) → ca. 8 Byte je Domain statt ~100.
 */
public final class FilterStore {
    private final File dir;
    private final Map<String, long[]> lists = new HashMap<>();
    private final Map<String, String> names = new HashMap<>();
    private volatile boolean busy;

    FilterStore(Context c) {
        dir = new File(c.getFilesDir(), "filters");
        dir.mkdirs();
        loadAll();
    }

    public static long fnv(String s) {
        long h = 0xcbf29ce484222325L;
        byte[] b = s.getBytes(StandardCharsets.UTF_8);
        for (byte x : b) { h ^= (x & 0xff); h *= 0x100000001b3L; }
        return h;
    }

    private synchronized void loadAll() {
        File[] fs = dir.listFiles();
        if (fs == null) return;
        for (File f : fs) {
            if (!f.getName().endsWith(".bin")) continue;
            String k = f.getName().substring(0, f.getName().length() - 4);
            try {
                DataInputStream in = new DataInputStream(new BufferedInputStream(new FileInputStream(f), 65536));
                int n = in.readInt();
                long[] a = new long[n];
                for (int i = 0; i < n; i++) a[i] = in.readLong();
                in.close();
                lists.put(k, a);
                File nf = new File(dir, k + ".name");
                if (nf.exists()) names.put(k, new String(Net.readAll(new FileInputStream(nf)), StandardCharsets.UTF_8));
            } catch (Exception e) {
                f.delete();
            }
        }
    }

    String localHash(String k) {
        try {
            File f = new File(dir, k + ".hash");
            return f.exists() ? new String(Net.readAll(new FileInputStream(f)), StandardCharsets.UTF_8) : "";
        } catch (Exception e) { return ""; }
    }

    /** Liste der aktiven Filter vom Server abgleichen (Hintergrund-Thread) */
    public void sync(JSONArray wanted, String server, String token) {
        if (busy || wanted == null) return;
        busy = true;
        try {
            Set<String> keep = new HashSet<>();
            for (int i = 0; i < wanted.length(); i++) {
                JSONObject f = wanted.optJSONObject(i);
                if (f == null) continue;
                String k = f.optString("key").replaceAll("[^a-z]", "");
                if (k.length() == 0) continue;
                keep.add(k);
                synchronized (this) { names.put(k, f.optString("name", k)); }
                if (f.optString("hash").equals(localHash(k)) && lists.containsKey(k)) continue;
                download(k, f.optString("hash"), f.optString("name", k), server, token);
            }
            synchronized (this) {
                for (String k : new HashSet<>(lists.keySet())) {
                    if (!keep.contains(k)) {
                        lists.remove(k);
                        new File(dir, k + ".bin").delete();
                        new File(dir, k + ".hash").delete();
                        new File(dir, k + ".name").delete();
                    }
                }
            }
        } finally {
            busy = false;
        }
    }

    private void download(String k, String hash, String name, String server, String token) {
        HttpURLConnection c = null;
        try {
            c = Net.openFilter(server, k, token);
            BufferedReader r = new BufferedReader(new InputStreamReader(new GZIPInputStream(c.getInputStream(), 65536), StandardCharsets.UTF_8), 65536);
            long[] a = new long[1 << 16];
            int n = 0;
            String line;
            while ((line = r.readLine()) != null) {
                line = line.trim().toLowerCase();
                if (line.length() < 4 || line.charAt(0) == '#') continue;
                if (n == a.length) a = Arrays.copyOf(a, a.length * 2);
                a[n++] = fnv(line);
            }
            r.close();
            a = Arrays.copyOf(a, n);
            Arrays.sort(a);
            File tmp = new File(dir, k + ".tmp");
            DataOutputStream out = new DataOutputStream(new BufferedOutputStream(new FileOutputStream(tmp), 65536));
            out.writeInt(n);
            for (long x : a) out.writeLong(x);
            out.close();
            tmp.renameTo(new File(dir, k + ".bin"));
            FileOutputStream h = new FileOutputStream(new File(dir, k + ".hash"));
            h.write(hash.getBytes(StandardCharsets.UTF_8));
            h.close();
            FileOutputStream nm = new FileOutputStream(new File(dir, k + ".name"));
            nm.write(name.getBytes(StandardCharsets.UTF_8));
            nm.close();
            synchronized (this) { lists.put(k, a); }
        } catch (Exception ignored) {
        } finally {
            if (c != null) c.disconnect();
        }
    }

    /** Liefert den Listen-Schlüssel, falls host oder eine übergeordnete Domain gelistet ist */
    public synchronized String match(String host) {
        if (lists.isEmpty()) return null;
        String h = host;
        while (true) {
            long x = fnv(h);
            for (Map.Entry<String, long[]> e : lists.entrySet()) if (Arrays.binarySearch(e.getValue(), x) >= 0) return e.getKey();
            int dot = h.indexOf('.');
            if (dot < 0 || h.indexOf('.', dot + 1) < 0) return null; // nicht bis zur TLD
            h = h.substring(dot + 1);
        }
    }

    public synchronized String name(String k) { String n = names.get(k); return n == null ? k : n; }

    public synchronized int count() { int n = 0; for (long[] a : lists.values()) n += a.length; return n; }

    public synchronized void clear() {
        lists.clear();
        File[] fs = dir.listFiles();
        if (fs != null) for (File f : fs) f.delete();
    }
}
