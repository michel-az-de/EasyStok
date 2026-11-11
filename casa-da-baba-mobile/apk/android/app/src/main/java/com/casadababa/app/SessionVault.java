package com.casadababa.app;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;
import org.json.JSONObject;

// Credenciais não entram no IndexedDB, backup público nem no JavaScript remoto.
final class SessionVault {
    private static final String ALIAS = "com.casadababa.piloto.session.v1";
    private final SharedPreferences preferences;

    SessionVault(Context context) {
        preferences = context.getSharedPreferences("easystok-native-session", Context.MODE_PRIVATE);
    }

    private SecretKey key() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        store.load(null);
        if (store.containsAlias(ALIAS)) return (SecretKey) store.getKey(ALIAS, null);
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setRandomizedEncryptionRequired(true).build());
        return generator.generateKey();
    }

    synchronized JSONObject read() throws Exception {
        String saved = preferences.getString("encrypted", null);
        if (saved == null) return new JSONObject();
        String[] parts = saved.split("\\.", -1);
        if (parts.length != 3 || !"v1".equals(parts[0])) throw new IllegalStateException("Sessão ilegível.");
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, Base64.decode(parts[1], Base64.NO_WRAP)));
        return new JSONObject(new String(cipher.doFinal(Base64.decode(parts[2], Base64.NO_WRAP)), StandardCharsets.UTF_8));
    }

    synchronized void write(JSONObject data) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key());
        String saved = "v1." + Base64.encodeToString(cipher.getIV(), Base64.NO_WRAP) + "."
            + Base64.encodeToString(cipher.doFinal(data.toString().getBytes(StandardCharsets.UTF_8)), Base64.NO_WRAP);
        // Confirmar a escrita antes de anunciar um refresh novo que substitui o anterior.
        if (!preferences.edit().putString("encrypted", saved).commit()) throw new IllegalStateException("Sessão não pôde ser guardada.");
    }
}
