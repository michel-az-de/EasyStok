package com.casadababa.app;

import java.util.Locale;
import java.util.regex.Pattern;

/**
 * Origens de rede do APK (#3114). Vêm do BuildConfig da variante: o oficial traz as da produção e a
 * homologação as de teste. Este arquivo não tem endereço, então o APK de homologação não carrega nenhum
 * da produção; a lista de recusa abaixo usa ponto escapado e não é endereço.
 */
public final class Ambiente {
    private static final Pattern ORIGEM = Pattern.compile("https://[a-z0-9]([a-z0-9-]*[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+");
    private static final Pattern PRODUCAO = Pattern.compile("https://(.+\\.)?(easystok\\.online|casadababa\\.com)");
    public static final Ambiente ATUAL = de(BuildConfig.CDB_AMBIENTE, BuildConfig.CDB_API_ORIGIN, BuildConfig.CDB_APP_ORIGIN, BuildConfig.CDB_DOWNLOADS_ORIGIN);

    private final String nome;
    private final String api;
    private final String app;
    private final String downloads;

    private Ambiente(String nome, String api, String app, String downloads) {
        this.nome = nome; this.api = api; this.app = app; this.downloads = downloads;
    }

    /** Falha fechada: origem inválida derruba a abertura do app em vez de cair em outro servidor. */
    public static Ambiente de(String nome, String api, String app, String downloads) {
        if ("oficial".equals(nome)) return new Ambiente(nome, origem(api), origem(app), origem(downloads));
        if (!"homologacao".equals(nome)) throw new IllegalStateException("Ambiente do APK desconhecido: " + nome);
        if (downloads != null && !downloads.isEmpty()) throw new IllegalStateException("A homologação não tem canal de distribuição.");
        return new Ambiente(nome, deTeste(api), deTeste(app), null);
    }

    private static String origem(String valor) {
        String normalizada = valor == null ? "" : valor.toLowerCase(Locale.ROOT);
        if (!ORIGEM.matcher(normalizada).matches())
            throw new IllegalStateException("Origem do APK precisa ser HTTPS sem caminho, porta ou usuário: " + valor);
        return normalizada;
    }

    private static String deTeste(String valor) {
        String normalizada = origem(valor);
        if (PRODUCAO.matcher(normalizada).matches()) throw new IllegalStateException("A homologação não pode falar com a produção: " + valor);
        return normalizada;
    }

    public String nome() { return nome; }
    public String apiOrigin() { return api; }
    public String appOrigin() { return app; }
    public String apiHost() { return api.substring("https://".length()); }
    public String appHost() { return app.substring("https://".length()); }
    /** OTA, push e página de download são canais da produção: só a variante oficial os usa. */
    public boolean distribuicaoOficial() { return downloads != null; }
    public String downloadsOrigin() { return downloads; }
    public String downloadsHost() { return downloads == null ? null : downloads.substring("https://".length()); }
    public String paginaDownloads() { return downloads == null ? null : downloads + "/downloads/casa-da-baba/"; }
}
