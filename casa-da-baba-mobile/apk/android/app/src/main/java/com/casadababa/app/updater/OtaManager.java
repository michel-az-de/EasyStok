package com.casadababa.app.updater;

import android.content.Context;
import android.content.pm.PackageInfo;
import android.os.SystemClock;
import android.util.AtomicFile;
import android.util.Base64;
import com.getcapacitor.JSObject;
import org.json.JSONObject;
import java.io.*;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import javax.net.ssl.HttpsURLConnection;

/** Owns code versions only. It never reads, restores or deletes business data. */
public final class OtaManager {
    private static OtaManager instance;
    private static final ExecutorService WORKER = Executors.newSingleThreadExecutor();
    private final Context context;
    private final File root;
    private final File bundles;
    private final AtomicFile stateFile;
    private final int nativeCode;
    private final String builtinVersion;
    private JSONObject state;
    private String savedState;
    private String lastError = "";
    private boolean stateBroken;
    private boolean booted;
    private boolean checking;
    private boolean checkSucceeded;
    private long lastCheck;

    public interface Callback { void complete(JSObject status); }

    public static synchronized OtaManager get(Context context) {
        if (instance == null) instance = new OtaManager(context.getApplicationContext());
        return instance;
    }

    @SuppressWarnings("deprecation")
    private OtaManager(Context context) {
        this.context = context;
        root = new File(context.getFilesDir(), "ota");
        bundles = new File(root, "bundles");
        stateFile = new AtomicFile(new File(root, "state.json"));
        try {
            PackageInfo info = context.getPackageManager().getPackageInfo(context.getPackageName(), 0);
            nativeCode = info.versionCode;
            builtinVersion = info.versionName;
        } catch (Exception error) { throw new IllegalStateException("Identificação do APK indisponível.", error); }
        try {
            UpdatePolicy.require(bundles.isDirectory() || bundles.mkdirs(), "Armazenamento de versões indisponível.");
            try (InputStream input = stateFile.openRead()) {
                state = new JSONObject(new String(readLimited(input, 65536), StandardCharsets.UTF_8));
            } catch (FileNotFoundException absent) { state = initialState(); }
            UpdatePolicy.require(integer(state, "schemaVersion") == 1 && integer(state, "maxSequence") >= 0,
                "Estado de atualização inválido.");
            lastError = state.optString("lastError", "");
        } catch (Exception error) {
            state = initialState();
            stateBroken = true;
            lastError = "Estado de atualização indisponível. Usando a versão do APK.";
        }
        savedState = state.toString();
    }

    private JSONObject initialState() {
        JSONObject value = new JSONObject();
        put(value, "schemaVersion", 1);
        put(value, "maxSequence", 0L);
        put(value, "active", builtin());
        put(value, "trial", false);
        put(value, "rolledBack", false);
        return value;
    }

    private JSONObject builtin() {
        JSONObject value = new JSONObject();
        put(value, "builtin", true);
        put(value, "bundleVersion", builtinVersion);
        put(value, "nativeVersionCode", nativeCode);
        put(value, "dataSchemaVersion", UpdatePolicy.DATA_SCHEMA);
        return value;
    }

    private static void put(JSONObject target, String key, Object value) {
        try { target.put(key, value); } catch (org.json.JSONException error) { throw new IllegalStateException(error); }
    }

    private static long integer(JSONObject value, String key) throws IOException {
        Object number = value.opt(key);
        UpdatePolicy.require(number instanceof Integer || number instanceof Long, "Campo numérico inválido: " + key);
        return ((Number) number).longValue();
    }

    private static int intField(JSONObject value, String key) throws IOException {
        long number = integer(value, key);
        UpdatePolicy.require(number >= 0 && number <= Integer.MAX_VALUE, "Campo numérico fora do limite: " + key);
        return (int) number;
    }

    private static String string(JSONObject value, String key) throws IOException {
        Object text = value.opt(key);
        UpdatePolicy.require(text instanceof String, "Campo de texto inválido: " + key);
        return (String) text;
    }

    private File directory(JSONObject bundle) throws IOException {
        String hash = string(bundle, "sha256");
        UpdatePolicy.require(hash.matches("[a-f0-9]{64}"), "Identificação de pacote inválida.");
        return new File(bundles, hash);
    }

    private boolean valid(JSONObject bundle) {
        try {
            if (bundle == null || intField(bundle, "nativeVersionCode") != nativeCode
                    || intField(bundle, "dataSchemaVersion") != UpdatePolicy.DATA_SCHEMA) return false;
            if (bundle.optBoolean("builtin", false)) return builtinVersion.equals(string(bundle, "bundleVersion"));
            File location = directory(bundle);
            if (!new File(location, "index.html").isFile()) return false;
            validateBundle(location, bundle);
            return true;
        } catch (Exception error) { return false; }
    }

    /** Versão ativa gravada por outro APK. Campo ausente ou malformado continua sendo recuperação visível. */
    private boolean installedOtherApk(JSONObject active) {
        if (active == null) return false;
        Object recorded = active.opt("nativeVersionCode");
        if (!(recorded instanceof Integer || recorded instanceof Long)) return false;
        if (((Number) recorded).longValue() != nativeCode) return true;
        return active.optBoolean("builtin", false) && !builtinVersion.equals(active.optString("bundleVersion"));
    }

    /** Called before Bridge.create(), never from a running web page. Null means APK assets. */
    public synchronized String beginBoot(boolean recreatedActivity) {
        boolean recreation = recreatedActivity && booted;
        booted = true;
        if (stateBroken) {
            put(state, "active", builtin());
            put(state, "trial", false);
            return null;
        }
        try {
            if (!recreation) {
                // Aviso de retorno e erro valem para a abertura em que ocorreram.
                put(state, "rolledBack", false);
                lastError = "";
            }
            JSONObject active = state.optJSONObject("active");
            boolean recovered = false;
            if (installedOtherApk(active)) {
                // Instalar um APK novo é o caminho esperado: o conteúdo do APK anterior não serve mais.
                put(state, "active", builtin());
                state.remove("previous");
                state.remove("pending");
                state.remove("pendingRollback");
                put(state, "trial", false);
                put(state, "rolledBack", false);
                lastError = "";
                recovered = true;
            } else if (!valid(active)) {
                restorePrevious("A versão ativa está ausente ou incompatível. Uma versão segura foi restaurada.");
                recovered = true;
            }
            if (UpdatePolicy.mustFallback(state.optBoolean("trial"), recreation, state.optLong("trialBoots", 1))) {
                restorePrevious("A versão nova não confirmou a inicialização. A versão anterior foi restaurada.");
            } else if (!recreation && !recovered) {
                JSONObject pending = state.optJSONObject("pending");
                boolean applied = false;
                if (pending != null) {
                    if (valid(pending)) {
                        boolean rollback = state.optBoolean("pendingRollback");
                        // A versão abandonada no retorno não vira "anterior": reaplicá-la pularia o teste de saúde.
                        if (rollback) state.remove("previous");
                        else put(state, "previous", state.optJSONObject("active"));
                        put(state, "active", pending);
                        boolean trial = !rollback && !pending.optBoolean("builtin");
                        put(state, "trial", trial);
                        if (trial) put(state, "trialBoots", 1L); else state.remove("trialBoots");
                        put(state, "rolledBack", rollback);
                        lastError = "";
                        applied = true;
                    } else lastError = "Atualização preparada incompatível ou ausente. Versão atual preservada.";
                    state.remove("pending");
                    state.remove("pendingRollback");
                }
                if (!applied && state.optBoolean("trial"))
                    put(state, "trialBoots", state.optLong("trialBoots", 1) + 1);
            }
            save(); // Persist the trial BEFORE the WebView can execute any new code.
            active = state.optJSONObject("active");
            return active.optBoolean("builtin") ? null : directory(active).getAbsolutePath();
        } catch (Exception error) {
            stateBroken = true;
            lastError = "Não foi possível registrar a inicialização. Usando a versão do APK.";
            put(state, "active", builtin());
            return null;
        }
    }

    private void restorePrevious(String message) {
        JSONObject previous = state.optJSONObject("previous");
        put(state, "active", valid(previous) ? previous : builtin());
        state.remove("previous");
        state.remove("pending");
        state.remove("pendingRollback");
        state.remove("trialBoots");
        put(state, "trial", false);
        put(state, "rolledBack", true);
        lastError = message;
    }

    public synchronized JSObject status() {
        JSObject result = new JSObject();
        result.put("supported", true);
        result.put("activeVersion", version(state.optJSONObject("active")));
        result.put("previousVersion", version(state.optJSONObject("previous")));
        result.put("pendingVersion", version(state.optJSONObject("pending")));
        result.put("builtinVersion", builtinVersion);
        result.put("nativeVersionCode", nativeCode);
        result.put("dataSchemaVersion", UpdatePolicy.DATA_SCHEMA);
        result.put("trial", state.optBoolean("trial"));
        result.put("rolledBack", state.optBoolean("rolledBack"));
        result.put("canRollback", !stateBroken && valid(state.optJSONObject("previous")));
        result.put("checking", checking);
        result.put("checkSucceeded", checkSucceeded);
        result.put("lastError", lastError);
        return result;
    }

    private static String version(JSONObject bundle) { return bundle == null ? "" : bundle.optString("bundleVersion", ""); }

    public synchronized JSObject ready(String version) throws IOException {
        UpdatePolicy.require(!stateBroken, lastError);
        UpdatePolicy.require(version != null && version.equals(version(state.optJSONObject("active"))),
            "Confirmação de inicialização pertence a outra versão.");
        if (state.optBoolean("trial")) {
            put(state, "trial", false);
            state.remove("trialBoots");
            save();
        }
        return status();
    }

    public synchronized JSObject rollback() throws IOException {
        try {
            UpdatePolicy.require(!stateBroken, lastError);
            JSONObject previous = state.optJSONObject("previous");
            UpdatePolicy.require(valid(previous), "Não há uma versão anterior compatível neste aparelho.");
            put(state, "pending", previous);
            put(state, "pendingRollback", true);
            lastError = "";
            save();
            return status();
        } catch (IOException error) {
            lastError = error.getMessage();
            throw error;
        }
    }

    private void save() throws IOException {
        put(state, "lastError", lastError);
        FileOutputStream output = null;
        try {
            output = stateFile.startWrite();
            byte[] bytes = state.toString().getBytes(StandardCharsets.UTF_8);
            output.write(bytes);
            stateFile.finishWrite(output);
            try (InputStream input = stateFile.openRead()) {
                UpdatePolicy.require(java.util.Arrays.equals(bytes, readLimited(input, 65536)), "Não foi possível confirmar o estado persistido da atualização.");
            }
            savedState = state.toString();
        } catch (IOException error) {
            if (output != null) stateFile.failWrite(output);
            try { state = new JSONObject(savedState); }
            catch (org.json.JSONException invalid) { state = initialState(); }
            stateBroken = true;
            checkSucceeded = false;
            lastError = "Falha ao preservar o estado da atualização. Reinicie o aplicativo para tentar novamente.";
            throw error;
        }
    }

    public void check(boolean force, Callback callback) {
        synchronized (this) {
            long now = SystemClock.elapsedRealtime();
            if (checking || stateBroken || state.optBoolean("trial")
                    || (!force && lastCheck != 0 && now - lastCheck < 300000)) {
                callback.complete(status());
                return;
            }
            checking = true;
            checkSucceeded = false;
            lastCheck = now;
        }
        WORKER.execute(() -> {
            try {
                download();
                synchronized (this) { checkSucceeded = true; lastError = ""; }
            }
            catch (Exception error) {
                synchronized (this) { lastError = safeError(error); }
            } finally {
                synchronized (this) { checking = false; }
                callback.complete(status());
            }
        });
    }

    private static String safeError(Exception error) {
        String message = error.getMessage();
        return message == null || message.length() > 200 ? "Não foi possível preparar a atualização. A versão atual foi preservada." : message;
    }

    private JSONObject manifest() throws Exception {
        JSONObject envelope = new JSONObject(new String(fetch(UpdatePolicy.MANIFEST_URL, 65536), StandardCharsets.UTF_8));
        byte[] payload = decode(string(envelope, "payloadBase64"));
        byte[] signature = decode(string(envelope, "signatureBase64"));
        try (InputStream key = context.getAssets().open("ota-public-key.der")) {
            UpdatePolicy.verifySignature(payload, signature, readLimited(key, 8192));
        }
        JSONObject value = new JSONObject(new String(payload, StandardCharsets.UTF_8));
        UpdatePolicy.validateRelease(intField(value, "schemaVersion"), string(value, "appId"), string(value, "channel"),
            integer(value, "sequence"), string(value, "bundleVersion"), intField(value, "nativeVersionCode"), nativeCode,
            intField(value, "dataSchemaVersion"), string(value, "url"), integer(value, "sizeBytes"), string(value, "sha256"));
        UpdatePolicy.require(!value.has("builtin"), "Manifesto contém campo reservado.");
        return value;
    }

    private static byte[] decode(String value) throws IOException {
        try {
            byte[] result = Base64.decode(value, Base64.NO_WRAP);
            UpdatePolicy.require(Base64.encodeToString(result, Base64.NO_WRAP).equals(value), "Base64 inválido no manifesto.");
            return result;
        } catch (IllegalArgumentException error) { throw new IOException("Base64 inválido no manifesto.", error); }
    }

    private void download() throws Exception {
        JSONObject release = manifest();
        long sequence = integer(release, "sequence");
        synchronized (this) {
            if (sequence <= integer(state, "maxSequence")) return;
            UpdatePolicy.require(!state.optBoolean("pendingRollback"), "Retorno à versão anterior já está preparado.");
            // A version label is an identity, not just display text: stale ready() calls must never acknowledge new bytes.
            for (String name : new String[] { "active", "previous", "pending" }) {
                JSONObject known = state.optJSONObject(name);
                if (known != null && version(known).equals(version(release)))
                    UpdatePolicy.require(string(release, "sha256").equals(known.optString("sha256")), "Nome de versão já utilizado por outro pacote.");
            }
        }
        File archive = new File(root, "download-" + UUID.randomUUID() + ".zip");
        File stage = new File(root, "stage-" + UUID.randomUUID());
        try {
            long expected = integer(release, "sizeBytes");
            HttpsURLConnection connection = connect(string(release, "url"));
            try (InputStream input = connection.getInputStream(); FileOutputStream output = new FileOutputStream(archive)) {
                copyLimited(input, output, expected);
                output.getFD().sync();
            } finally { connection.disconnect(); }
            UpdatePolicy.require(archive.length() == expected && UpdatePolicy.digest(archive).equals(string(release, "sha256")),
                "Pacote incompleto ou hash divergente.");
            UpdatePolicy.require(stage.mkdir(), "Não foi possível preparar o diretório da atualização.");
            UpdatePolicy.extract(archive, stage);
            validateBundle(stage, release);
            synchronized (this) {
                if (sequence <= integer(state, "maxSequence") || state.optBoolean("pendingRollback")) return;
                File target = directory(release);
                if (target.exists()) validateBundle(target, release);
                else UpdatePolicy.require(stage.renameTo(target), "Falha ao concluir o pacote da atualização.");
                put(state, "pending", release);
                put(state, "pendingRollback", false);
                put(state, "maxSequence", sequence);
                lastError = "";
                save();
                cleanup();
            }
        } finally {
            deleteOwned(stage);
            if (archive.exists() && !archive.delete()) archive.deleteOnExit();
        }
    }

    private void validateBundle(File location, JSONObject expected) throws Exception {
        UpdatePolicy.require(new File(location, "index.html").isFile(), "Pacote sem tela inicial.");
        try (InputStream input = new FileInputStream(new File(location, "bundle.json"))) {
            JSONObject metadata = new JSONObject(new String(readLimited(input, 8192), StandardCharsets.UTF_8));
            UpdatePolicy.require(intField(metadata, "schemaVersion") == 1
                && UpdatePolicy.APP_ID.equals(string(metadata, "appId")) && UpdatePolicy.CHANNEL.equals(string(metadata, "channel"))
                && version(expected).equals(string(metadata, "bundleVersion"))
                && intField(metadata, "nativeVersionCode") == nativeCode
                && intField(metadata, "dataSchemaVersion") == UpdatePolicy.DATA_SCHEMA,
                "Identificação interna do pacote não corresponde ao manifesto.");
        }
    }

    private static HttpsURLConnection connect(String url) throws IOException {
        UpdatePolicy.validateUrl(url);
        HttpsURLConnection connection = (HttpsURLConnection) new URL(url).openConnection();
        connection.setInstanceFollowRedirects(false);
        connection.setConnectTimeout(10000);
        connection.setReadTimeout(15000);
        connection.setUseCaches(false);
        connection.setRequestProperty("Accept-Encoding", "identity");
        connection.setRequestProperty("Cache-Control", "no-cache");
        try {
            UpdatePolicy.require(connection.getResponseCode() == 200, "Servidor de atualização indisponível (HTTP " + connection.getResponseCode() + ").");
            return connection;
        } catch (IOException error) { connection.disconnect(); throw error; }
    }

    private static byte[] fetch(String url, long limit) throws IOException {
        HttpsURLConnection connection = connect(url);
        try (InputStream input = connection.getInputStream()) { return readLimited(input, limit); }
        finally { connection.disconnect(); }
    }

    private static byte[] readLimited(InputStream input, long limit) throws IOException {
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        copyLimited(input, output, limit);
        return output.toByteArray();
    }

    private static void copyLimited(InputStream input, OutputStream output, long limit) throws IOException {
        byte[] buffer = new byte[16384];
        long total = 0;
        int read;
        while ((read = input.read(buffer)) != -1) {
            total += read;
            UpdatePolicy.require(total <= limit, "Download excede o tamanho permitido.");
            output.write(buffer, 0, read);
        }
    }

    private void cleanup() throws IOException {
        File[] files = bundles.listFiles();
        if (files == null) return;
        for (File file : files) {
            boolean retained = false;
            for (String name : new String[] { "active", "previous", "pending" }) {
                JSONObject bundle = state.optJSONObject(name);
                if (bundle != null && file.getName().equals(bundle.optString("sha256"))) retained = true;
            }
            if (!retained) deleteOwned(file);
        }
    }

    private void deleteOwned(File file) throws IOException {
        UpdatePolicy.require(file.getCanonicalPath().startsWith(root.getCanonicalPath() + File.separator), "Limpeza fora do diretório de atualização recusada.");
        if (!file.exists()) return;
        File[] children = file.listFiles();
        if (children != null) for (File child : children) deleteOwned(child);
        UpdatePolicy.require(file.delete(), "Não foi possível remover um arquivo temporário da atualização.");
    }
}
