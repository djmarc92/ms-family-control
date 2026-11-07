package de.mstrassenit.familycontrol;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

/** Geräte-API (device.php) – gleiche Schnittstelle wie die Windows-App */
public final class Net {
    private Net() {}

    public static class ApiException extends Exception {
        public final int status;
        public ApiException(int status, String msg) { super(msg); this.status = status; }
    }

    public static JSONObject call(String server, String action, JSONObject body, String token, int timeoutMs) throws Exception {
        URL u = new URL(server + "/device.php?a=" + action);
        HttpURLConnection c = (HttpURLConnection) u.openConnection();
        c.setConnectTimeout(timeoutMs);
        c.setReadTimeout(timeoutMs);
        c.setRequestProperty("Accept", "application/json");
        c.setRequestProperty("User-Agent", "MSFamilyControl-Android/" + Cfg.VERSION);
        if (token != null && token.length() > 0) c.setRequestProperty("Authorization", "Bearer " + token);
        if (body != null) {
            byte[] data = body.toString().getBytes(StandardCharsets.UTF_8);
            c.setRequestMethod("POST");
            c.setDoOutput(true);
            c.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            c.setFixedLengthStreamingMode(data.length);
            OutputStream os = c.getOutputStream();
            os.write(data);
            os.close();
        }
        int code = c.getResponseCode();
        InputStream in = code >= 400 ? c.getErrorStream() : c.getInputStream();
        String txt = in == null ? "" : new String(readAll(in), StandardCharsets.UTF_8);
        c.disconnect();
        JSONObject j;
        try {
            j = new JSONObject(txt);
        } catch (Exception e) {
            if (code >= 400) throw new ApiException(code, "Server-Fehler " + code);
            throw new ApiException(code, "Ungültige Antwort vom Server – stimmt die Adresse?");
        }
        if (code >= 400 || !j.optBoolean("ok", true)) throw new ApiException(code, j.optString("error", "Fehler " + code));
        return j;
    }

    /** Filterliste (gzip) als Stream – Aufrufer schließt */
    public static HttpURLConnection openFilter(String server, String key, String token) throws Exception {
        URL u = new URL(server + "/device.php?a=filter&key=" + key);
        HttpURLConnection c = (HttpURLConnection) u.openConnection();
        c.setConnectTimeout(15000);
        c.setReadTimeout(60000);
        c.setRequestProperty("Authorization", "Bearer " + token);
        if (c.getResponseCode() != 200) {
            int code = c.getResponseCode();
            c.disconnect();
            throw new ApiException(code, "Filterliste " + key + ": " + code);
        }
        return c;
    }

    static byte[] readAll(InputStream in) throws Exception {
        ByteArrayOutputStream bo = new ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        int n;
        while ((n = in.read(buf)) > 0) bo.write(buf, 0, n);
        in.close();
        return bo.toByteArray();
    }
}
