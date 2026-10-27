package com.casadababa.app.updater;

import android.content.Context;
import android.util.AtomicFile;
import java.io.*;
import java.lang.reflect.Constructor;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.*;
import java.util.*;
import java.util.zip.*;
import org.json.JSONObject;

public class UpdaterTest {
    private static int assertions;
    private static final String URL = "https://easystok.online/downloads/casa-da-baba/ota/abc/bundle.zip";
    private static final String HASH = "a".repeat(64);
    private static Path temporary;
    interface Action { void run() throws Exception; }

    private static void check(boolean value, String message) {
        assertions++;
        if (!value) throw new AssertionError(message);
    }
    private static void rejects(Action action, String message) throws Exception {
        try { action.run(); } catch (IOException expected) { assertions++; return; }
        throw new AssertionError(message);
    }
    private static Path directory(String name) throws IOException { return Files.createTempDirectory(temporary, name); }
    private static JSONObject builtin() {
        return new JSONObject().put("builtin", true).put("bundleVersion", "0.3.0")
            .put("nativeVersionCode", 3).put("dataSchemaVersion", 2);
    }
    private static JSONObject release(String version, String hash, long sequence) {
        return new JSONObject().put("schemaVersion", 1).put("appId", "com.casadababa.piloto").put("channel", "piloto")
            .put("sequence", sequence).put("bundleVersion", version).put("nativeVersionCode", 3)
            .put("dataSchemaVersion", 2).put("url", URL).put("sizeBytes", 123).put("sha256", hash);
    }
    private static JSONObject state(JSONObject active, JSONObject previous, JSONObject pending, boolean trial) {
        return new JSONObject().put("schemaVersion", 1).put("maxSequence", 10).put("active", active)
            .put("previous", previous).put("pending", pending).put("trial", trial).put("rolledBack", false);
    }
    private static void seed(Path files, JSONObject value, JSONObject... releases) throws Exception {
        Files.createDirectories(files.resolve("ota/bundles"));
        Files.writeString(files.resolve("ota/state.json"), value.toString());
        for (JSONObject release : releases) {
            Path target = files.resolve("ota/bundles/" + release.getString("sha256"));
            Files.createDirectories(target);
            Files.writeString(target.resolve("index.html"), "<html>validated test fixture</html>");
            Files.writeString(target.resolve("bundle.json"), release.toString());
        }
    }
    private static JSONObject disk(Path files) throws Exception { return new JSONObject(Files.readString(files.resolve("ota/state.json"))); }
    private static OtaManager manager(Path files) throws Exception {
        Constructor<OtaManager> constructor = OtaManager.class.getDeclaredConstructor(Context.class);
        constructor.setAccessible(true);
        return constructor.newInstance(new Context(files.toFile()));
    }

    private static void stateTests() throws Exception {
        JSONObject next = release("0.3.1", HASH, 10);
        Path files = directory("activation");
        seed(files, state(builtin(), null, next, false), next);
        OtaManager app = manager(files);
        check(app.status().getString("activeVersion").equals("0.3.0"), "Pending must not change running version");
        check(app.beginBoot(false).endsWith(HASH), "Cold boot applies prepared bundle");
        check(disk(files).getBoolean("trial"), "Trial persisted before code runs");
        rejects(() -> app.ready("wrong"), "Wrong acknowledgment rejected");
        check(app.status().getBoolean("trial"), "Wrong acknowledgment cannot mark healthy");
        check(app.beginBoot(true).endsWith(HASH), "Same-process recreation retains trial");
        app.ready("0.3.1");
        check(!disk(files).getBoolean("trial"), "Health acknowledgment persisted");
        check(manager(files).beginBoot(false).endsWith(HASH), "Healthy version survives restart");

        app.rollback();
        check(app.status().getString("activeVersion").equals("0.3.1"), "Rollback does not reload running app");
        check(app.status().getString("pendingVersion").equals("0.3.0"), "Rollback queued");
        OtaManager rolled = manager(files);
        check(rolled.beginBoot(false) == null, "Manual rollback selects embedded previous");
        check(rolled.status().getBoolean("rolledBack"), "Rollback visible");
        check(disk(files).getLong("maxSequence") == 10, "Rollback preserves anti-replay sequence");

        Path unconfirmed = directory("trial");
        seed(unconfirmed, state(next, builtin(), null, true).put("trialBoots", UpdatePolicy.MAX_TRIAL_BOOTS), next);
        OtaManager restarted = manager(unconfirmed);
        check(restarted.beginBoot(true) == null, "Process restart with saved Activity still rolls back unacked trial");
        check(restarted.status().getBoolean("rolledBack"), "Automatic rollback visible");
        check(disk(unconfirmed).getLong("maxSequence") == 10, "Failed sequence remains blocked");

        Path trialRollback = directory("trial-rollback");
        seed(trialRollback, state(next, builtin(), null, true), next);
        OtaManager trialApp = manager(trialRollback);
        trialApp.rollback();
        check(manager(trialRollback).beginBoot(false) == null, "Manual rollback during trial recovers previous");
        check(!disk(trialRollback).optBoolean("pendingRollback"), "Trial fallback clears manual rollback marker for future updates");

        Path missing = directory("missing");
        JSONObject broken = release("0.3.2", "b".repeat(64), 11);
        seed(missing, state(broken, next, null, true), next);
        OtaManager recovery = manager(missing);
        check(recovery.beginBoot(false).endsWith(HASH), "Missing active chooses good previous before embedded version");
        check(recovery.status().getString("activeVersion").equals("0.3.1"), "Backup identity retained");
        check(!recovery.status().getBoolean("trial"), "Recovery clears failed trial");

        Path writeFailed = directory("ack-failed");
        seed(writeFailed, state(next, builtin(), null, true).put("trialBoots", UpdatePolicy.MAX_TRIAL_BOOTS), next);
        OtaManager failing = manager(writeFailed);
        AtomicFile.failNextWrite = true;
        rejects(() -> failing.ready("0.3.1"), "Failed save cannot acknowledge readiness");
        check(failing.status().getBoolean("trial"), "Memory restored after failed acknowledgment save");
        check(disk(writeFailed).getBoolean("trial"), "Disk remains unacknowledged");
        rejects(() -> failing.ready("0.3.1"), "Failed state is closed until restart");
        check(failing.beginBoot(false) == null, "Broken state serves embedded assets in same process");
        check(failing.status().getString("activeVersion").equals("0.3.0"), "Broken-state status matches actually served assets");
        check(manager(writeFailed).beginBoot(false) == null, "Write failure still recovers previous on restart");

        Path rollbackFailed = directory("rollback-failed");
        seed(rollbackFailed, state(next, builtin(), null, false), next);
        OtaManager failRollback = manager(rollbackFailed);
        AtomicFile.failNextWrite = true;
        rejects(failRollback::rollback, "Rollback save failure reported");
        check(failRollback.status().getString("pendingVersion").isEmpty(), "Failed rollback does not linger in memory");
        check(!disk(rollbackFailed).has("pending"), "Failed rollback does not change persisted pending");

        Path activationFailed = directory("activation-failed");
        seed(activationFailed, state(builtin(), null, next, false), next);
        OtaManager failActivation = manager(activationFailed);
        AtomicFile.failNextWrite = true;
        check(failActivation.beginBoot(false) == null, "Failed trial save runs embedded version only");
        check(!disk(activationFailed).getBoolean("trial"), "Failed activation never claims persisted trial");
        check(disk(activationFailed).getJSONObject("active").getBoolean("builtin"), "Old active survives failed trial save");
    }

    /** Revisão da PR #2066: achados 1, 2, 3 e 5. */
    private static void reviewTests() throws Exception {
        JSONObject next = release("0.3.1", HASH, 10);

        // Achado 1: atualizar o APK é o caminho esperado, não uma recuperação.
        Path upgraded = directory("apk-upgrade");
        JSONObject oldBuiltin = builtin().put("nativeVersionCode", 2).put("bundleVersion", "0.2.0");
        seed(upgraded, state(oldBuiltin, null, null, false));
        OtaManager afterUpgrade = manager(upgraded);
        check(afterUpgrade.beginBoot(false) == null, "APK upgrade serves the new embedded assets");
        check(!afterUpgrade.status().getBoolean("rolledBack"), "APK upgrade from builtin is not reported as recovery");
        check(afterUpgrade.status().getString("lastError").isEmpty(), "APK upgrade from builtin shows no error");
        check(disk(upgraded).getJSONObject("active").getInt("nativeVersionCode") == 3, "New APK identity persisted");
        Path upgradedOta = directory("apk-upgrade-ota");
        JSONObject oldOta = release("0.2.5", "c".repeat(64), 9).put("nativeVersionCode", 2);
        seed(upgradedOta, state(oldOta, oldBuiltin, null, false), oldOta);
        OtaManager afterOtaUpgrade = manager(upgradedOta);
        check(afterOtaUpgrade.beginBoot(false) == null, "APK upgrade over an OTA bundle serves the new embedded assets");
        check(!afterOtaUpgrade.status().getBoolean("rolledBack"), "APK upgrade over OTA is not reported as recovery");
        check(afterOtaUpgrade.status().getString("lastError").isEmpty(), "APK upgrade over OTA shows no error");
        check(!afterOtaUpgrade.status().getBoolean("canRollback"), "Old APK content is never offered as rollback");
        check(disk(upgradedOta).getLong("maxSequence") == 10, "APK upgrade preserves anti-replay sequence");
        Path corrupt = directory("same-apk-missing");
        seed(corrupt, state(release("0.3.2", "d".repeat(64), 11), null, null, false));
        OtaManager missingActive = manager(corrupt);
        missingActive.beginBoot(false);
        check(missingActive.status().getBoolean("rolledBack"), "Missing bundle on the same APK is still a visible recovery");

        // Achado 2: depois de voltar, a versão rejeitada não volta pelo mesmo botão.
        Path toggle = directory("rollback-toggle");
        seed(toggle, state(next, builtin(), null, false), next);
        manager(toggle).rollback();
        OtaManager back = manager(toggle);
        check(back.beginBoot(false) == null, "Rollback applies the older version");
        check(!back.status().getBoolean("canRollback"), "Rejected version is not offered as rollback target");
        check(back.status().getString("previousVersion").isEmpty(), "Rejected version is not kept as previous");
        rejects(back::rollback, "A second rollback cannot re-apply the rejected version without trial");

        // Achado 3: fechar o app antes do ACK não descarta uma release boa.
        Path closed = directory("closed-before-ready");
        seed(closed, state(builtin(), null, next, false), next);
        check(manager(closed).beginBoot(false).endsWith(HASH), "Prepared release applied in trial");
        OtaManager reopened = manager(closed);
        check(reopened.beginBoot(false).endsWith(HASH), "One close before ready keeps the new version in trial");
        check(reopened.status().getBoolean("trial") && !reopened.status().getBoolean("rolledBack"), "Second trial boot is not a rollback");
        reopened.ready("0.3.1");
        check(!disk(closed).getBoolean("trial"), "Second trial boot can still acknowledge the release");
        Path never = directory("never-ready");
        seed(never, state(builtin(), null, next, false), next);
        for (int boot = 0; boot < UpdatePolicy.MAX_TRIAL_BOOTS; boot++)
            check(manager(never).beginBoot(false).endsWith(HASH), "Release keeps every allowed trial boot");
        OtaManager exhausted = manager(never);
        check(exhausted.beginBoot(false) == null, "Release that never acknowledges rolls back after the allowed trial boots");
        check(exhausted.status().getBoolean("rolledBack"), "Exhausted trial rollback is visible");

        // Achado 5: aviso de recuperação e erro valem para a abertura em que ocorreram.
        Path sticky = directory("sticky-notice");
        seed(sticky, state(next, builtin(), null, false).put("rolledBack", true).put("lastError", "falha antiga"), next);
        OtaManager later = manager(sticky);
        later.beginBoot(false);
        check(!later.status().getBoolean("rolledBack"), "Recovery notice does not survive into a later boot");
        check(later.status().getString("lastError").isEmpty(), "Old error does not survive into a later boot");
    }

    private static void policyTests() throws Exception {
        UpdatePolicy.validateRelease(1, "com.casadababa.piloto", "piloto", 1, "0.3.1", 3, 3, 2, URL, 123, HASH);
        for (String url : new String[] { "http://easystok.online/downloads/casa-da-baba/ota/a.zip", "https://evil.test/downloads/casa-da-baba/ota/a.zip",
            URL + "?x=1", URL + "#x", "https://user@easystok.online/downloads/casa-da-baba/ota/a.zip",
            "https://easystok.online/downloads/casa-da-baba/ota/../a.zip", "https://easystok.online/downloads/casa-da-baba/ota/%2e%2e/a.zip",
            "https://easystok.online:443/downloads/casa-da-baba/ota/a.zip" }) rejects(() -> UpdatePolicy.validateUrl(url), "Unsafe URL accepted");
        rejects(() -> UpdatePolicy.validateRelease(1, "other", "piloto", 1, "0.3.1", 3, 3, 2, URL, 1, HASH), "Other package accepted");
        rejects(() -> UpdatePolicy.validateRelease(1, "com.casadababa.piloto", "piloto", 0, "0.3.1", 3, 3, 2, URL, 1, HASH), "Invalid sequence accepted");
        rejects(() -> UpdatePolicy.validateRelease(1, "com.casadababa.piloto", "piloto", 1, "0.3.1", 4, 3, 2, URL, 1, HASH), "Other APK accepted");
        rejects(() -> UpdatePolicy.validateRelease(1, "com.casadababa.piloto", "piloto", 1, "0.3.1", 3, 3, 1, URL, 1, HASH), "Other data schema accepted");
        rejects(() -> UpdatePolicy.validateRelease(1, "com.casadababa.piloto", "piloto", 1, "x".repeat(65), 3, 3, 2, URL, 1, HASH), "Long version accepted");
        rejects(() -> UpdatePolicy.validateRelease(1, "com.casadababa.piloto", "piloto", 1, "0.3.1", 3, 3, 2, URL, UpdatePolicy.MAX_ZIP + 1, HASH), "Large package accepted");
        KeyPairGenerator generator = KeyPairGenerator.getInstance("RSA"); generator.initialize(2048);
        KeyPair pair = generator.generateKeyPair();
        byte[] payload = "signed exact bytes".getBytes(StandardCharsets.UTF_8);
        Signature signer = Signature.getInstance("SHA256withRSA"); signer.initSign(pair.getPrivate()); signer.update(payload);
        byte[] signature = signer.sign();
        UpdatePolicy.verifySignature(payload, signature, pair.getPublic().getEncoded()); assertions++;
        byte[] changed = payload.clone(); changed[0] ^= 1;
        rejects(() -> UpdatePolicy.verifySignature(changed, signature, pair.getPublic().getEncoded()), "Tampered payload accepted");
        rejects(() -> UpdatePolicy.verifySignature(payload, signature, generator.generateKeyPair().getPublic().getEncoded()), "Wrong key accepted");
        check(UpdatePolicy.isUpdateAnnouncement("/topics/casa-da-baba-piloto-updates", "ota-update", "10"), "Valid topic announcement accepted");
        check(!UpdatePolicy.isUpdateAnnouncement("/topics/other", "ota-update", "10"), "Other topic rejected");
        check(!UpdatePolicy.isUpdateAnnouncement("/topics/casa-da-baba-piloto-updates", "execute", "10"), "Other message kind rejected");
        for (String sequence : new String[] { "0", "-1", "1.5", "1e6", "9999999999999999999", " 1", "01" })
            check(!UpdatePolicy.isUpdateAnnouncement("/topics/casa-da-baba-piloto-updates", "ota-update", sequence), "Malformed push sequence rejected");
    }

    private static JSONObject checkUpdate(OtaManager app) throws Exception {
        java.util.concurrent.CompletableFuture<JSONObject> result = new java.util.concurrent.CompletableFuture<>();
        app.check(true, result::complete);
        return result.get(15, java.util.concurrent.TimeUnit.SECONDS);
    }

    private static void signedManifest(JSONObject release, KeyPair key) throws Exception {
        byte[] payload = release.toString().getBytes(StandardCharsets.UTF_8);
        Signature signer = Signature.getInstance("SHA256withRSA"); signer.initSign(key.getPrivate()); signer.update(payload);
        JSONObject envelope = new JSONObject().put("payloadBase64", Base64.getEncoder().encodeToString(payload))
            .put("signatureBase64", Base64.getEncoder().encodeToString(signer.sign()));
        NetworkFixture.responses.put(UpdatePolicy.MANIFEST_URL, envelope.toString().getBytes(StandardCharsets.UTF_8));
    }

    private static OtaManager networkManager(Path files, KeyPair key) throws Exception {
        Files.createDirectories(files.resolve("assets"));
        Files.write(files.resolve("assets/ota-public-key.der"), key.getPublic().getEncoded());
        OtaManager app = manager(files);
        app.beginBoot(false);
        return app;
    }

    private static void networkTests() throws Exception {
        NetworkFixture.install();
        KeyPairGenerator generator = KeyPairGenerator.getInstance("RSA"); generator.initialize(2048);
        KeyPair key = generator.generateKeyPair();
        JSONObject next = release("0.3.1", HASH, 10);
        ByteArrayOutputStream data = new ByteArrayOutputStream();
        try (ZipOutputStream zip = new ZipOutputStream(data)) {
            zip.putNextEntry(new ZipEntry("index.html")); zip.write("<html>test</html>".getBytes(StandardCharsets.UTF_8)); zip.closeEntry();
            zip.putNextEntry(new ZipEntry("bundle.json")); zip.write(next.toString().getBytes(StandardCharsets.UTF_8)); zip.closeEntry();
        }
        File archive = directory("signed-download").resolve("bundle.zip").toFile();
        Files.write(archive.toPath(), data.toByteArray());
        next.put("sizeBytes", archive.length()).put("sha256", UpdatePolicy.digest(archive));
        NetworkFixture.responses.put(URL, data.toByteArray());
        signedManifest(next, key);
        Path files = directory("download-flow");
        OtaManager app = networkManager(files, key);
        JSONObject downloaded = checkUpdate(app);
        check(downloaded.getBoolean("checkSucceeded"), "Signed manifest and ZIP accepted");
        check(downloaded.getString("activeVersion").equals("0.3.0"), "Download never changes running code");
        check(downloaded.getString("pendingVersion").equals("0.3.1"), "Verified bundle prepared");
        check(disk(files).getLong("maxSequence") == 10, "Accepted sequence persisted atomically with pending");
        int requests = NetworkFixture.packageRequests;
        checkUpdate(app);
        check(NetworkFixture.packageRequests == requests, "Same sequence never downloads again");
        NetworkFixture.status = 500;
        check(!checkUpdate(app).getString("lastError").isEmpty(), "Offline check reports its error");
        NetworkFixture.status = 200;
        JSONObject recovered = checkUpdate(app);
        check(recovered.getBoolean("checkSucceeded") && recovered.getString("lastError").isEmpty(), "Successful check without news clears the old error");
        JSONObject older = new JSONObject(next.toString()).put("sequence", 9);
        signedManifest(older, key); checkUpdate(app);
        check(NetworkFixture.packageRequests == requests, "Older signed manifest rejected by anti-replay");
        signedManifest(next, key);
        app.beginBoot(false);
        for (int boot = 1; boot < UpdatePolicy.MAX_TRIAL_BOOTS; boot++) manager(files).beginBoot(false);
        OtaManager failedTrial = manager(files);
        check(failedTrial.beginBoot(false) == null, "Unacknowledged downloaded bundle rolls back");
        checkUpdate(failedTrial);
        check(failedTrial.status().getString("pendingVersion").isEmpty(), "Failed sequence is not automatically reinstalled");
        check(NetworkFixture.packageRequests == requests, "Failed sequence remains blocked at download gate");

        Path saveFailure = directory("download-save-failure");
        OtaManager saveFail = networkManager(saveFailure, key);
        AtomicFile.failNextWrite = true;
        JSONObject failure = checkUpdate(saveFail);
        check(!failure.getBoolean("checkSucceeded"), "Persistence failure rejects preparation");
        check(failure.getString("pendingVersion").isEmpty(), "Failed pending save restores in-memory pending");
        check(disk(saveFailure).getLong("maxSequence") == 0, "Failed save does not consume sequence on disk");

        Path tampered = directory("tampered-download");
        OtaManager tamperApp = networkManager(tampered, key);
        byte[] corrupt = data.toByteArray(); corrupt[corrupt.length - 1] ^= 1;
        NetworkFixture.responses.put(URL, corrupt);
        check(!checkUpdate(tamperApp).getBoolean("checkSucceeded"), "Hash-corrupted ZIP rejected");
        check(tamperApp.status().getString("pendingVersion").isEmpty(), "Corrupt ZIP cannot become pending");
        check(disk(tampered).getLong("maxSequence") == 0, "Corrupt ZIP does not consume sequence");
        NetworkFixture.responses.put(URL, data.toByteArray());
        NetworkFixture.status = 302;
        check(!checkUpdate(tamperApp).getBoolean("checkSucceeded"), "HTTP redirects refused");
        NetworkFixture.status = 200;
        signedManifest(next, generator.generateKeyPair());
        check(!checkUpdate(tamperApp).getBoolean("checkSucceeded"), "Manifest signed by unknown key rejected");
        signedManifest(new JSONObject(next.toString()).put("nativeVersionCode", 4), key);
        check(!checkUpdate(tamperApp).getBoolean("checkSucceeded"), "Different native APK manifest rejected");
        signedManifest(new JSONObject(next.toString()).put("bundleVersion", "0.3.2"), key);
        check(!checkUpdate(tamperApp).getBoolean("checkSucceeded"), "Internal bundle version mismatch rejected");
    }

    private static File zip(String name, String[] entries, int largeBytes) throws Exception {
        File archive = directory(name).resolve("bundle.zip").toFile();
        try (ZipOutputStream stream = new ZipOutputStream(new FileOutputStream(archive))) {
            for (String entry : entries) {
                stream.putNextEntry(new ZipEntry(entry));
                if (largeBytes > 0) {
                    byte[] buffer = new byte[8192];
                    for (int bytes = 0; bytes < largeBytes; bytes += buffer.length) stream.write(buffer, 0, Math.min(buffer.length, largeBytes - bytes));
                } else stream.write("ok".getBytes(StandardCharsets.UTF_8));
                stream.closeEntry();
            }
        }
        return archive;
    }

    private static void zipTests() throws Exception {
        File valid = zip("valid", new String[] { "index.html", "bundle.json", "assets/app.js" }, 0);
        Path target = directory("valid-output");
        UpdatePolicy.extract(valid, target.toFile());
        check(Files.readString(target.resolve("assets/app.js")).equals("ok"), "Valid archive extracted");
        for (String entry : new String[] { "../escape", "/absolute", "a/../../escape", "a\\escape", "C:/escape", "./escape" }) {
            File unsafe = zip("unsafe", new String[] { entry }, 0);
            rejects(() -> UpdatePolicy.extract(unsafe, directory("unsafe-output").toFile()), "Unsafe ZIP path accepted");
        }
        File duplicate = zip("duplicate", new String[] { "a/b", "a//b" }, 0);
        rejects(() -> UpdatePolicy.extract(duplicate, directory("duplicate-output").toFile()), "Canonical duplicate accepted");
        File missing = zip("missing-meta", new String[] { "index.html" }, 0);
        rejects(() -> UpdatePolicy.extract(missing, directory("missing-output").toFile()), "Missing metadata accepted");
        File large = zip("large", new String[] { "huge.bin" }, (int) UpdatePolicy.MAX_FILE + 1);
        rejects(() -> UpdatePolicy.extract(large, directory("large-output").toFile()), "Expanded file limit ignored");
        String[] many = new String[UpdatePolicy.MAX_ENTRIES + 1];
        for (int index = 0; index < many.length; index++) many[index] = "file-" + index;
        File excessive = zip("many", many, 0);
        rejects(() -> UpdatePolicy.extract(excessive, directory("many-output").toFile()), "Entry count limit ignored");
        check(UpdatePolicy.digest(valid).matches("[a-f0-9]{64}"), "SHA256 format");
    }

    public static void main(String[] args) throws Exception {
        temporary = Files.createTempDirectory("cdb-updater-test-");
        try { policyTests(); zipTests(); stateTests(); reviewTests(); networkTests(); System.out.println("PASS: " + assertions + " native updater assertions (host fixtures; Android device validation pending)"); }
        finally {
            java.lang.reflect.Field worker = OtaManager.class.getDeclaredField("WORKER"); worker.setAccessible(true);
            ((java.util.concurrent.ExecutorService) worker.get(null)).shutdownNow();
            try (java.util.stream.Stream<Path> files = Files.walk(temporary)) {
                for (Path file : files.sorted(Comparator.reverseOrder()).toList()) Files.delete(file);
            }
        }
    }
}
