package com.casadababa.app;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.os.Build;
import android.provider.Settings;
import androidx.core.app.NotificationManagerCompat;
import com.google.firebase.messaging.FirebaseMessaging;
import com.google.firebase.messaging.RemoteMessage;

public final class AppNotifications {
    public static final int PERMISSION_REQUEST = 2087;
    private static boolean subscribing;
    private AppNotifications() {}
    private static SharedPreferences prefs(Context context) {
        return context.getSharedPreferences("cdb-visible-notifications", Context.MODE_PRIVATE);
    }
    public static void initialize(Context context) {
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel channel = new NotificationChannel(NoticePolicy.CHANNEL,
                "Orientações e avisos", NotificationManager.IMPORTANCE_DEFAULT);
            channel.setDescription("Orientações de uso e avisos públicos da Casa da Baba.");
            context.getSystemService(NotificationManager.class).createNotificationChannel(channel);
        }
        subscribe(context, false);
    }
    public static boolean enabled(Context context) {
        if (Build.VERSION.SDK_INT >= 33 && context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)
                != PackageManager.PERMISSION_GRANTED) return false;
        if (!NotificationManagerCompat.from(context).areNotificationsEnabled()) return false;
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel channel = context.getSystemService(NotificationManager.class).getNotificationChannel(NoticePolicy.CHANNEL);
            if (channel != null && channel.getImportance() == NotificationManager.IMPORTANCE_NONE) return false;
        }
        return true;
    }
    public static void subscribe(Context context, boolean newToken) {
        Context app = context.getApplicationContext();
        if (newToken) prefs(app).edit().putBoolean("subscribed", false).apply();
        if (!enabled(app)) return;
        synchronized (AppNotifications.class) {
            if (subscribing || prefs(app).getBoolean("subscribed", false)) return;
            subscribing = true;
        }
        try {
            FirebaseMessaging.getInstance().subscribeToTopic(NoticePolicy.TOPIC).addOnCompleteListener(task -> {
                synchronized (AppNotifications.class) { subscribing = false; }
                prefs(app).edit().putBoolean("subscribed", task.isSuccessful()).apply();
            });
        } catch (RuntimeException error) {
            synchronized (AppNotifications.class) { subscribing = false; }
        }
    }
    public static void offerPermission(Activity activity, boolean explicit) {
        if (activity.isFinishing()) return;
        initialize(activity);
        if (!explicit && prefs(activity).getBoolean("explained", false)) return;
        if (enabled(activity)) {
            prefs(activity).edit().putBoolean("explained", true).apply();
            if (explicit) new AlertDialog.Builder(activity).setTitle("Notificações")
                .setMessage(prefs(activity).getBoolean("subscribed", false)
                    ? "Avisos ativados. Você receberá orientações e poderá tocar para abrir o app."
                    : "Permissão ativada. Conecte-se à internet para concluir o registro dos avisos.")
                .setPositiveButton("OK", null).setNeutralButton("Configurar", (d,w) -> openSettings(activity)).show();
            return;
        }
        // A decisão não interfere no banco, na produção nem no atualizador silencioso.
        prefs(activity).edit().putBoolean("explained", true).apply();
        new AlertDialog.Builder(activity).setTitle("Receber avisos da Casa da Baba?")
            .setMessage("Ative notificações para receber orientações, como trazer seus dados do PWA. "
                + "O app continua funcionando se você escolher Agora não. Depois, use Notificações na tela inicial.")
            .setPositiveButton("Ativar", (d,w) -> {
                if (NoticePolicy.shouldRequestPermission(Build.VERSION.SDK_INT,
                        Build.VERSION.SDK_INT < 33 || activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED,
                        prefs(activity).getBoolean("requested", false))) {
                    prefs(activity).edit().putBoolean("requested", true).apply();
                    activity.requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, PERMISSION_REQUEST);
                } else openSettings(activity);
            }).setNegativeButton("Agora não", null).show();
    }
    private static void openSettings(Activity activity) {
        Intent intent = new Intent(Build.VERSION.SDK_INT >= 26 ? Settings.ACTION_APP_NOTIFICATION_SETTINGS : Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
        if (Build.VERSION.SDK_INT >= 26) intent.putExtra(Settings.EXTRA_APP_PACKAGE, activity.getPackageName());
        else intent.setData(android.net.Uri.parse("package:" + activity.getPackageName()));
        activity.startActivity(intent);
    }
    public static boolean openNotice(Activity activity) {
        Intent intent = activity.getIntent();
        if (intent == null || !"app-notice".equals(intent.getStringExtra("kind"))) return false;
        String screen = intent.getStringExtra("screen");
        intent.removeExtra("kind"); intent.removeExtra("screen");
        if (!NoticePolicy.screenAllowed(screen)) return false;
        if ("migration-guide".equals(screen)) {
            showGuide(activity);
            return true;
        }
        return false;
    }
    public static void showGuide(Activity activity) {
        new AlertDialog.Builder(activity).setTitle("Trazer dados do PWA antigo")
            .setMessage("1. Pare novos registros durante a transferência. Não apague nem limpe o PWA antigo.\n\n"
                + "2. No PWA antigo: Ajustes > Manut. > Backup e restauração > Exportar backup (JSON). Confirme que o arquivo foi salvo e guarde outra cópia.\n\n"
                + "3. Neste app: Já usava o PWA antigo? > Trazer dados do PWA. Selecione o JSON antes de conectar a produção.\n\n"
                + "4. Confira produtos, estoque, pedidos, lotes e fotos. Se houver conflito ou foto ausente, preserve tudo e peça ajuda.\n\n"
                + "5. Conecte a produção com o código de 6 dígitos da empresa e loja corretas. Como os dados estavam somente no Android, use Enviar primeira migração.\n\n"
                + "6. Espere Primeira migração confirmada pelo EasyStok e confira os dados no sistema. Se cair a conexão, use Retomar migração.\n\n"
                + "Depois da conferência, registre somente no app novo. Preserve o PWA antigo para consulta e mantenha o backup. Históricos não geram novas vendas ou pagamentos.")
            .setPositiveButton("Entendi", (d,w) -> offerPermission(activity, false)).show();
    }
    public static void receive(Context context, RemoteMessage message) {
        RemoteMessage.Notification notification = message.getNotification();
        if (notification == null) return;
        String id = message.getData().get("noticeId"), screen = message.getData().get("screen");
        String title = notification.getTitle(), body = notification.getBody();
        if (!NoticePolicy.accepts(message.getFrom(), message.getData().get("kind"), id, screen, title, body)) return;
        initialize(context);
        if (!enabled(context)) return;
        Intent open = new Intent(context, MainActivity.class).setAction(NoticePolicy.ACTION)
            .addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP)
            .putExtra("kind", "app-notice").putExtra("screen", screen);
        PendingIntent tap = PendingIntent.getActivity(context, id.hashCode(), open,
            PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26
            ? new Notification.Builder(context, NoticePolicy.CHANNEL) : new Notification.Builder(context);
        builder.setSmallIcon(R.drawable.ic_stat_notice).setContentTitle(title).setContentText(body)
            .setStyle(new Notification.BigTextStyle().bigText(body)).setContentIntent(tap).setAutoCancel(true)
            .setVisibility(Notification.VISIBILITY_PRIVATE);
        try { context.getSystemService(NotificationManager.class).notify(id, 2087, builder.build()); }
        catch (SecurityException ignored) { /* A permissão pode ser revogada durante o recebimento. */ }
    }
}
