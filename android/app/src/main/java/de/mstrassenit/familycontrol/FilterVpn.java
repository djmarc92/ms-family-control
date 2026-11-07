package de.mstrassenit.familycontrol;

import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.net.ConnectivityManager;
import android.net.LinkProperties;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.VpnService;
import android.os.ParcelFileDescriptor;

import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.Inet4Address;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.regex.Pattern;

/**
 * Webfilter als lokales „VPN“: Es wird NUR der DNS-Verkehr über die App geleitet (kein fremder Server).
 * Gesperrte Domains bekommen „gibt es nicht“, Google/Bing/DuckDuckGo/YouTube werden auf die sicheren Varianten umgelenkt.
 */
public class FilterVpn extends VpnService {
    static final String VPN_ADDR = "10.111.222.1";
    static final String DNS_ADDR = "10.111.222.2";
    static volatile boolean running;
    static volatile long lastBlockAt;

    private ParcelFileDescriptor tun;
    private Thread reader;
    private ExecutorService pool;
    private FileOutputStream out;
    private volatile boolean stop;

    public static void start(Context c) {
        if (running) return;
        if (VpnService.prepare(c) != null) return; // Zustimmung fehlt (Einrichtung)
        try { c.startService(new Intent(c, FilterVpn.class)); } catch (Exception ignored) {}
    }

    public static void stop(Context c) {
        try { c.startService(new Intent(c, FilterVpn.class).setAction("stop")); } catch (Exception ignored) {}
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null && "stop".equals(intent.getAction())) {
            shutdown();
            stopSelf();
            return START_NOT_STICKY;
        }
        if (tun == null) establish();
        return START_STICKY;
    }

    private void establish() {
        try {
            Builder b = new Builder();
            b.setSession("MS Family Control – Webfilter");
            b.addAddress(VPN_ADDR, 32);
            b.addDnsServer(DNS_ADDR);
            b.addRoute(DNS_ADDR, 32); // nur DNS läuft hier durch, alles andere direkt
            b.setBlocking(true);
            b.setMtu(1500);
            try { b.addDisallowedApplication(getPackageName()); } catch (Exception ignored) {}
            Intent i = new Intent(this, MainActivity.class);
            b.setConfigureIntent(PendingIntent.getActivity(this, 1, i, PendingIntent.FLAG_IMMUTABLE));
            tun = b.establish();
            if (tun == null) return;
            out = new FileOutputStream(tun.getFileDescriptor());
            pool = Executors.newFixedThreadPool(6);
            stop = false;
            running = true;
            reader = new Thread(new Runnable() { public void run() { loop(); } }, "msfc-dns");
            reader.start();
        } catch (Exception e) {
            running = false;
        }
    }

    private void loop() {
        try {
            FileInputStream in = new FileInputStream(tun.getFileDescriptor());
            byte[] buf = new byte[32767];
            while (!stop) {
                int n = in.read(buf);
                if (n <= 0) continue;
                final byte[] pkt = new byte[n];
                System.arraycopy(buf, 0, pkt, 0, n);
                pool.execute(new Runnable() { public void run() { handle(pkt); } });
            }
        } catch (Exception ignored) {
        }
        running = false;
    }

    // ---------- Paketverarbeitung ----------
    private void handle(byte[] p) {
        try {
            if ((p[0] >> 4) != 4) return;              // nur IPv4
            int ihl = (p[0] & 0x0f) * 4;
            if ((p[9] & 0xff) != 17) return;            // nur UDP
            int dport = ((p[ihl + 2] & 0xff) << 8) | (p[ihl + 3] & 0xff);
            if (dport != 53) return;
            int udpLen = ((p[ihl + 4] & 0xff) << 8) | (p[ihl + 5] & 0xff);
            int dnsOff = ihl + 8, dnsLen = Math.min(udpLen - 8, p.length - dnsOff);
            if (dnsLen < 12) return;
            byte[] q = new byte[dnsLen];
            System.arraycopy(p, dnsOff, q, 0, dnsLen);
            byte[] answer = resolve(q);
            if (answer != null) write(p, ihl, answer);
        } catch (Exception ignored) {
        }
    }

    private byte[] resolve(byte[] q) throws Exception {
        int[] pos = {12};
        String name = readName(q, pos);
        if (name == null) return forward(q);
        int qtype = ((q[pos[0]] & 0xff) << 8) | (q[pos[0] + 1] & 0xff);
        int qEnd = pos[0] + 4;
        String host = name.toLowerCase(Locale.ROOT);
        if (host.endsWith(".")) host = host.substring(0, host.length() - 1);

        Engine eng = Engine.get(this);
        if (Cfg.get(this).active() && !eng.settingsUnlocked()) {
            // 1) Eltern-Sperrliste / Freigaben
            String[] site = eng.checkHost(host);
            String cat = null;
            if (site == null && !isAllowedOrGranted(eng, host)) cat = eng.filters.match(host);
            if (site != null || cat != null) {
                GuardService.onSiteBlocked(this, site != null ? site[1] : host, cat);
                return nx(q, qEnd);
            }
            // 2) Sichere Suche
            String target = safeTarget(eng, host);
            if (target != null) {
                if (qtype == 28) return empty(q, qEnd);     // AAAA → keine IPv6-Antwort, nimmt IPv4
                if (qtype == 1) {
                    List<byte[]> ips = lookupA(target);
                    if (!ips.isEmpty()) return aAnswer(q, qEnd, ips);
                }
            }
        }
        return forward(q);
    }

    private boolean isAllowedOrGranted(Engine eng, String host) {
        // checkHost liefert null auch bei Freigabe – hier nur prüfen, ob eine Freigabe/Allowlist greift
        org.json.JSONObject pol = eng.policy();
        if (pol == null) return false;
        org.json.JSONArray al = pol.optJSONArray("allow_sites");
        if (al != null) for (int i = 0; i < al.length(); i++) if (Engine.matches(host, al.optString(i))) return true;
        org.json.JSONArray gr = pol.optJSONArray("site_grants");
        long now = eng.now();
        if (gr != null) for (int i = 0; i < gr.length(); i++) {
            org.json.JSONObject g = gr.optJSONObject(i);
            if (g != null && Engine.parseDT(g.optString("until_at", g.optString("until", ""))) > now && Engine.matches(host, g.optString("domain"))) return true;
        }
        return false;
    }

    static final Pattern GOOGLE = Pattern.compile("^(www\\.)?google\\.[a-z]{2,3}(\\.[a-z]{2})?$");

    private String safeTarget(Engine eng, String host) {
        if (eng.safeSearch()) {
            if (GOOGLE.matcher(host).matches()) return "forcesafesearch.google.com";
            if (host.equals("www.bing.com") || host.equals("bing.com")) return "strict.bing.com";
            if (host.equals("duckduckgo.com") || host.equals("www.duckduckgo.com") || host.equals("start.duckduckgo.com")) return "safe.duckduckgo.com";
        }
        int yt = eng.youtubeRestrict();
        if (yt > 0 && (host.equals("www.youtube.com") || host.equals("m.youtube.com") || host.equals("youtube.com") || host.equals("youtubei.googleapis.com")
                || host.equals("youtube.googleapis.com") || host.equals("www.youtube-nocookie.com")))
            return yt >= 2 ? "restrict.youtube.com" : "restrictmoderate.youtube.com";
        return null;
    }

    // ---------- DNS-Hilfen ----------
    static String readName(byte[] d, int[] pos) {
        StringBuilder sb = new StringBuilder();
        int p = pos[0], guard = 0;
        while (p < d.length && guard++ < 64) {
            int len = d[p] & 0xff;
            if (len == 0) { p++; pos[0] = p; return sb.length() == 0 ? "." : sb.toString(); }
            if ((len & 0xc0) != 0) return null; // Komprimierung in Fragen unüblich
            if (p + 1 + len > d.length) return null;
            if (sb.length() > 0) sb.append('.');
            sb.append(new String(d, p + 1, len, StandardCharsets.US_ASCII));
            p += 1 + len;
        }
        return null;
    }

    private static byte[] header(byte[] q, int qEnd, int rcode, int ancount) {
        byte[] r = new byte[qEnd];
        System.arraycopy(q, 0, r, 0, qEnd);
        r[2] = (byte) (0x80 | (q[2] & 0x01)); // QR, RD übernehmen
        r[3] = (byte) (0x80 | rcode);          // RA + Fehlercode
        r[4] = 0; r[5] = 1;                    // 1 Frage
        r[6] = (byte) (ancount >> 8); r[7] = (byte) ancount;
        r[8] = 0; r[9] = 0; r[10] = 0; r[11] = 0;
        return r;
    }

    static byte[] nx(byte[] q, int qEnd) { return header(q, qEnd, 3, 0); }
    static byte[] empty(byte[] q, int qEnd) { return header(q, qEnd, 0, 0); }

    static byte[] aAnswer(byte[] q, int qEnd, List<byte[]> ips) {
        byte[] h = header(q, qEnd, 0, ips.size());
        byte[] r = new byte[h.length + ips.size() * 16];
        System.arraycopy(h, 0, r, 0, h.length);
        int p = h.length;
        for (byte[] ip : ips) {
            r[p++] = (byte) 0xc0; r[p++] = 0x0c;        // Name → Frage
            r[p++] = 0; r[p++] = 1;                     // Typ A
            r[p++] = 0; r[p++] = 1;                     // Klasse IN
            r[p++] = 0; r[p++] = 0; r[p++] = 0x01; r[p++] = 0x2c; // TTL 300
            r[p++] = 0; r[p++] = 4;
            System.arraycopy(ip, 0, r, p, 4); p += 4;
        }
        return r;
    }

    private List<byte[]> lookupA(String host) {
        List<byte[]> l = new ArrayList<>();
        try {
            for (InetAddress a : getUnderlyingNetwork() != null ? getUnderlyingNetwork().getAllByName(host) : InetAddress.getAllByName(host))
                if (a instanceof Inet4Address) l.add(a.getAddress());
        } catch (Exception ignored) {}
        return l;
    }

    // ---------- Weiterleiten an den echten DNS-Server ----------
    private Network getUnderlyingNetwork() {
        try {
            ConnectivityManager cm = (ConnectivityManager) getSystemService(CONNECTIVITY_SERVICE);
            for (Network n : cm.getAllNetworks()) {
                NetworkCapabilities nc = cm.getNetworkCapabilities(n);
                if (nc != null && !nc.hasTransport(NetworkCapabilities.TRANSPORT_VPN) && nc.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
                        && nc.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)) return n;
            }
        } catch (Exception ignored) {}
        return null;
    }

    private List<InetAddress> upstream() {
        List<InetAddress> l = new ArrayList<>();
        try {
            ConnectivityManager cm = (ConnectivityManager) getSystemService(CONNECTIVITY_SERVICE);
            Network n = getUnderlyingNetwork();
            if (n != null) {
                LinkProperties lp = cm.getLinkProperties(n);
                if (lp != null) for (InetAddress a : lp.getDnsServers()) if (a instanceof Inet4Address) l.add(a);
            }
        } catch (Exception ignored) {}
        try {
            l.add(InetAddress.getByName("1.1.1.1"));
            l.add(InetAddress.getByName("8.8.8.8"));
        } catch (Exception ignored) {}
        return l;
    }

    private byte[] forward(byte[] q) {
        for (InetAddress dns : upstream()) {
            DatagramSocket s = null;
            try {
                s = new DatagramSocket();
                protect(s);
                s.setSoTimeout(4000);
                s.send(new DatagramPacket(q, q.length, dns, 53));
                byte[] buf = new byte[4096];
                DatagramPacket resp = new DatagramPacket(buf, buf.length);
                s.receive(resp);
                byte[] r = new byte[resp.getLength()];
                System.arraycopy(buf, 0, r, 0, r.length);
                return r;
            } catch (Exception ignored) {
            } finally {
                if (s != null) s.close();
            }
        }
        return null;
    }

    // ---------- Antwortpaket bauen (IPv4/UDP, Quelle und Ziel getauscht) ----------
    private void write(byte[] req, int ihl, byte[] dns) throws Exception {
        int total = 20 + 8 + dns.length;
        byte[] p = new byte[total];
        p[0] = 0x45; p[1] = 0;
        p[2] = (byte) (total >> 8); p[3] = (byte) total;
        p[4] = req[4]; p[5] = req[5];      // Kennung
        p[6] = 0x40; p[7] = 0;             // nicht fragmentieren
        p[8] = 64; p[9] = 17;
        System.arraycopy(req, 16, p, 12, 4); // Quelle = altes Ziel
        System.arraycopy(req, 12, p, 16, 4); // Ziel = alte Quelle
        int cs = checksum(p, 0, 20);
        p[10] = (byte) (cs >> 8); p[11] = (byte) cs;
        // UDP
        p[20] = req[ihl + 2]; p[21] = req[ihl + 3]; // Quellport = 53
        p[22] = req[ihl]; p[23] = req[ihl + 1];     // Zielport = alter Quellport
        int ul = 8 + dns.length;
        p[24] = (byte) (ul >> 8); p[25] = (byte) ul;
        p[26] = 0; p[27] = 0;                        // Prüfsumme optional (IPv4)
        System.arraycopy(dns, 0, p, 28, dns.length);
        synchronized (this) { out.write(p); }
    }

    static int checksum(byte[] b, int off, int len) {
        int sum = 0;
        for (int i = off; i < off + len; i += 2) sum += ((b[i] & 0xff) << 8) | (i + 1 < off + len ? (b[i + 1] & 0xff) : 0);
        while ((sum >> 16) != 0) sum = (sum & 0xffff) + (sum >> 16);
        return ~sum & 0xffff;
    }

    private void shutdown() {
        stop = true;
        running = false;
        try { if (tun != null) tun.close(); } catch (Exception ignored) {}
        tun = null;
        if (pool != null) pool.shutdownNow();
    }

    @Override
    public void onRevoke() {
        // Ein anderes VPN wurde gestartet oder der Filter in den Einstellungen getrennt
        Cfg cfg = Cfg.get(this);
        if (cfg.active()) Engine.get(this).addEvent("tamper", "Webfilter am Handy getrennt (VPN)", "Ein anderes VPN oder die Einstellungen haben den Filter beendet");
        shutdown();
        super.onRevoke();
    }

    @Override
    public void onDestroy() {
        shutdown();
        super.onDestroy();
    }
}
