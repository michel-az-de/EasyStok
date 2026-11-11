package com.casadababa.app;

import android.app.Activity;
import android.app.Instrumentation;
import android.content.Context;
import android.content.ContextWrapper;
import android.content.SharedPreferences;
import android.net.Uri;
import android.os.Bundle;
import androidx.credentials.CredentialManager;
import com.google.android.libraries.identity.googleid.GetGoogleIdOption;
import org.json.JSONObject;

// Teste no Android real, em preferências próprias. Nunca altera sessão ou dados da operação.
public final class NativeSessionInstrumentacao extends Instrumentation {
    private String phase;
    @Override public void onCreate(Bundle arguments) { super.onCreate(arguments); phase = arguments.getString("phase", "prepare"); start(); }
    private static void check(boolean valid, String message) { if (!valid) throw new AssertionError(message); }
    @Override public void onStart() {
        Bundle result = new Bundle();
        try {
            if ("pairing".equals(phase)) {
                NativePairingInstrumentacao.run(getTargetContext());
                result.putString("stream", "\n#2184 pairing: renovação, revogação e limite de tentativas aprovados.\n");
                finish(Activity.RESULT_OK, result); return;
            }
            Context isolated = new ContextWrapper(getTargetContext()) {
                @Override public SharedPreferences getSharedPreferences(String name, int mode) {
                    return super.getSharedPreferences("qa2184-" + name, mode);
                }
            };
            SharedPreferences prefs = isolated.getSharedPreferences("easystok-native-session", Context.MODE_PRIVATE);
            SessionVault vault = new SessionVault(isolated);
            if ("prepare".equals(phase)) {
                check(!prefs.contains("encrypted"), "Preferências de teste devem começar vazias.");
                vault.write(new JSONObject().put("refreshToken", "fixture"));
                String ciphertext = prefs.getString("encrypted", "");
                check(ciphertext.startsWith("v1.") && !ciphertext.contains("fixture"), "Cofre deve guardar somente ciphertext.");
                check(CredentialManager.create(getTargetContext()) != null, "Credential Manager disponível.");
                check(new GetGoogleIdOption.Builder().setServerClientId("qa2184.apps.googleusercontent.com")
                    .setFilterByAuthorizedAccounts(false).build() != null, "SDK Google compatível.");
            } else if ("resume".equals(phase)) {
                check("fixture".equals(vault.read().getString("refreshToken")), "Cofre retoma após processo encerrado.");
                String ciphertext = prefs.getString("encrypted", "");
                String[] parts = ciphertext.split("\\.");
                parts[2] = (parts[2].charAt(0) == 'A' ? "B" : "A") + parts[2].substring(1);
                prefs.edit().putString("encrypted", String.join(".", parts)).commit();
                boolean rejected = false;
                try { vault.read(); } catch (Exception tampered) { rejected = true; }
                check(rejected, "Credencial adulterada deve ser recusada.");
                check(prefs.edit().clear().commit(), "Limpar somente a fixture de teste.");
            } else throw new AssertionError("Fase de teste desconhecida.");
            check(WorkspaceSessionBridge.trusted(Uri.parse("https://app.easystok.online/")), "Origem canônica permitida.");
            for (String origin : new String[] { "http://app.easystok.online/", "https://app.easystok.online.evil/", "https://app.easystok.online:8443/", "https://alguem@app.easystok.online/" })
                check(!WorkspaceSessionBridge.trusted(Uri.parse(origin)), "Origem fora do contrato recusada.");
            result.putString("stream", "\n#2184 " + phase + ": cofre Android, SDK Google e fronteira de origem aprovados.\n");
            finish(Activity.RESULT_OK, result);
        } catch (Throwable failure) {
            result.putString("stream", "\n#2184 FALHOU: " + failure.getClass().getSimpleName() + ": " + failure.getMessage() + "\n");
            finish(Activity.RESULT_CANCELED, result);
        }
    }
}
