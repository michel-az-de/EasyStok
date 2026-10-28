package com.casadababa.app;

import android.util.Base64;
import org.json.JSONObject;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.security.spec.X509EncodedKeySpec;
import java.util.Arrays;
import java.util.HashSet;
import java.util.Iterator;
import java.util.Set;

/** Wire signatures cover exact UTF-8 bytes; no JSON reserialization during verification. */
public final class SignedNoticePolicy {
    private SignedNoticePolicy() {}
    public static final int MAX_PAYLOAD = 8192;
    public static String installationId(byte[] spki) throws GeneralSecurityException {
        byte[] digest = MessageDigest.getInstance("SHA-256").digest(spki);
        StringBuilder out = new StringBuilder();
        for (byte b : digest) out.append(String.format(java.util.Locale.ROOT, "%02x", b & 255));
        return out.toString();
    }
    public static JSONObject sign(JSONObject payload, PrivateKey key, byte[] spki) throws Exception {
        byte[] bytes = payload.toString().getBytes(StandardCharsets.UTF_8);
        Signature signer = Signature.getInstance("SHA256withECDSA");
        signer.initSign(key); signer.update(bytes);
        return new JSONObject().put("payloadBase64", Base64.encodeToString(bytes, Base64.NO_WRAP))
            .put("signatureBase64", Base64.encodeToString(signer.sign(), Base64.NO_WRAP))
            .put("publicKeyBase64", Base64.encodeToString(spki, Base64.NO_WRAP));
    }
    public static JSONObject verify(JSONObject envelope, byte[] publisherSpki, String installationId, long now, boolean fresh) throws Exception {
        require(envelope.length() == 2 && envelope.has("payloadBase64") && envelope.has("signatureBase64"), "Envelope inválido.");
        byte[] payload = decode(envelope.getString("payloadBase64"), MAX_PAYLOAD);
        byte[] signature = decode(envelope.getString("signatureBase64"), 1024);
        PublicKey key = KeyFactory.getInstance("RSA").generatePublic(new X509EncodedKeySpec(publisherSpki));
        Signature verifier = Signature.getInstance("SHA256withRSA");
        verifier.initVerify(key); verifier.update(payload);
        require(verifier.verify(signature), "Assinatura inválida.");
        JSONObject value = new JSONObject(new String(payload, StandardCharsets.UTF_8));
        Set<String> expected = new HashSet<>(Arrays.asList("kind","id","createdAt","expiresAt","target","nonce","title","body","screen"));
        for (Iterator<String> names = value.keys(); names.hasNext();) require(expected.remove(names.next()), "Campo desconhecido.");
        require(expected.isEmpty(), "Campo ausente.");
        require("cdb-notice-v1".equals(value.getString("kind")), "Tipo inválido.");
        require(value.getString("id").matches("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"), "Identificador inválido.");
        require(installationId.matches("[0-9a-f]{64}") && installationId.equals(value.getString("target")), "Destinatário divergente.");
        require(value.getString("nonce").matches("[A-Za-z0-9_-]{22,128}"), "Nonce inválido.");
        String title = value.getString("title"), body = value.getString("body");
        require(!title.trim().isEmpty() && title.length() <= 100 && !body.trim().isEmpty() && body.length() <= 1200, "Texto inválido.");
        require(NoticePolicy.screenAllowed(value.getString("screen")), "Destino inválido.");
        long created = value.getLong("createdAt"), expires = value.getLong("expiresAt");
        require(created > 0 && expires > created && expires - created <= 7L * 86400000, "Validade inválida.");
        if (fresh) require(created <= now + 300000 && expires > now, "Aviso fora da validade.");
        return value;
    }
    public interface Acceptor { void accept(JSONObject envelope) throws Exception; }
    /** One invalid or expired message must not hold back the rest of the inbox. Returns how many were skipped. */
    public static int acceptEach(org.json.JSONArray messages, Acceptor acceptor) {
        int skipped = 0;
        for (int i = 0; i < messages.length(); i++) {
            try { acceptor.accept(messages.getJSONObject(i)); }
            catch (Exception rejected) { skipped++; }
        }
        return skipped;
    }
    static byte[] decode(String value, int maximum) {
        require(value != null && value.length() <= ((maximum + 2) / 3) * 4, "Campo excede limite.");
        byte[] decoded = Base64.decode(value, Base64.NO_WRAP);
        require(decoded.length <= maximum && Base64.encodeToString(decoded, Base64.NO_WRAP).equals(value), "Base64 inválida.");
        return decoded;
    }
    static void require(boolean condition, String message) { if (!condition) throw new IllegalArgumentException(message); }
}
