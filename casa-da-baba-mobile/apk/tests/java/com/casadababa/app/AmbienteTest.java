package com.casadababa.app;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.URL;
import java.net.URLConnection;
import java.net.URLStreamHandler;
import java.nio.charset.StandardCharsets;
import java.security.cert.Certificate;
import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;
import javax.net.ssl.HttpsURLConnection;
import org.json.JSONObject;

/** #3114 (B9): login e pareamento do APK de homologação não abrem conexão com a produção. */
public final class AmbienteTest {
    private static final List<String> abertas = new CopyOnWriteArrayList<>();
    private static int checks;

    public static void main(String[] args) throws Exception {
        instalarRede();
        Ambiente oficial = Ambiente.ATUAL;
        check("oficial".equals(oficial.nome()) && oficial.distribuicaoOficial(), "harness roda como a variante oficial");
        check("https://api.easystok.online".equals(oficial.apiOrigin()) && "easystok.online".equals(oficial.downloadsHost()),
            "oficial segue com as origens de sempre");

        Ambiente hml = Ambiente.de("homologacao", "https://api-teste.example", "https://console-teste.example", "");
        check("console-teste.example".equals(hml.appHost()), "Workspace da homologação abre o Console de teste");
        check(!hml.distribuicaoOficial() && hml.paginaDownloads() == null, "homologação sem OTA, push e página de download");

        // Pedido real pelo cliente de sessão: a única conexão aberta é a da API de teste.
        JSONObject resposta = new SessionHttpClient(hml).request("/api/mobile/pairing", new JSONObject().put("code", "123456"), null);
        check("ok".equals(resposta.optString("status")), "pareamento da homologação recebe a resposta da API de teste");
        new SessionHttpClient(hml).request("/api/mobile/session", null, "token-teste");
        check(abertas.equals(List.of("https://api-teste.example/api/mobile/pairing", "https://api-teste.example/api/mobile/session")),
            "login e pareamento da homologação abrem só a API de teste: " + abertas);
        abertas.clear();
        new SessionHttpClient().request("/api/mobile/session", null, null);
        check(abertas.equals(List.of("https://api.easystok.online/api/mobile/session")), "cliente padrão segue o ambiente do build");

        String[] producao = { "https://api.easystok.online", "https://API.EasyStok.Online", "https://easystok.online",
            "https://x.app.easystok.online", "https://casadababa.com", "https://www.casadababa.com",
            "https://api.easystok.online.", "https://casadababa.com." };
        for (String origem : producao) {
            recusa(origem, "https://console-teste.example", "");
            recusa("https://api-teste.example", origem, "");
        }
        String[] invalidas = { null, "", "http://api-teste.example", "https://u@api-teste.example", "https://api-teste.example/",
            "https://api-teste.example/api", "https://api-teste.example?x=1", "https://api-teste.example:8443", "https://localhost", "ftp://api-teste.example" };
        for (String origem : invalidas) recusa(origem, "https://console-teste.example", "");
        recusa("https://api-teste.example", "https://console-teste.example", "https://downloads-teste.example");
        try { Ambiente.de("producao", "https://api-teste.example", "https://console-teste.example", ""); throw new AssertionError("ambiente desconhecido aceito"); }
        catch (IllegalStateException expected) { checks++; }
        System.out.println("PASS: " + checks + " asserções do ambiente; homologação sem caminho para a produção");
    }

    private static void recusa(String api, String app, String downloads) {
        try { Ambiente.de("homologacao", api, app, downloads); }
        catch (IllegalStateException expected) { checks++; return; }
        throw new AssertionError("homologação aceitou " + api + " / " + app + " / " + downloads);
    }

    /** Toda conexão do processo passa por aqui; nada sai para a rede. */
    private static void instalarRede() {
        URL.setURLStreamHandlerFactory(protocol -> new URLStreamHandler() {
            @Override protected URLConnection openConnection(URL url) {
                abertas.add(url.toString());
                return new HttpsURLConnection(url) {
                    private final ByteArrayOutputStream corpo = new ByteArrayOutputStream();
                    public void disconnect() {}
                    public boolean usingProxy() { return false; }
                    public void connect() {}
                    public String getCipherSuite() { return "HOST_TEST"; }
                    public Certificate[] getLocalCertificates() { return null; }
                    public Certificate[] getServerCertificates() { return null; }
                    @Override public OutputStream getOutputStream() { return corpo; }
                    @Override public int getResponseCode() { return 200; }
                    @Override public InputStream getInputStream() {
                        return new ByteArrayInputStream("{\"data\":{\"status\":\"ok\"}}".getBytes(StandardCharsets.UTF_8));
                    }
                };
            }
        });
    }

    private static void check(boolean valid, String message) {
        if (!valid) throw new AssertionError(message);
        checks++;
    }
}
