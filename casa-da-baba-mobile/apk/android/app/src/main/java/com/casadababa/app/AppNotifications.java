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
import org.json.JSONObject;
import com.google.firebase.messaging.RemoteMessage;

public final class AppNotifications {
    public static final int PERMISSION_REQUEST = 2087;
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
    public static String permission(Context context) {
        if (Build.VERSION.SDK_INT >= 33 && context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)
                != PackageManager.PERMISSION_GRANTED) return "denied";
        return enabled(context) ? "granted" : "blocked";
    }
    public static void subscribe(Context context, boolean newToken) {
        // No topic acknowledgement can prove delivery. The new flow registers this installation.
        try {
            if (newToken) NoticeManager.get(context).tokenChanged();
            else NoticeSyncJob.schedule(context);
        } catch (Exception ignored) { /* The bell exposes storage/identity failures without touching production. */ }
    }
    private static String enabledMessage(Context context) {
        try {
            return NoticeManager.get(context).status().optBoolean("registered", false)
                ? "Notificações permitidas. Consulte no sino o histórico e as confirmações deste aparelho."
                : "Notificações permitidas. Abra o sino e toque em Atualizar para registrar o aparelho.";
        } catch (Exception failure) { return "Notificações permitidas. Não foi possível ler o registro; confira o diagnóstico no sino."; }
    }
    public static void offerPermission(Activity activity, boolean explicit) {
        if (activity.isFinishing()) return;
        initialize(activity);
        if (!explicit && prefs(activity).getBoolean("explained", false)) return;
        if (enabled(activity)) {
            prefs(activity).edit().putBoolean("explained", true).apply();
            if (explicit) new AlertDialog.Builder(activity).setTitle("Notificações")
                .setMessage(enabledMessage(activity))
                .setPositiveButton("OK", null).setNeutralButton("Configurar", (d,w) -> openSettings(activity)).show();
            return;
        }
        // A decisão não interfere no banco, na produção nem no atualizador silencioso.
        prefs(activity).edit().putBoolean("explained", true).apply();
        new AlertDialog.Builder(activity).setTitle("Receber avisos da Casa da Baba?")
            .setMessage("Receba orientações e atualizações. Se escolher Depois, abra o sino de Avisos e toque em Ativar.")
            .setPositiveButton("Ativar", (d,w) -> {
                if (NoticePolicy.shouldRequestPermission(Build.VERSION.SDK_INT,
                        Build.VERSION.SDK_INT < 33 || activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED,
                        prefs(activity).getBoolean("requested", false))) {
                    prefs(activity).edit().putBoolean("requested", true).apply();
                    activity.requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, PERMISSION_REQUEST);
                } else openSettings(activity);
            }).setNegativeButton("Depois", null).show();
    }
    public static void openSettings(Activity activity) {
        Intent intent = new Intent(Build.VERSION.SDK_INT >= 26 ? Settings.ACTION_APP_NOTIFICATION_SETTINGS : Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
        if (Build.VERSION.SDK_INT >= 26) intent.putExtra(Settings.EXTRA_APP_PACKAGE, activity.getPackageName());
        else intent.setData(android.net.Uri.parse("package:" + activity.getPackageName()));
        activity.startActivity(intent);
    }
    public static boolean openNotice(Activity activity) {
        Intent intent = activity.getIntent();
        if (intent == null || !"signed-notice".equals(intent.getStringExtra("kind"))) return false;
        String id = intent.getStringExtra("noticeId");
        intent.removeExtra("kind"); intent.removeExtra("noticeId");
        if (id == null) return false;
        try { NoticeManager.get(activity).pendingOpen(id); return true; }
        catch (Exception failure) { return false; }
    }
    public static void showGuide(Activity activity) {
        new AlertDialog.Builder(activity).setTitle("Trazer dados do PWA antigo")
            .setMessage("1. Pare novos registros durante a transferência. Não apague nem limpe o PWA antigo.\n\n"
                + "2. No PWA antigo: Ajustes > Manut. > Backup e restauração > Exportar backup (JSON). Confirme que o arquivo foi salvo e guarde outra cópia.\n\n"
                + "3. Neste app: Configurações > Backup do PWA > Importar. Selecione o JSON antes de conectar.\n\n"
                + "4. Confira produtos, estoque, pedidos, lotes e fotos. Se houver conflito ou foto ausente, preserve tudo e peça ajuda.\n\n"
                + "5. No início, toque em Conectar e informe o código de 6 dígitos da empresa e loja corretas. Revise os dados importados e toque em Sincronizar para o primeiro envio.\n\n"
                + "6. Aguarde a confirmação do EasyStok e compare as contagens no sistema. Se cair a conexão, retome pelo botão Sincronizar.\n\n"
                + "Depois da conferência, registre somente no app novo. Preserve o PWA antigo para consulta e mantenha o backup. Históricos não geram novas vendas ou pagamentos.")
            .setPositiveButton("Entendi", (d,w) -> offerPermission(activity, false)).show();
    }
    public static void receive(Context context, RemoteMessage message) {
        if (!"signed-notice".equals(message.getData().get("kind"))) return;
        try {
            JSONObject envelope = new JSONObject().put("payloadBase64", message.getData().get("payloadBase64"))
                .put("signatureBase64", message.getData().get("signatureBase64"));
            NoticeManager.get(context).acceptPush(envelope);
        } catch (Exception ignored) { /* Unverified data is never presented or acknowledged. */ }
    }
    static void display(Context context, JSONObject notice) throws Exception {
        initialize(context);
        if (!enabled(context)) return;
        String id = notice.getString("id");
        Intent open = new Intent(context, MainActivity.class).setAction(NoticePolicy.ACTION)
            .setData(android.net.Uri.parse("cdb-notice:" + id))
            .addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP)
            .putExtra("kind", "signed-notice").putExtra("noticeId", id);
        PendingIntent tap = PendingIntent.getActivity(context, 0, open,
            PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26
            ? new Notification.Builder(context, NoticePolicy.CHANNEL) : new Notification.Builder(context);
        builder.setSmallIcon(R.drawable.ic_stat_notice).setContentTitle(notice.getString("title")).setContentText(notice.getString("body"))
            .setStyle(new Notification.BigTextStyle().bigText(notice.getString("body"))).setContentIntent(tap).setAutoCancel(true)
            .setDefaults(Notification.DEFAULT_SOUND).setVisibility(Notification.VISIBILITY_PRIVATE);
        try { context.getSystemService(NotificationManager.class).notify(id, 2091, builder.build()); }
        catch (SecurityException ignored) { /* Received and presented are distinct. The signed inbox remains available. */ }
    }
}
