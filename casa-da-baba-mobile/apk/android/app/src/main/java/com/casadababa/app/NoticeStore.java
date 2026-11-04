package com.casadababa.app;

import android.util.AtomicFile;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.*;
import java.nio.charset.StandardCharsets;

/** The inbox and receipt outbox are committed together, before presenting a notification. */
public final class NoticeStore {
    private final AtomicFile file;
    private JSONObject state;
    public NoticeStore(File location) throws Exception {
        file = new AtomicFile(location);
        try (InputStream input = file.openRead()) {
            state = new JSONObject(new String(read(input, 2097152), StandardCharsets.UTF_8));
            state.getJSONArray("items"); state.getJSONArray("pending");
        } catch (FileNotFoundException absent) {
            state = new JSONObject().put("items", new JSONArray()).put("pending", new JSONArray());
        }
    }
    public synchronized JSONObject snapshot() throws Exception { return new JSONObject(state.toString()); }
    public synchronized void update(String key, Object value) throws Exception {
        JSONObject next = snapshot(); next.put(key, value); commit(next);
    }
    public synchronized boolean accept(JSONObject envelope, JSONObject notice, String source, long now, String version) throws Exception {
        SignedNoticePolicy.require("push".equals(source) || "sync".equals(source), "Origem inválida.");
        JSONObject next = snapshot(); JSONArray items = next.getJSONArray("items");
        JSONObject item = find(items, notice.getString("id"));
        boolean fresh = item == null;
        if (fresh) {
            // Never evict an item while its acknowledgement is awaiting upload.
            while (items.length() >= 100) {
                int removable = -1;
                for (int i = 0; i < items.length(); i++) {
                    JSONObject candidate = items.getJSONObject(i);
                    if (pendingFor(next, candidate.getString("id"))) continue;
                    if (removable < 0 || candidate.getLong("createdAt") < items.getJSONObject(removable).getLong("createdAt")
                        || (candidate.getLong("createdAt") == items.getJSONObject(removable).getLong("createdAt")
                            && candidate.getString("id").compareTo(items.getJSONObject(removable).getString("id")) < 0)) removable = i;
                }
                SignedNoticePolicy.require(removable >= 0, "Histórico aguardando confirmação. Conecte-se para continuar.");
                items.remove(removable);
            }
            item = new JSONObject(notice.toString()).put("receivedAt", now).put("openedAt", 0)
                .put("source", source).put("envelope", envelope);
            items.put(item);
        } else {
            SignedNoticePolicy.require(item.getJSONObject("envelope").getString("payloadBase64").equals(envelope.getString("payloadBase64")), "Aviso repetido com conteúdo divergente.");
        }
        String marker = "push".equals(source) ? "pushAt" : "syncAt";
        boolean firstSource = item.optLong(marker, 0) == 0;
        if (firstSource) {
            item.put(marker, now);
            enqueue(next, notice, "received", source, now, version);
        }
        if ("push".equals(source)) { item.put("source", "push"); next.put("lastPushAt", now); }
        else next.put("lastSyncAt", now);
        commit(next);
        // A late FCM delivery still records its receipt, but an opened notice is not announced again.
        return fresh || ("push".equals(source) && firstSource && item.optLong("openedAt", 0) == 0);
    }
    /**
     * A device transfer copies this file but not the Keystore key. Receipts signed by the lost key
     * cannot be sent, so they are dropped; the visible history stays. Returns the discarded receipts.
     */
    public synchronized int resetIdentity(String installationId) throws Exception {
        JSONObject next = snapshot();
        int discarded = next.getJSONArray("pending").length();
        JSONArray items = next.getJSONArray("items");
        // Old notices are bound to the lost installation on the server: opening them must not queue a receipt.
        for (int i = 0; i < items.length(); i++) items.getJSONObject(i).put("receiptless", true);
        next.put("pending", new JSONArray()).put("installationId", installationId).put("registered", false);
        next.remove("lastRegistrationAt"); next.remove("lastRegistrationIssuedAt");
        commit(next);
        return discarded;
    }
    public synchronized void opened(String id, long now, String version) throws Exception {
        JSONObject next = snapshot(); JSONObject item = find(next.getJSONArray("items"), id);
        SignedNoticePolicy.require(item != null, "Aviso não encontrado.");
        if (item.optLong("openedAt", 0) == 0) {
            item.put("openedAt", now);
            enqueue(next, item, "opened", item.getString("source"), now, version);
        }
        if (id.equals(next.optString("pendingOpenId"))) next.remove("pendingOpenId");
        commit(next);
    }
    public synchronized void pendingOpen(String id) throws Exception {
        if (find(state.getJSONArray("items"), id) != null) update("pendingOpenId", id);
    }
    public synchronized void acknowledged(String key) throws Exception {
        JSONObject next = snapshot(); JSONArray queue = next.getJSONArray("pending");
        for (int i = queue.length() - 1; i >= 0; i--) if (key.equals(queue.getJSONObject(i).getString("key"))) queue.remove(i);
        commit(next);
    }
    private static void enqueue(JSONObject next, JSONObject item, String event, String source, long now, String version) throws Exception {
        if (item.optBoolean("receiptless", false)) return;
        JSONArray queue = next.getJSONArray("pending"); String key = item.getString("id") + ":" + event + ":" + source;
        for (int i = 0; i < queue.length(); i++) if (key.equals(queue.getJSONObject(i).getString("key"))) return;
        SignedNoticePolicy.require(queue.length() < 400, "Confirmações aguardando conexão.");
        JSONObject receipt = new JSONObject().put("kind", "cdb-receipt-v1").put("installationId", item.getString("target"))
            .put("messageId", item.getString("id")).put("nonce", item.getString("nonce")).put("event", event)
            .put("source", source).put("occurredAt", now).put("apkVersion", version);
        queue.put(new JSONObject().put("key", key).put("payload", receipt));
    }
    private static boolean pendingFor(JSONObject state, String id) throws Exception {
        JSONArray queue = state.getJSONArray("pending");
        for (int i = 0; i < queue.length(); i++) if (id.equals(queue.getJSONObject(i).getJSONObject("payload").getString("messageId"))) return true;
        return false;
    }
    public static JSONObject find(JSONArray array, String id) throws Exception {
        for (int i = 0; i < array.length(); i++) if (id.equals(array.getJSONObject(i).getString("id"))) return array.getJSONObject(i);
        return null;
    }
    private void commit(JSONObject next) throws Exception {
        byte[] bytes = next.toString().getBytes(StandardCharsets.UTF_8);
        SignedNoticePolicy.require(bytes.length <= 2097152, "Histórico excedeu o limite.");
        FileOutputStream stream = null;
        try { stream = file.startWrite(); stream.write(bytes); file.finishWrite(stream); state = next; }
        catch (Exception failure) { if (stream != null) file.failWrite(stream); throw failure; }
    }
    static byte[] read(InputStream input, int maximum) throws IOException {
        ByteArrayOutputStream output = new ByteArrayOutputStream(); byte[] buffer = new byte[4096]; int count;
        while ((count = input.read(buffer)) != -1) { if (output.size() + count > maximum) throw new IOException("Resposta excedeu o limite."); output.write(buffer, 0, count); }
        return output.toByteArray();
    }
}
