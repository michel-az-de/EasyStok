package com.casadababa.app;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import com.getcapacitor.JSObject;
import com.google.android.gms.tasks.Tasks;
import com.google.firebase.messaging.FirebaseMessaging;
import org.json.JSONArray;
import org.json.JSONObject;
import javax.net.ssl.HttpsURLConnection;
import java.io.*;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.security.spec.ECGenParameterSpec;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Installation identity is separate from production pairing; only public notices enter here. */
public final class NoticeManager {
    private static final String API = "https://api.easystok.online/api/mobile/push/";
    private static final String KEY_ALIAS = "cdb-push-installation-v1";
    private static NoticeManager instance;
    private final Context context;
    private final NoticeStore store;
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final AtomicBoolean checking = new AtomicBoolean();
    private final Handler main = new Handler(Looper.getMainLooper());
    private PrivateKey privateKey;
    private byte[] publicKey;
    private byte[] publisherKey;
    private String installationId;
    private String apkVersion;
    private volatile Listener listener;
    private volatile String transientError = "";
    private volatile long retryAfter;
    public interface Listener { void changed(JSObject status); }
    public interface Callback { void complete(JSObject status, boolean success); }

    private NoticeManager(Context context) throws Exception {
        this.context = context.getApplicationContext();
        store = new NoticeStore(new File(this.context.getFilesDir(), "cdb-notices-v1.json"));
        apkVersion = this.context.getPackageManager().getPackageInfo(this.context.getPackageName(), 0).versionName;
    }
    public static synchronized NoticeManager get(Context context) throws Exception {
        if (instance == null) instance = new NoticeManager(context);
        return instance;
    }
    private synchronized void identity() throws Exception {
        if (privateKey != null) return;
        KeyStore keys = KeyStore.getInstance("AndroidKeyStore"); keys.load(null);
        if (!keys.containsAlias(KEY_ALIAS)) {
            KeyPairGenerator generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, "AndroidKeyStore");
            generator.initialize(new KeyGenParameterSpec.Builder(KEY_ALIAS, KeyProperties.PURPOSE_SIGN | KeyProperties.PURPOSE_VERIFY)
                .setAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1"))
                .setDigests(KeyProperties.DIGEST_SHA256).setUserAuthenticationRequired(false).build());
            generator.generateKeyPair();
        }
        PrivateKey loadedPrivate = (PrivateKey) keys.getKey(KEY_ALIAS, null);
        byte[] loadedPublic = keys.getCertificate(KEY_ALIAS).getPublicKey().getEncoded();
        String loadedId = SignedNoticePolicy.installationId(loadedPublic);
        try (InputStream input = context.getAssets().open("ota-public-key.der")) { publisherKey = NoticeStore.read(input, 2048); }
        String recorded = store.snapshot().optString("installationId");
        // Do not silently transfer receipts from a lost identity to a newly generated key.
        SignedNoticePolicy.require(recorded.isEmpty() || loadedId.equals(recorded), "A identidade dos avisos mudou. Solicite suporte sem apagar os dados.");
        if (recorded.isEmpty()) store.update("installationId", loadedId);
        privateKey = loadedPrivate; publicKey = loadedPublic; installationId = loadedId;
    }
    public void setListener(Listener value) { listener = value; }
    public void removeListener(Listener value) { if (listener == value) listener = null; }
    public void statusAsync(Callback callback) {
        executor.execute(() -> { try { identity(); callback.complete(status(), true); } catch (Exception failure) { error("Não foi possível abrir a identidade segura dos avisos."); callback.complete(status(), false); } });
    }
    public JSObject status() {
        JSObject result = new JSObject();
        result.put("supported", true); result.put("permission", AppNotifications.permission(context));
        result.put("installationId", installationId == null ? "" : installationId); result.put("apkVersion", apkVersion);
        result.put("checking", checking.get());
        try {
            JSONObject snapshot = store.snapshot(); JSONArray items = snapshot.getJSONArray("items"), visible = new JSONArray(); int unread = 0;
            for (int i = items.length() - 1; i >= 0; i--) {
                JSONObject item = items.getJSONObject(i); if (item.optLong("openedAt", 0) == 0) unread++;
                visible.put(new JSONObject().put("id", item.getString("id")).put("title", item.getString("title"))
                    .put("body", item.getString("body")).put("screen", item.getString("screen"))
                    .put("receivedAt", item.getLong("receivedAt")).put("openedAt", item.optLong("openedAt", 0))
                    .put("source", item.getString("source")));
            }
            result.put("items", visible); result.put("unread", unread);
            result.put("registered", privateKey != null && snapshot.optBoolean("registered", false));
            result.put("pendingReceipts", snapshot.getJSONArray("pending").length());
            result.put("lastPushAt", snapshot.optLong("lastPushAt", 0)); result.put("lastSyncAt", snapshot.optLong("lastSyncAt", 0));
            result.put("pendingOpenId", snapshot.optString("pendingOpenId", ""));
            result.put("lastError", transientError.isEmpty() ? snapshot.optString("lastError", "") : transientError);
        } catch (Exception failure) { result.put("registered", false); result.put("items", new JSONArray()); result.put("lastError", "Não foi possível ler o histórico. Os dados foram preservados."); }
        return result;
    }
    private void changed() { Listener current = listener; if (current != null) { JSObject value = status(); main.post(() -> { if (listener == current) current.changed(value); }); } }
    public void error(String message) {
        transientError = message;
        try { store.update("lastError", message); } catch (Exception ignored) { /* Keep the previous durable inbox intact. */ }
        changed();
    }
    public void refresh(Callback callback) { refresh(callback, true); }
    public void refresh(Callback callback, boolean foreground) {
        if (android.os.SystemClock.elapsedRealtime() < retryAfter) { if (callback != null) callback.complete(status(), false); return; }
        if (!checking.compareAndSet(false, true)) { if (callback != null) callback.complete(status(), false); return; }
        changed();
        executor.execute(() -> {
            boolean success = false;
            try {
                identity();
                JSONObject before = store.snapshot();
                if (!before.optBoolean("registered", false) || System.currentTimeMillis() - before.optLong("lastRegistrationAt", 0) > 21600000) {
                    String token = Tasks.await(FirebaseMessaging.getInstance().getToken(), 20, TimeUnit.SECONDS);
                    SignedNoticePolicy.require(token != null && !token.isEmpty() && token.length() <= 4096, "Token indisponível.");
                    long issued = Math.max(System.currentTimeMillis(), before.optLong("lastRegistrationAt", 0) + 1);
                    JSONObject registration = new JSONObject().put("kind", "cdb-installation-v1").put("installationId", installationId)
                        .put("token", token).put("apkVersion", apkVersion).put("issuedAt", issued);
                    JSONObject registered = post("installations", sign(registration));
                    SignedNoticePolicy.require(registered.optBoolean("registered", false), "Registro não confirmado.");
                    store.update("registered", true); store.update("lastRegistrationAt", issued);
                }
                // An upgraded APK may still belong to the old unsigned notification topic.
                boolean legacyRemoved = store.snapshot().optBoolean("legacyTopicRemoved", false);
                if (!legacyRemoved) {
                    try {
                        Tasks.await(FirebaseMessaging.getInstance().unsubscribeFromTopic(NoticePolicy.TOPIC), 20, TimeUnit.SECONDS);
                        store.update("legacyTopicRemoved", true); legacyRemoved = true;
                    } catch (Exception failure) { /* Retry cleanup, but do not hold back signed receipts. */ }
                }
                flush(8);
                if (foreground || System.currentTimeMillis() - before.optLong("lastSyncAt", 0) > 900000) {
                    JSONObject query = new JSONObject().put("kind", "cdb-inbox-v1").put("installationId", installationId).put("issuedAt", System.currentTimeMillis());
                    JSONArray messages = post("inbox", sign(query)).getJSONArray("messages");
                    SignedNoticePolicy.require(messages.length() <= 100, "Histórico excede o limite.");
                    for (int i = 0; i < messages.length(); i++) {
                        JSONObject envelope = messages.getJSONObject(i);
                        JSONObject notice = SignedNoticePolicy.verify(envelope, publisherKey, installationId, System.currentTimeMillis(), true);
                        store.accept(envelope, notice, "sync", System.currentTimeMillis(), apkVersion);
                    }
                    store.update("lastSyncAt", System.currentTimeMillis());
                }
                flush(8);
                if (legacyRemoved) { transientError = ""; store.update("lastError", ""); }
                else error("Os avisos novos foram verificados. A retirada do registro antigo no Firebase ainda será repetida.");
                success = legacyRemoved && store.snapshot().getJSONArray("pending").length() == 0;
            } catch (TimeoutException timeout) { error("O registro no Firebase demorou. Confira a conexão e os serviços do Google Play; tentaremos novamente."); }
            catch (ApiException failure) { error(failure.safeMessage); }
            catch (ExecutionException failure) { error("O Firebase não concluiu o registro. Confira a conexão e os serviços do Google Play; tentaremos novamente."); }
            catch (Exception failure) { error("Não foi possível concluir a verificação dos avisos. O histórico e os recibos foram preservados."); }
            finally {
                checking.set(false); changed();
                if (callback != null) callback.complete(status(), success);
                if (!success && callback == null) NoticeSyncJob.schedule(context);
            }
        });
    }
    public void tokenChanged() {
        try { store.update("registered", false); store.update("legacyTopicRemoved", false); } catch (Exception ignored) {}
        NoticeSyncJob.schedule(context); changed();
    }
    public void acceptPush(JSONObject envelope) {
        JSONObject notice;
        boolean display;
        try {
            identity();
            notice = SignedNoticePolicy.verify(envelope, publisherKey, installationId, System.currentTimeMillis(), true);
            display = store.accept(envelope, notice, "push", System.currentTimeMillis(), apkVersion);
        } catch (Exception rejected) {
            error("Um aviso não pôde ser validado ou salvo. Nenhuma confirmação foi criada para esta tentativa.");
            return;
        }
        try { if (display) AppNotifications.display(context, notice); }
        catch (Exception failure) { error("Aviso recebido e salvo no sino. O Android não conseguiu exibir a notificação."); }
        finally { NoticeSyncJob.schedule(context); changed(); }
    }
    public void openAsync(String id, Callback callback) {
        executor.execute(() -> {
            try { identity(); store.opened(id, System.currentTimeMillis(), apkVersion); NoticeSyncJob.schedule(context); changed(); callback.complete(status(), true); }
            catch (Exception failure) { error("Não foi possível confirmar a abertura deste aviso."); callback.complete(status(), false); }
        });
    }
    public void pendingOpen(String id) {
        try { store.pendingOpen(id); changed(); } catch (Exception failure) { error("Não foi possível abrir o aviso selecionado."); }
    }
    private JSONObject sign(JSONObject payload) throws Exception { return SignedNoticePolicy.sign(payload, privateKey, publicKey); }
    private void flush(int maximum) throws Exception {
        JSONArray queue = store.snapshot().getJSONArray("pending");
        long deadline = android.os.SystemClock.elapsedRealtime() + 20000;
        for (int i = 0; i < Math.min(queue.length(), maximum) && android.os.SystemClock.elapsedRealtime() < deadline; i++) {
            JSONObject entry = queue.getJSONObject(i);
            JSONObject response = post("receipts", sign(entry.getJSONObject("payload")));
            SignedNoticePolicy.require(response.optBoolean("received", false), "Recibo não confirmado.");
            store.acknowledged(entry.getString("key"));
        }
    }
    private JSONObject post(String route, JSONObject payload) throws Exception {
        HttpsURLConnection connection = (HttpsURLConnection) new URL(API + route).openConnection();
        connection.setInstanceFollowRedirects(false); connection.setConnectTimeout(10000); connection.setReadTimeout(15000);
        connection.setRequestMethod("POST"); connection.setDoOutput(true); connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
        byte[] bytes = payload.toString().getBytes(StandardCharsets.UTF_8); connection.setFixedLengthStreamingMode(bytes.length);
        try {
            try (OutputStream output = connection.getOutputStream()) { output.write(bytes); }
            int code = connection.getResponseCode();
            if (code == 429) {
                long delay = 60000;
                try { delay = Math.min(3600000, Math.max(delay, Long.parseLong(connection.getHeaderField("Retry-After")) * 1000)); }
                catch (NumberFormatException ignored) { }
                retryAfter = android.os.SystemClock.elapsedRealtime() + delay;
            }
            if (code != 200) throw new ApiException(code == 429 ? "Verificações temporariamente limitadas. Tentaremos novamente."
                : code == 404 || code == 503 ? "O serviço de avisos está indisponível. Tentaremos novamente."
                : "O servidor não confirmou os avisos (HTTP " + code + "). O histórico foi preservado.");
            try (InputStream input = connection.getInputStream()) { return new JSONObject(new String(NoticeStore.read(input, 1048576), StandardCharsets.UTF_8)); }
        } finally { connection.disconnect(); }
    }
    private static final class ApiException extends IOException {
        final String safeMessage;
        ApiException(String message) { super(message); safeMessage = message; }
    }
}
