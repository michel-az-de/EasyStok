package com.casadababa.app;

import android.content.Context;
import android.content.ContextWrapper;
import android.content.SharedPreferences;
import android.util.Base64;
import java.nio.charset.StandardCharsets;
import java.util.UUID;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicReference;
import org.json.JSONObject;

// Exercita NativeSession real. Somente o transporte HTTP e as preferências são isolados.
final class NativePairingInstrumentacao {
    private static void check(boolean value, String message) { if (!value) throw new AssertionError(message); }

    static void run(Context context) throws Exception {
        prove(context, 200);
        prove(context, 401);
        prove(context, 403);
        prove(context, 409); // servidor volta a recusar o JWT após a renovação
        prove(context, 410); // Sair enquanto a renovação está interrompida
    }

    private static void prove(Context context, int outcome) throws Exception {
        String prefix = "qa2184-pair-" + UUID.randomUUID() + "-";
        Context isolated = new ContextWrapper(context) {
            @Override public SharedPreferences getSharedPreferences(String name, int mode) {
                return super.getSharedPreferences(prefix + name, mode);
            }
        };
        SessionVault vault = new SessionVault(isolated);
        JSONObject claims = new JSONObject().put("sub", UUID.randomUUID().toString()).put("empresaId", UUID.randomUUID().toString())
            .put("sid", UUID.randomUUID().toString()).put("dispositivoId", "device-fixture").put("sessaoNativa", "true")
            .put("exp", System.currentTimeMillis() / 1000 + 3600);
        String before = token(claims.put("jti", "before")), after = token(claims.put("jti", "after"));
        vault.write(new JSONObject().put("token", before).put("refreshToken", "old")
            .put("usuario", new JSONObject().put("id", claims.getString("sub")).put("nome", "Teste"))
            .put("dispositivoId", claims.getString("dispositivoId")));
        int[] pairs = { 0 }, refreshes = { 0 };
        CountDownLatch refreshing = new CountDownLatch(1), resumeRefresh = new CountDownLatch(1);
        SessionHttpClient api = new SessionHttpClient() {
            @Override JSONObject request(String path, JSONObject body, String jwt, String key) throws Exception {
                if ("/api/auth/refresh".equals(path)) {
                    refreshes[0]++;
                    UUID.fromString(body.getString("requisicaoId"));
                    if (outcome == 410) { refreshing.countDown(); if (!resumeRefresh.await(5, TimeUnit.SECONDS)) throw new Failure(500, "TEST_TIMEOUT", "Renovação de teste não foi retomada."); }
                    if (outcome == 401) throw new Failure(401, "REVOKED", "Sessão encerrada.");
                    return new JSONObject().put("accessToken", after).put("refreshToken", "new").put("expiresIn", 3600);
                }
                if ("/api/auth/logout".equals(path) && outcome == 410) return new JSONObject();
                if (!"/api/mobile/devices/pair-session".equals(path)) throw new Failure(500, "ROUTE_UNEXPECTED", "Rota de negócio não deve ser repetida.");
                pairs[0]++;
                if (outcome == 403) throw new Failure(403, "FORBIDDEN", "Sem permissão para conectar.");
                if (pairs[0] == 1 || outcome == 409) throw new Failure(401, "REJECTED", "Acesso recusado.");
                if (!after.equals(jwt)) throw new Failure(500, "OLD_TOKEN", "O token anterior não pode ser reapresentado.");
                return new JSONObject().put("deviceId", claims.getString("dispositivoId")).put("empresaId", claims.getString("empresaId"))
                    .put("lojaId", "shop-fixture").put("apiKey", "fixture");
            }
        };
        CountDownLatch providerCleared = new CountDownLatch(1);
        NativeSession owner = new NativeSession(isolated, vault, api, providerCleared::countDown);
        AtomicBoolean revoked = new AtomicBoolean();
        owner.addListener((authenticated, version) -> { if (!authenticated) revoked.set(true); });
        CountDownLatch completed = new CountDownLatch(1);
        AtomicReference<JSONObject> response = new AtomicReference<>();
        AtomicReference<SessionHttpClient.Failure> failure = new AtomicReference<>();
        try {
            owner.pairing(new JSONObject(), (value, error) -> { response.set(value); failure.set(error); completed.countDown(); });
            if (outcome == 410) {
                check(refreshing.await(5, TimeUnit.SECONDS), "Pareamento deve chegar à renovação.");
                CountDownLatch signedOut = new CountDownLatch(1);
                owner.signOut((value, error) -> signedOut.countDown());
                check(signedOut.await(5, TimeUnit.SECONDS), "Sair não pode esperar a resposta da renovação.");
                resumeRefresh.countDown();
            }
            check(completed.await(10, TimeUnit.SECONDS), "Pareamento deve terminar sem aguardar outro login.");
            if (outcome == 200) {
                check(failure.get() == null && response.get() != null, "401 deve renovar e concluir o vínculo com um único retry.");
                check(pairs[0] == 2 && refreshes[0] == 1, "Só o pareamento é retentado uma vez.");
                check("new".equals(vault.read().optString("refreshToken")), "Sucessor deve permanecer no cofre.");
            } else if (outcome == 401) {
                check(failure.get() != null && failure.get().status == 401, "Revogação deve retornar necessidade de entrada.");
                check(revoked.get() && !vault.read().has("refreshToken"), "Revogação deve anunciar saída e remover credencial local.");
                check(pairs[0] == 1 && refreshes[0] == 1, "Revogação não deve repetir o pareamento.");
            } else if (outcome == 403) {
                check(failure.get() != null && failure.get().status == 403, "Permissão recusada não é erro de identidade.");
                check(!revoked.get() && vault.read().has("refreshToken"), "403 não encerra a conta Google.");
                check(pairs[0] == 1 && refreshes[0] == 0, "403 não deve disparar renovação.");
            } else if (outcome == 409) {
                check(failure.get() != null && failure.get().status == 401, "Segunda recusa deve ser devolvida à tela.");
                check(pairs[0] == 2 && refreshes[0] == 1, "A recuperação deve ter limite de uma tentativa.");
                check(vault.read().optBoolean("exigirRenovacao"), "Token recusado outra vez não pode voltar pelo cache.");
            } else {
                check(failure.get() != null && "SESSAO_ALTERADA".equals(failure.get().code), "Sair deve prevalecer sobre resposta atrasada.");
                check(revoked.get() && !vault.read().has("refreshToken"), "A renovação não pode restaurar a conta encerrada.");
                check(pairs[0] == 1 && refreshes[0] == 1, "Após Sair não há retry do pareamento.");
                check(providerCleared.await(5, TimeUnit.SECONDS), "Sair usa o efeito de provedor isolado, preservando a conta real.");
            }
        } finally {
            resumeRefresh.countDown();
            isolated.getSharedPreferences("easystok-native-session", Context.MODE_PRIVATE).edit().clear().commit();
        }
    }

    private static String token(JSONObject claims) {
        return "header." + Base64.encodeToString(claims.toString().getBytes(StandardCharsets.UTF_8), Base64.URL_SAFE | Base64.NO_WRAP | Base64.NO_PADDING) + ".signature";
    }
}
