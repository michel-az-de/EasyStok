package com.casadababa.app;

import android.util.AtomicFile;
import android.util.Base64;
import org.json.JSONArray;
import org.json.JSONObject;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.io.File;
import java.security.*;
import java.security.spec.ECGenParameterSpec;

/** Host tests cover cryptography and durable inbox semantics, not FCM or Android Keystore hardware. */
public final class SignedNoticeTest {
    private static int assertions;
    private static final long NOW = 1800000000000L;
    public static void main(String[] args) throws Exception {
        KeyPairGenerator rsa = KeyPairGenerator.getInstance("RSA"); rsa.initialize(3072); KeyPair publisher = rsa.generateKeyPair();
        KeyPairGenerator ec = KeyPairGenerator.getInstance("EC"); ec.initialize(new ECGenParameterSpec("secp256r1")); KeyPair installation = ec.generateKeyPair();
        String id = SignedNoticePolicy.installationId(installation.getPublic().getEncoded());
        check(id.matches("[a-f0-9]{64}"));
        JSONObject notice = notice(id); JSONObject envelope = sign(notice, publisher);
        check(SignedNoticePolicy.verify(envelope, publisher.getPublic().getEncoded(), id, NOW, true).getString("title").equals("Aviso de teste"));
        JSONObject tampered = new JSONObject(envelope.toString()).put("payloadBase64", Base64.encodeToString(notice.toString().replace("Aviso", "Outro").getBytes(StandardCharsets.UTF_8), Base64.NO_WRAP));
        fails(() -> SignedNoticePolicy.verify(tampered, publisher.getPublic().getEncoded(), id, NOW, true));
        KeyPair wrong = rsa.generateKeyPair();
        fails(() -> SignedNoticePolicy.verify(envelope, wrong.getPublic().getEncoded(), id, NOW, true));
        fails(() -> SignedNoticePolicy.verify(envelope, publisher.getPublic().getEncoded(), "0".repeat(64), NOW, true));
        fails(() -> SignedNoticePolicy.verify(envelope, publisher.getPublic().getEncoded(), id, NOW + 90000000, true));
        fails(() -> SignedNoticePolicy.verify(envelope, publisher.getPublic().getEncoded(), id, NOW - 400000, true));
        check(SignedNoticePolicy.verify(envelope, publisher.getPublic().getEncoded(), id, NOW + 90000000, false).getString("id").equals(notice.getString("id")));
        for (String field : new String[]{"nonce", "screen", "kind", "id", "title", "body"}) {
            JSONObject invalid = new JSONObject(notice.toString()).put(field, field.equals("screen") ? "https://evil.test" : "");
            JSONObject signed = sign(invalid, publisher);
            fails(() -> SignedNoticePolicy.verify(signed, publisher.getPublic().getEncoded(), id, NOW, true));
        }
        JSONObject extra = sign(new JSONObject(notice.toString()).put("url", "https://evil.test"), publisher);
        fails(() -> SignedNoticePolicy.verify(extra, publisher.getPublic().getEncoded(), id, NOW, true));
        JSONObject missing = new JSONObject(notice.toString()); missing.remove("nonce"); JSONObject missingEnvelope = sign(missing, publisher);
        fails(() -> SignedNoticePolicy.verify(missingEnvelope, publisher.getPublic().getEncoded(), id, NOW, true));
        JSONObject receipt = new JSONObject().put("kind", "cdb-receipt-v1").put("installationId", id).put("event", "received");
        JSONObject signedReceipt = SignedNoticePolicy.sign(receipt, installation.getPrivate(), installation.getPublic().getEncoded());
        Signature verify = Signature.getInstance("SHA256withECDSA"); verify.initVerify(installation.getPublic());
        verify.update(Base64.decode(signedReceipt.getString("payloadBase64"), Base64.NO_WRAP));
        check(verify.verify(Base64.decode(signedReceipt.getString("signatureBase64"), Base64.NO_WRAP)));
        check(SignedNoticePolicy.installationId(Base64.decode(signedReceipt.getString("publicKeyBase64"), Base64.NO_WRAP)).equals(id));
        File folder = Files.createTempDirectory("cdb-notices-test").toFile(); File file = new File(folder, "notices.json");
        try {
            NoticeStore store = new NoticeStore(file);
            check(store.accept(envelope, notice, "sync", NOW, "0.6.0"));
            check(store.snapshot().getJSONArray("pending").length() == 1);
            check(!store.accept(envelope, notice, "sync", NOW, "0.6.0"));
            check(store.snapshot().getJSONArray("pending").length() == 1);
            check(store.accept(envelope, notice, "push", NOW + 1, "0.6.0"));
            check(!store.accept(envelope, notice, "push", NOW + 1, "0.6.0"));
            check(store.snapshot().getJSONArray("items").length() == 1);
            check(store.snapshot().getJSONArray("pending").length() == 2);
            check(store.snapshot().getJSONArray("items").getJSONObject(0).getString("source").equals("push"));
            store = new NoticeStore(file);
            check(store.snapshot().getJSONArray("pending").length() == 2);
            store.pendingOpen(notice.getString("id"));
            check(store.snapshot().getJSONArray("items").getJSONObject(0).getLong("openedAt") == 0);
            store.opened(notice.getString("id"), NOW + 2, "0.6.0");
            store.opened(notice.getString("id"), NOW + 3, "0.6.0");
            check(store.snapshot().getJSONArray("pending").length() == 3);
            check(!store.snapshot().has("pendingOpenId"));
            JSONArray queue = store.snapshot().getJSONArray("pending");
            check(queue.getJSONObject(0).getJSONObject("payload").getString("source").equals("sync"));
            check(queue.getJSONObject(1).getJSONObject("payload").getString("source").equals("push"));
            check(queue.getJSONObject(2).getJSONObject("payload").getString("event").equals("opened"));
            store.acknowledged(queue.getJSONObject(0).getString("key"));
            store.acknowledged(queue.getJSONObject(0).getString("key"));
            check(store.snapshot().getJSONArray("pending").length() == 2);
            JSONObject different = new JSONObject(notice.toString()).put("body", "Alterado"); JSONObject otherEnvelope = sign(different, publisher);
            final NoticeStore captured = store;
            fails(() -> captured.accept(otherEnvelope, different, "push", NOW + 4, "0.6.0"));
            check(captured.snapshot().getJSONArray("items").getJSONObject(0).getString("body").equals("Conteúdo público."));
            JSONObject next = new JSONObject(notice.toString()).put("id", "22be2d87-d6ef-4274-894b-3d928fe0c95a"); JSONObject nextEnvelope = sign(next, publisher);
            AtomicFile.failNextWrite = true;
            fails(() -> captured.accept(nextEnvelope, next, "push", NOW + 4, "0.6.0"));
            check(captured.snapshot().getJSONArray("items").length() == 1);
            check(new NoticeStore(file).snapshot().getJSONArray("items").length() == 1);
            fails(() -> captured.opened("missing", NOW, "0.6.0"));
            fails(() -> captured.accept(envelope, notice, "polling-as-push", NOW, "0.6.0"));
        } finally { Files.deleteIfExists(file.toPath()); Files.deleteIfExists(new File(file + ".new").toPath()); Files.deleteIfExists(folder.toPath()); }
        testTokenRotation();
        testEviction(id, publisher);
        System.out.println("PASS: " + assertions + " signed notice and durable receipt assertions; Android Keystore/FCM need device validation");
    }
    private static void testTokenRotation() throws Exception {
        TokenRegistration registration = new TokenRegistration();
        java.util.concurrent.atomic.AtomicBoolean persisted = new java.util.concurrent.atomic.AtomicBoolean(true);
        java.util.concurrent.atomic.AtomicReference<Throwable> failure = new java.util.concurrent.atomic.AtomicReference<>();
        java.util.concurrent.CountDownLatch waitingResponse = new java.util.concurrent.CountDownLatch(1);
        java.util.concurrent.CountDownLatch deliverResponse = new java.util.concurrent.CountDownLatch(1);
        long tokenA = registration.begin();
        Thread responseA = new Thread(() -> {
            try {
                waitingResponse.countDown();
                if (!deliverResponse.await(10, java.util.concurrent.TimeUnit.SECONDS)) throw new AssertionError("Response not released");
                if (registration.complete(tokenA, () -> persisted.set(true))) throw new AssertionError("Stale token accepted");
            } catch (Throwable problem) { failure.set(problem); }
        });
        responseA.setDaemon(true); responseA.start();
        check(waitingResponse.await(10, java.util.concurrent.TimeUnit.SECONDS));
        registration.changed(() -> persisted.set(false));
        deliverResponse.countDown(); responseA.join(10000);
        check(!responseA.isAlive() && failure.get() == null);
        check(!persisted.get());
        long tokenB = registration.begin();
        check(registration.complete(tokenB, () -> persisted.set(true)));
        check(persisted.get());
        // Rotation after a completed response must still invalidate that persisted success.
        registration.changed(() -> persisted.set(false));
        check(!persisted.get());
        check(!registration.complete(tokenB, () -> persisted.set(true)));
    }
    private static void testEviction(String target, KeyPair publisher) throws Exception {
        File folder = Files.createTempDirectory("cdb-notice-eviction").toFile(); File file = new File(folder, "notices.json");
        try {
            NoticeStore store = new NoticeStore(file);
            // The inbox response is newest first. The oldest item intentionally has a pending receipt.
            for (int i = 99; i >= 0; i--) {
                JSONObject value = notice(target).put("id", historyId(i)).put("createdAt", NOW + i);
                store.accept(sign(value, publisher), value, "sync", NOW + 200, "0.6.0");
                if (i != 0) store.acknowledged(historyId(i) + ":received:sync");
            }
            JSONObject newest = notice(target).put("id", historyId(100)).put("createdAt", NOW + 100);
            store.accept(sign(newest, publisher), newest, "push", NOW + 201, "0.6.0");
            JSONArray items = store.snapshot().getJSONArray("items");
            check(items.length() == 100);
            check(NoticeStore.find(items, historyId(0)) != null);
            check(NoticeStore.find(items, historyId(1)) == null);
            check(NoticeStore.find(items, historyId(99)) != null);
            check(NoticeStore.find(items, historyId(100)) != null);
            check(store.snapshot().getJSONArray("pending").length() == 2);
            check(new NoticeStore(file).snapshot().getJSONArray("items").length() == 100);
        } finally { Files.deleteIfExists(file.toPath()); Files.deleteIfExists(new File(file + ".new").toPath()); Files.deleteIfExists(folder.toPath()); }
    }
    private static String historyId(int index) { return String.format(java.util.Locale.ROOT, "00000000-0000-4000-8000-%012d", index); }
    static JSONObject notice(String target) throws Exception {
        return new JSONObject().put("kind", "cdb-notice-v1").put("id", "11be2d87-d6ef-4274-894b-3d928fe0c95a")
            .put("createdAt", NOW).put("expiresAt", NOW + 86400000).put("target", target)
            .put("nonce", "abcdefghijklmnopqrstuvwx").put("title", "Aviso de teste").put("body", "Conteúdo público.").put("screen", "home");
    }
    static JSONObject sign(JSONObject value, KeyPair key) throws Exception {
        byte[] bytes = value.toString().getBytes(StandardCharsets.UTF_8); Signature signer = Signature.getInstance("SHA256withRSA");
        signer.initSign(key.getPrivate()); signer.update(bytes);
        return new JSONObject().put("payloadBase64", Base64.encodeToString(bytes, Base64.NO_WRAP)).put("signatureBase64", Base64.encodeToString(signer.sign(), Base64.NO_WRAP));
    }
    interface Attempt { void run() throws Exception; }
    static void fails(Attempt action) throws Exception { boolean failed = false; try { action.run(); } catch (Exception expected) { failed = true; } check(failed); }
    static void check(boolean ok) { assertions++; if (!ok) throw new AssertionError("Assertion " + assertions); }
}
