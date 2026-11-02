package com.casadababa.app;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import javax.net.ssl.HttpsURLConnection;
import org.json.JSONObject;

final class SessionHttpClient {
    static final String ORIGIN = "https://api.easystok.online";

    static final class Failure extends Exception {
        final int status;
        final String code;
        final JSONObject details;
        Failure(int status, String code, String message) { this(status, code, message, new JSONObject()); }
        Failure(int status, String code, String message, JSONObject details) {
            super(message); this.status = status; this.code = code; this.details = details;
        }
    }

    JSONObject request(String path, JSONObject body, String token) throws Exception {
        return request(path, body, token, null);
    }

    JSONObject request(String path, JSONObject body, String token, String deviceKey) throws Exception {
        HttpsURLConnection connection = (HttpsURLConnection) new URL(ORIGIN + path).openConnection();
        connection.setInstanceFollowRedirects(false);
        connection.setConnectTimeout(10000);
        connection.setReadTimeout(10000);
        connection.setRequestProperty("Accept", "application/json");
        if (token != null) connection.setRequestProperty("Authorization", "Bearer " + token);
        if (deviceKey != null) connection.setRequestProperty("X-Mobile-Api-Key", deviceKey);
        try {
            if (body != null) {
                connection.setRequestMethod("POST");
                connection.setRequestProperty("Content-Type", "application/json; charset=UTF-8");
                connection.setDoOutput(true);
                try (OutputStream output = connection.getOutputStream()) { output.write(body.toString().getBytes(StandardCharsets.UTF_8)); }
            }
            int status = connection.getResponseCode();
            InputStream input = status >= 200 && status < 300 ? connection.getInputStream() : connection.getErrorStream();
            JSONObject result = new JSONObject();
            if (input != null) {
                try (InputStream response = input; ByteArrayOutputStream output = new ByteArrayOutputStream()) {
                    byte[] buffer = new byte[8192]; int count;
                    while ((count = response.read(buffer)) != -1) {
                        if (output.size() + count > 524288) throw new Failure(502, "RESPOSTA_INVALIDA", "O serviço retornou uma resposta inválida.");
                        output.write(buffer, 0, count);
                    }
                    String text = output.toString(StandardCharsets.UTF_8.name());
                    if (!text.trim().isEmpty()) {
                        try { result = new JSONObject(text); }
                        catch (Exception invalid) { throw new Failure(status, "RESPOSTA_INVALIDA", "O serviço não respondeu como esperado. Tente de novo."); }
                    }
                }
            }
            if (status < 200 || status >= 300) {
                JSONObject error = result.optJSONObject("error");
                String message = error == null ? result.optString("error", "Não foi possível concluir. Tente de novo.")
                    : error.optString("message", "Não foi possível concluir. Tente de novo.");
                if (status >= 500 || message.length() > 400) message = "O EasyStok não respondeu. Tente de novo com conexão.";
                throw new Failure(status, error == null ? result.optString("code", "API_FALHOU") : error.optString("code", "API_FALHOU"), message, error == null ? result : error);
            }
            return result.optJSONObject("data") != null ? result.getJSONObject("data") : result;
        } finally { connection.disconnect(); }
    }
}
