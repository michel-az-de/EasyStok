package com.casadababa.app.updater;

import java.io.*;
import java.net.URI;
import java.security.*;
import java.security.spec.X509EncodedKeySpec;
import java.util.HashSet;
import java.util.Set;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;

/** Validation shared by the Android updater and its dependency-free host tests. */
public final class UpdatePolicy {
    public static final long MAX_ZIP = 64L * 1024 * 1024;
    public static final long MAX_EXPANDED = 128L * 1024 * 1024;
    public static final long MAX_FILE = 32L * 1024 * 1024;
    public static final int MAX_ENTRIES = 4096;
    public static final int DATA_SCHEMA = 2;
    /** Aberturas em teste sem ACK antes de voltar. Uma só confunde fechar o app com versão quebrada. */
    public static final int MAX_TRIAL_BOOTS = 2;
    public static final String APP_ID = "com.casadababa.piloto";
    public static final String CHANNEL = "piloto";
    public static final String MANIFEST_URL = "https://easystok.online/downloads/casa-da-baba/ota/latest.json";
    private UpdatePolicy() {}

    public static void require(boolean condition, String message) throws IOException {
        if (!condition) throw new IOException(message);
    }

    public static void validateUrl(String value) throws IOException {
        try {
            URI uri = new URI(value);
            String path = uri.getRawPath();
            require("https".equals(uri.getScheme()) && "easystok.online".equals(uri.getHost())
                && uri.getPort() == -1 && uri.getRawUserInfo() == null && uri.getRawQuery() == null
                && uri.getRawFragment() == null && path != null
                && path.startsWith("/downloads/casa-da-baba/ota/")
                && path.matches("/[A-Za-z0-9/._-]+") && !path.contains("..") && !path.contains("//"),
                "Endereço de atualização não autorizado.");
        } catch (java.net.URISyntaxException error) {
            throw new IOException("Endereço de atualização inválido.", error);
        }
    }

    public static void validateRelease(int schema, String appId, String channel, long sequence,
            String version, int nativeVersion, int expectedNative, int dataSchema,
            String url, long size, String hash) throws IOException {
        require(schema == 1 && APP_ID.equals(appId) && CHANNEL.equals(channel), "Manifesto incompatível com este aplicativo.");
        require(sequence > 0 && version != null && version.matches("[A-Za-z0-9][A-Za-z0-9._-]{0,63}"), "Versão de atualização inválida.");
        require(nativeVersion == expectedNative && dataSchema == DATA_SCHEMA, "Atualização exige outro APK ou formato de dados.");
        require(size > 0 && size <= MAX_ZIP && hash != null && hash.matches("[a-f0-9]{64}"), "Tamanho ou hash de atualização inválido.");
        validateUrl(url);
    }

    public static void verifySignature(byte[] payload, byte[] signature, byte[] publicKey) throws IOException {
        try {
            Signature verifier = Signature.getInstance("SHA256withRSA");
            verifier.initVerify(KeyFactory.getInstance("RSA").generatePublic(new X509EncodedKeySpec(publicKey)));
            verifier.update(payload);
            require(verifier.verify(signature), "Assinatura da atualização inválida.");
        } catch (GeneralSecurityException error) {
            throw new IOException("Não foi possível validar a assinatura da atualização.", error);
        }
    }

    public static String digest(File file) throws IOException {
        try (InputStream input = new FileInputStream(file)) {
            MessageDigest hash = MessageDigest.getInstance("SHA-256");
            byte[] buffer = new byte[16384];
            int read;
            while ((read = input.read(buffer)) != -1) hash.update(buffer, 0, read);
            StringBuilder result = new StringBuilder();
            for (byte value : hash.digest()) result.append(String.format(java.util.Locale.ROOT, "%02x", value & 255));
            return result.toString();
        } catch (NoSuchAlgorithmException error) {
            throw new IOException(error);
        }
    }

    public static void extract(File archive, File destination) throws IOException {
        require(destination.isDirectory(), "Diretório temporário indisponível.");
        String prefix = destination.getCanonicalPath() + File.separator;
        Set<String> paths = new HashSet<>();
        long total = 0;
        int count = 0;
        try (ZipInputStream zip = new ZipInputStream(new FileInputStream(archive))) {
            ZipEntry entry;
            byte[] buffer = new byte[16384];
            while ((entry = zip.getNextEntry()) != null) {
                require(++count <= MAX_ENTRIES, "Atualização contém arquivos demais.");
                String name = entry.getName();
                require(!name.isEmpty() && !name.startsWith("/") && !name.contains("\\")
                    && !name.contains(":") && name.indexOf(0) == -1, "Caminho inválido no pacote.");
                for (String part : name.split("/", -1))
                    require(!part.equals(".") && !part.equals(".."), "Caminho inválido no pacote.");
                File output = new File(destination, name);
                String canonical = output.getCanonicalPath();
                require(canonical.startsWith(prefix) && paths.add(canonical), "Caminho repetido ou fora do pacote.");
                if (entry.isDirectory()) {
                    require(output.isDirectory() || output.mkdirs(), "Falha ao criar diretório da atualização.");
                } else {
                    File parent = output.getParentFile();
                    require(parent.isDirectory() || parent.mkdirs(), "Falha ao preparar atualização.");
                    long bytes = 0;
                    try (FileOutputStream target = new FileOutputStream(output)) {
                        int read;
                        while ((read = zip.read(buffer)) != -1) {
                            bytes += read;
                            total += read;
                            require(bytes <= MAX_FILE && total <= MAX_EXPANDED, "Atualização excede o limite descompactado.");
                            target.write(buffer, 0, read);
                        }
                        target.getFD().sync();
                    }
                }
                zip.closeEntry();
            }
        }
        require(new File(destination, "index.html").isFile() && new File(destination, "bundle.json").isFile(),
            "Pacote sem tela inicial ou identificação de versão.");
    }

    /** trialBoots conta as aberturas já feitas em teste; volta só quando a nova abertura excederia o limite. */
    public static boolean mustFallback(boolean trial, boolean recreatedInSameProcess, long trialBoots) {
        return trial && !recreatedInSameProcess && trialBoots >= MAX_TRIAL_BOOTS;
    }

    public static boolean isUpdateAnnouncement(String sender, String kind, String sequence) {
        if (!"/topics/casa-da-baba-piloto-updates".equals(sender) || !"ota-update".equals(kind)
                || sequence == null || !sequence.matches("[1-9][0-9]{0,18}")) return false;
        try { return Long.parseLong(sequence) > 0; }
        catch (NumberFormatException invalid) { return false; }
    }
}
