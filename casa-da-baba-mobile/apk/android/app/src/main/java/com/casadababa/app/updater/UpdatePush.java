package com.casadababa.app.updater;

import android.content.Context;
import android.content.SharedPreferences;
import android.os.SystemClock;
import com.getcapacitor.JSObject;
import com.google.firebase.messaging.FirebaseMessaging;

/** Only public update announcements use this topic. No customer or tenant data is sent. */
final class UpdatePush {
    static final String TOPIC = "casa-da-baba-piloto-updates";
    private static boolean subscribing;
    private static long lastAttempt;
    private UpdatePush() {}

    private static SharedPreferences preferences(Context context) {
        return context.getSharedPreferences("cdb-update-push", Context.MODE_PRIVATE);
    }

    static void subscribe(Context context, boolean newToken) {
        Context app = context.getApplicationContext();
        synchronized (UpdatePush.class) {
            if (newToken) preferences(app).edit().putBoolean("subscribed", false).apply();
            long now = SystemClock.elapsedRealtime();
            if (subscribing || (!newToken && (preferences(app).getBoolean("subscribed", false)
                    || (lastAttempt != 0 && now - lastAttempt < 300000)))) return;
            subscribing = true;
            lastAttempt = now;
        }
        try {
            FirebaseMessaging.getInstance().subscribeToTopic(TOPIC).addOnCompleteListener(task -> {
                synchronized (UpdatePush.class) { subscribing = false; }
                preferences(app).edit().putBoolean("subscribed", task.isSuccessful())
                    .putString("error", task.isSuccessful() ? "" : "Não foi possível registrar o push. Confira a conexão e o Google Play Services.").apply();
            });
        } catch (RuntimeException error) {
            synchronized (UpdatePush.class) { subscribing = false; }
            error(app, "Push indisponível neste aparelho ou configuração Firebase inválida.");
        }
    }

    static void received(Context context) {
        preferences(context).edit().putLong("lastReceivedAt", System.currentTimeMillis()).apply();
    }

    static void error(Context context, String message) {
        preferences(context).edit().putString("error", message).apply();
    }

    static JSObject decorate(Context context, JSObject value) {
        SharedPreferences prefs = preferences(context);
        value.put("pushSupported", true);
        value.put("pushSubscribed", prefs.getBoolean("subscribed", false));
        value.put("pushError", prefs.getString("error", ""));
        value.put("pushLastReceivedAt", prefs.getLong("lastReceivedAt", 0));
        return value;
    }
}
