package com.casadababa.app;

import android.app.Activity;
import android.content.Context;
import android.os.CancellationSignal;
import android.os.Handler;
import android.os.Looper;
import android.util.Base64;
import androidx.credentials.ClearCredentialStateRequest;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.ClearCredentialException;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.GetCredentialException;
import androidx.credentials.exceptions.NoCredentialException;
import androidx.core.content.ContextCompat;
import com.google.android.libraries.identity.googleid.GetGoogleIdOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;
import java.nio.charset.StandardCharsets;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.CopyOnWriteArraySet;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.atomic.AtomicBoolean;
import org.json.JSONArray;
import org.json.JSONObject;

// Um único dono dos refresh tokens para Home e Workspace, inclusive durante interrupções.
final class NativeSession {
    interface Callback { void complete(JSONObject value, SessionHttpClient.Failure error); }
    interface Listener { void changed(boolean authenticated, long version); }
    private static NativeSession instance;
    private final SessionVault vault;
    private final SessionHttpClient api;
    private final CredentialManager credentials;
    private final Runnable clearProvider;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final ExecutorService revocations = Executors.newSingleThreadExecutor();
    private final AtomicBoolean revocationPending = new AtomicBoolean();
    private final Object stateLock = new Object();
    private final AtomicLong generation = new AtomicLong();
    private final Set<Listener> listeners = new CopyOnWriteArraySet<>();

    private NativeSession(Context context) {
        this(context, new SessionVault(context), new SessionHttpClient(), null);
    }

    // Transporte e cofre isolados permitem exercitar o proprietário no Android sem contas reais.
    NativeSession(Context context, SessionVault vault, SessionHttpClient api, Runnable clearProvider) {
        this.vault = vault;
        this.api = api;
        credentials = CredentialManager.create(context);
        this.clearProvider = clearProvider == null ? this::clearGoogleState : clearProvider;
    }

    static synchronized NativeSession get(Context context) {
        if (instance == null) instance = new NativeSession(context.getApplicationContext());
        return instance;
    }

    void addListener(Listener listener) { listeners.add(listener); }
    void removeListener(Listener listener) { listeners.remove(listener); }
    private void changed(boolean authenticated, long version) {
        main.post(() -> { if (current(version)) for (Listener listener : listeners) listener.changed(authenticated, version); });
    }
    private void done(Callback callback, JSONObject data, long version) {
        main.post(() -> {
            if (!current(version)) { failed(callback, new SessionHttpClient.Failure(401, "SESSAO_ALTERADA", "A conta foi encerrada ou trocada.")); return; }
            try { data.put("versao", version); callback.complete(data, null); }
            catch (Exception invalid) { failed(callback, invalid); }
        });
    }
    private void failed(Callback callback, Exception failure) {
        SessionHttpClient.Failure safe = failure instanceof SessionHttpClient.Failure
            ? (SessionHttpClient.Failure) failure
            : new SessionHttpClient.Failure(0, "CONEXAO_INDISPONIVEL", "Não foi possível acessar a conta. Confira a conexão e tente de novo. Seus dados foram mantidos.");
        main.post(() -> callback.complete(null, safe));
    }

    private boolean current(long version) { return generation.get() == version; }
    private long newGeneration() { synchronized (stateLock) { return generation.incrementAndGet(); } }
    private void saveCurrent(JSONObject saved, long version) throws Exception {
        synchronized (stateLock) { requireCurrent(version); vault.write(saved); }
    }
    private void requireCurrent(long version) throws SessionHttpClient.Failure {
        if (!current(version)) throw new SessionHttpClient.Failure(401, "SESSAO_ALTERADA", "A conta foi encerrada ou trocada.");
    }

    static JSONObject claims(String token) throws Exception {
        String[] parts = token.split("\\.");
        if (parts.length != 3) throw new IllegalArgumentException("Token inválido.");
        return new JSONObject(new String(Base64.decode(parts[1], Base64.URL_SAFE | Base64.NO_WRAP | Base64.NO_PADDING), StandardCharsets.UTF_8));
    }

    private JSONObject profile(JSONObject saved) throws Exception {
        JSONObject result = new JSONObject();
        boolean authenticated = saved.has("refreshToken") && saved.has("token");
        result.put("autenticado", authenticated);
        if (!authenticated) return result;
        JSONObject payload = claims(saved.getString("token"));
        result.put("usuario", saved.getJSONObject("usuario"));
        result.put("empresaId", payload.getString("empresaId"));
        result.put("dispositivoId", saved.getString("dispositivoId"));
        result.put("sessaoId", payload.getString("sid"));
        result.put("vinculado", saved.has("pairing"));
        return result;
    }

    private JSONObject publicSession(JSONObject saved) throws Exception {
        JSONObject result = profile(saved);
        if (result.optBoolean("autenticado")) {
            result.put("token", saved.getString("token"));
            result.put("expiresIn", Math.max(0, claims(saved.getString("token")).getLong("exp") - System.currentTimeMillis() / 1000));
        }
        return result;
    }

    void status(Callback callback) {
        long version = generation.get();
        worker.execute(() -> {
            try { JSONObject saved = vault.read(); requireCurrent(version); done(callback, profile(saved), version); scheduleRevocations(); }
            catch (Exception failure) { failed(callback, failure); }
        });
    }

    private void scheduleRevocations() {
        if (!revocationPending.compareAndSet(false, true)) return;
        revocations.execute(() -> {
            try {
                JSONArray pending = vault.read().optJSONArray("pendingLogout");
                if (pending == null || pending.length() == 0) return;
                String oldToken = pending.getString(0);
                api.request("/api/auth/logout", new JSONObject().put("refreshToken", oldToken), null);
                synchronized (stateLock) {
                    JSONObject current = vault.read();
                    JSONArray list = current.optJSONArray("pendingLogout"), retained = new JSONArray();
                    if (list != null) for (int i = 0; i < list.length(); i++) if (!oldToken.equals(list.optString(i))) retained.put(list.optString(i));
                    current.put("pendingLogout", retained); vault.write(current);
                }
            } catch (Exception unavailable) { /* permanece no cofre para a próxima abertura/conexão */ }
            finally { revocationPending.set(false); }
        });
    }

    private JSONObject fresh(long version) throws Exception {
        return fresh(version, null);
    }

    private JSONObject fresh(long version, String rejectedToken) throws Exception {
        JSONObject saved = vault.read();
        if (!saved.has("refreshToken")) return saved;
        boolean forceRefresh = saved.optBoolean("exigirRenovacao") || (rejectedToken != null && rejectedToken.equals(saved.getString("token")));
        if (!forceRefresh && claims(saved.getString("token")).getLong("exp") * 1000L > System.currentTimeMillis() + 60000) return saved;
        if (forceRefresh) saved.put("exigirRenovacao", true);
        if (saved.optString("refreshRequestId").isEmpty()) saved.put("refreshRequestId", UUID.randomUUID().toString());
        saveCurrent(saved, version);
        JSONObject response;
        try { response = api.request("/api/auth/refresh", new JSONObject().put("refreshToken", saved.getString("refreshToken")).put("requisicaoId", saved.getString("refreshRequestId")), null); }
        catch (SessionHttpClient.Failure rejected) {
            if (rejected.status == 401 || rejected.status == 403) synchronized (stateLock) {
                if (current(version)) {
                    JSONObject cleared = metadataAfterLogout(saved).put("pendingLogout", vault.read().optJSONArray("pendingLogout"));
                    vault.write(cleared); long clearedVersion = generation.incrementAndGet(); changed(false, clearedVersion);
                }
            }
            throw rejected;
        }
        requireCurrent(version);
        String nextToken = response.optString("accessToken", response.optString("token"));
        JSONObject before = claims(saved.getString("token"));
        JSONObject after = claims(nextToken);
        if (!before.getString("sid").equals(after.optString("sid")) || !before.getString("empresaId").equals(after.optString("empresaId"))
            || !before.getString("sub").equals(after.optString("sub")) || !before.getString("dispositivoId").equals(after.optString("dispositivoId"))) {
            throw new SessionHttpClient.Failure(401, "CONTEXTO_ALTERADO", "O acesso da conta mudou. Entre novamente.");
        }
        saved.put("token", nextToken).put("refreshToken", response.getString("refreshToken"));
        saved.remove("refreshRequestId");
        saved.remove("exigirRenovacao");
        // A fila de revogações pode avançar em paralelo, sem ressuscitar um recibo removido.
        synchronized (stateLock) { saved.put("pendingLogout", vault.read().optJSONArray("pendingLogout")); saveCurrent(saved, version); }
        return saved;
    }

    void obtain(Callback callback) {
        obtain(null, callback);
    }

    void obtain(String rejectedToken, Callback callback) {
        long version = generation.get();
        worker.execute(() -> {
            try { JSONObject saved = fresh(version, rejectedToken); requireCurrent(version); done(callback, publicSession(saved), version); scheduleRevocations(); }
            catch (Exception failure) { failed(callback, failure); }
        });
    }

    void signIn(Activity activity, JSONObject context, Callback callback) {
        String deviceId = context.optString("dispositivoId");
        if (deviceId.isEmpty()) {
            worker.execute(() -> {
                try {
                    JSONObject known = vault.read();
                    if (!known.optString("dispositivoId").matches("[A-Za-z0-9_-]{1,64}"))
                        throw new SessionHttpClient.Failure(400, "APARELHO_INVALIDO", "Volte ao início do aplicativo para entrar com Google.");
                    JSONObject recovered = new JSONObject().put("dispositivoId", known.getString("dispositivoId"));
                    if (!known.optString("empresaId").isEmpty()) recovered.put("empresaId", known.getString("empresaId"));
                    main.post(() -> signIn(activity, recovered, callback));
                } catch (Exception failure) { failed(callback, failure); }
            });
            return;
        }
        if (!deviceId.matches("[A-Za-z0-9_-]{1,64}")) {
            failed(callback, new SessionHttpClient.Failure(400, "APARELHO_INVALIDO", "Reabra o início do aplicativo para entrar.")); return;
        }
        long version = newGeneration();
        worker.execute(() -> {
            try {
                JSONObject config = api.request("/api/auth/google/config", null, null);
                if (!config.optBoolean("sessaoNativaHabilitada"))
                    throw new SessionHttpClient.Failure(503, "APP_LOGIN_INDISPONIVEL", "A entrada pelo aplicativo ainda não está disponível. Seus dados foram mantidos. Atualize o EasyStok e tente de novo.");
                if (!config.optBoolean("habilitado") || config.optString("clientId").trim().isEmpty())
                    throw new SessionHttpClient.Failure(503, "GOOGLE_INDISPONIVEL", "A entrada com Google está indisponível. Seus dados foram mantidos.");
                requireCurrent(version);
                main.post(() -> chooseGoogle(activity, context, config.optString("clientId"), true, version, callback));
            } catch (Exception failure) { failed(callback, failure); }
        });
    }

    private void chooseGoogle(Activity activity, JSONObject context, String clientId, boolean authorizedOnly, long version, Callback callback) {
        if (!current(version) || activity.isFinishing() || activity.isDestroyed()) {
            failed(callback, new SessionHttpClient.Failure(401, "SESSAO_ALTERADA", "Reabra o início para entrar.")); return;
        }
        GetGoogleIdOption google = new GetGoogleIdOption.Builder().setServerClientId(clientId)
            .setFilterByAuthorizedAccounts(authorizedOnly).setAutoSelectEnabled(authorizedOnly)
            .setNonce(UUID.randomUUID().toString()).build();
        GetCredentialRequest request = new GetCredentialRequest.Builder().addCredentialOption(google).build();
        credentials.getCredentialAsync(activity, request, new CancellationSignal(), ContextCompat.getMainExecutor(activity),
            new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                @Override public void onResult(GetCredentialResponse result) {
                    try {
                        if (!(result.getCredential() instanceof CustomCredential)
                            || !GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(result.getCredential().getType()))
                            throw new IllegalStateException("Credencial inesperada.");
                        CustomCredential credential = (CustomCredential) result.getCredential();
                        String token = GoogleIdTokenCredential.createFrom(credential.getData()).getIdToken();
                        worker.execute(() -> completeGoogle(context, token, version, callback));
                    } catch (Exception failure) {
                        failed(callback, new SessionHttpClient.Failure(401, "GOOGLE_INVALIDO", "Não foi possível confirmar a conta Google. Tente de novo."));
                    }
                }
                @Override public void onError(GetCredentialException failure) {
                    if (failure instanceof NoCredentialException && authorizedOnly && current(version)) {
                        chooseGoogle(activity, context, clientId, false, version, callback); return;
                    }
                    String code = failure instanceof GetCredentialCancellationException ? "GOOGLE_CANCELADO" : "GOOGLE_NAO_CONFIRMADO";
                    failed(callback, new SessionHttpClient.Failure(0, code,
                        code.equals("GOOGLE_CANCELADO") ? "Entrada cancelada. Seus dados foram mantidos." : "Não foi possível entrar com Google. Confira a conta no aparelho e tente de novo."));
                }
            });
    }

    private void completeGoogle(JSONObject context, String googleToken, long version, Callback callback) {
        JSONObject response = null;
        try {
            requireCurrent(version);
            JSONObject body = new JSONObject().put("idToken", googleToken).put("dispositivoId", context.getString("dispositivoId"));
            if (!context.optString("empresaId").trim().isEmpty()) body.put("empresaId", context.getString("empresaId"));
            response = api.request("/api/auth/google/login", body, null);
            requireCurrent(version);
            JSONObject payload = claims(response.getString("token"));
            if (!"true".equals(payload.optString("sessaoNativa")) || !context.getString("dispositivoId").equals(payload.optString("dispositivoId"))
                || (!context.optString("empresaId").trim().isEmpty() && !context.getString("empresaId").equals(payload.optString("empresaId"))))
                throw new SessionHttpClient.Failure(401, "CONTEXTO_ALTERADO", "A conta não corresponde aos dados deste aparelho. Seus dados foram mantidos.");
            JSONObject previous = vault.read();
            JSONObject saved = new JSONObject().put("token", response.getString("token")).put("refreshToken", response.getString("refreshToken"))
                .put("usuario", response.getJSONObject("usuario")).put("dispositivoId", context.getString("dispositivoId"))
                .put("pendingLogout", previous.optJSONArray("pendingLogout"));
            requireCurrent(version);
            synchronized (stateLock) { saved.put("pendingLogout", vault.read().optJSONArray("pendingLogout")); saveCurrent(saved, version); }
            changed(true, version); done(callback, publicSession(saved), version); scheduleRevocations();
        } catch (Exception failure) {
            if (response != null && response.has("refreshToken")) {
                try { api.request("/api/auth/logout", new JSONObject().put("refreshToken", response.getString("refreshToken")), null); }
                catch (Exception unavailable) { /* a resposta atrasada nunca é instalada no aparelho */ }
            }
            failed(callback, failure);
        }
    }

    void pairing(JSONObject context, Callback callback) {
        long version = generation.get();
        worker.execute(() -> {
            try {
                JSONObject saved = fresh(version);
                if (!saved.has("refreshToken")) throw new SessionHttpClient.Failure(401, "LOGIN_NECESSARIO", "Entre com Google para conectar o aparelho.");
                requireCurrent(version);
                JSONObject existing = saved.optJSONObject("pairing");
                if (existing != null && existing.optString("apiKey").equals(context.optString("apiKeyRejeitada"))) {
                    saved.remove("pairing"); saveCurrent(saved, version); existing = null;
                }
                if (existing != null && (context.optString("lojaId").trim().isEmpty() || context.getString("lojaId").equals(existing.optString("lojaId")))) {
                    done(callback, existing, version); return;
                }
                JSONObject body = new JSONObject().put("dispositivoId", saved.getString("dispositivoId")).put("label", "Celular Casa da Baba");
                if (!context.optString("lojaId").trim().isEmpty()) body.put("lojaId", context.getString("lojaId"));
                String currentKey = context.optString("apiKeyAtual");
                String keyProof = currentKey.trim().isEmpty() ? null : currentKey;
                JSONObject linked;
                try { linked = api.request("/api/mobile/devices/pair-session", body, saved.getString("token"), keyProof); }
                catch (SessionHttpClient.Failure rejected) {
                    if (rejected.status != 401) throw rejected;
                    saved = fresh(version, saved.getString("token"));
                    requireCurrent(version);
                    // A primeira recusa ocorreu antes do pareamento. Só esta operação pode ter um retry.
                    try { linked = api.request("/api/mobile/devices/pair-session", body, saved.getString("token"), keyProof); }
                    catch (SessionHttpClient.Failure repeated) {
                        if (repeated.status == 401) { saved.put("exigirRenovacao", true); saveCurrent(saved, version); }
                        throw repeated;
                    }
                }
                requireCurrent(version);
                if (!claims(saved.getString("token")).getString("empresaId").equals(linked.optString("empresaId"))
                    || !saved.getString("dispositivoId").equals(linked.optString("deviceId")))
                    throw new SessionHttpClient.Failure(409, "CONTEXTO_ALTERADO", "A conexão não corresponde a este aparelho. Nenhum dado foi enviado.");
                synchronized (stateLock) { saved.put("pairing", linked).put("pendingLogout", vault.read().optJSONArray("pendingLogout")); saveCurrent(saved, version); }
                done(callback, linked, version);
            } catch (Exception failure) { failed(callback, failure); }
        });
    }

    private JSONObject metadataAfterLogout(JSONObject saved) throws Exception {
        JSONObject metadata = new JSONObject();
        if (saved.has("dispositivoId")) metadata.put("dispositivoId", saved.getString("dispositivoId"));
        if (saved.has("token")) metadata.put("empresaId", claims(saved.getString("token")).optString("empresaId"));
        else if (saved.has("empresaId")) metadata.put("empresaId", saved.getString("empresaId"));
        return metadata;
    }

    void signOut(Callback callback) {
        try {
            final long version;
            synchronized (stateLock) {
                version = generation.incrementAndGet();
                JSONObject previous = vault.read();
                JSONArray pending = previous.optJSONArray("pendingLogout");
                if (pending == null) pending = new JSONArray();
                if (previous.has("refreshToken")) pending.put(previous.getString("refreshToken"));
                JSONObject cleared = metadataAfterLogout(previous).put("pendingLogout", pending);
                vault.write(cleared);
            }
            changed(false, version); done(callback, new JSONObject().put("autenticado", false), version);
            worker.execute(clearProvider);
            scheduleRevocations();
        } catch (Exception failure) { failed(callback, failure); }
    }

    private void clearGoogleState() {
        try {
            credentials.clearCredentialStateAsync(new ClearCredentialStateRequest(), new CancellationSignal(), worker,
                new CredentialManagerCallback<Void, ClearCredentialException>() {
                    @Override public void onResult(Void result) { }
                    @Override public void onError(ClearCredentialException error) { }
                });
        } catch (Exception unavailable) { /* sair do app não depende do provedor Google */ }
    }
}
