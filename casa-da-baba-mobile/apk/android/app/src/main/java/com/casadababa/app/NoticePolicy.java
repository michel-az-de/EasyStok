package com.casadababa.app;

/** Avisos públicos, sem URLs executáveis nem dados de clientes. */
public final class NoticePolicy {
    public static final String TOPIC = "casa-da-baba-piloto-notices-v1";
    public static final String CHANNEL = "cdb-operacao";
    public static final String ACTION = "com.casadababa.app.OPEN_NOTICE";
    private NoticePolicy() {}
    public static boolean shouldRequestPermission(int sdk, boolean granted, boolean previouslyRequested) {
        return sdk >= 33 && !granted && !previouslyRequested;
    }
    public static boolean screenAllowed(String screen) {
        return "migration-guide".equals(screen) || "home".equals(screen);
    }
    public static boolean accepts(String sender, String kind, String id, String screen, String title, String body) {
        return ("/topics/" + TOPIC).equals(sender) && "app-notice".equals(kind)
            && id != null && id.matches("[a-zA-Z0-9_-]{1,64}") && screenAllowed(screen)
            && title != null && !title.trim().isEmpty() && title.length() <= 100
            && body != null && !body.trim().isEmpty() && body.length() <= 1200;
    }
}
