package com.casadababa.app.backup;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.ContentUris;
import android.content.ContentValues;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.provider.MediaStore;
import android.provider.OpenableColumns;

import androidx.activity.result.ActivityResult;

import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.ActivityCallback;
import com.getcapacitor.annotation.CapacitorPlugin;

import java.io.BufferedReader;
import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;

/**
 * Plugin de backup público — sobrevive uninstall do app.
 *
 * Usa MediaStore (Android Q+) pra escrever em /storage/emulated/0/Documents/
 * casa-da-baba/ e /storage/emulated/0/Download/, locais públicos que NÃO são
 * apagados quando o app é desinstalado. Em Android < Q, cai pro path direto
 * via Environment.getExternalStoragePublicDirectory.
 *
 * Limitação conhecida: após reinstall, o app perde "ownership" sobre os
 * arquivos que criou. Pra LER de volta, precisa do SAF picker (método
 * pickBackupFile) onde o usuário toca no arquivo manualmente uma vez.
 *
 * Métodos:
 *  - writePublicBackup({ filename, json }) → salva em Documents + Download
 *  - readPublicBackup({ filename })        → tenta ler via path direto / cursor
 *  - pickBackupFile()                      → abre SAF picker, retorna conteúdo
 */
@CapacitorPlugin(name = "PublicBackup")
public class PublicBackupPlugin extends Plugin {

    private static final String SUBDIR = "casa-da-baba";

    @PluginMethod
    public void writePublicBackup(PluginCall call) {
        final String filename = call.getString("filename", "backup-latest.json");
        final String json = call.getString("json");
        if (json == null) { call.reject("json obrigatorio"); return; }

        // #3425: cada pasta no seu try; uma falha em Documents não pula a cópia em Download.
        Uri docsUri = null;
        Uri dlUri = null;
        String docsErro = null;
        String dlErro = null;
        try {
            docsUri = writeToCollection(filename, json,
                Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q
                    ? MediaStore.Files.getContentUri("external")
                    : null,
                Environment.DIRECTORY_DOCUMENTS + "/" + SUBDIR);
        } catch (Exception e) {
            docsErro = mensagem(e);
        }

        // Cópia adicional em /Download/ — fácil pro usuário achar pelo
        // gerenciador de arquivos, e MUITO menos agressivo de scoped storage.
        try {
            dlUri = writeToCollection(filename, json,
                Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q
                    ? MediaStore.Downloads.EXTERNAL_CONTENT_URI
                    : null,
                Environment.DIRECTORY_DOWNLOADS + "/" + SUBDIR);
        } catch (Exception e) {
            dlErro = mensagem(e);
        }

        if (docsUri == null && dlUri == null) {
            call.reject("Falha ao salvar backup publico: " + docsErro + "; downloads: " + dlErro);
            return;
        }
        JSObject ret = new JSObject();
        ret.put("documentsUri", docsUri != null ? docsUri.toString() : null);
        ret.put("downloadsUri", dlUri != null ? dlUri.toString() : null);
        ret.put("documentsError", docsErro);
        ret.put("downloadsError", dlErro);
        ret.put("filename", filename);
        ret.put("survivesUninstall", true);
        call.resolve(ret);
    }

    private static String mensagem(Exception e) {
        return e.getMessage() != null ? e.getMessage() : e.getClass().getSimpleName();
    }

    /**
     * Escreve em uma coleção MediaStore (Q+) ou direto no path público (P-).
     * No Q+ o PublicBackupWriter sobrescreve a cópia do app em vez de criar outra a cada envio.
     */
    private Uri writeToCollection(String filename, String json, Uri collection, String relativePath) throws IOException {
        ContentResolver resolver = getContext().getContentResolver();

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q && collection != null) {
            long id = PublicBackupWriter.write(new MediaStoreFolder(resolver, collection, relativePath), filename,
                json.getBytes(StandardCharsets.UTF_8), new SimpleDateFormat("yyyyMMddHHmmss", Locale.ROOT).format(new Date()));
            return ContentUris.withAppendedId(collection, id);
        }

        // Android P (API 28) e abaixo: usa filesystem direto.
        File dir = new File(Environment.getExternalStorageDirectory(), relativePath);
        if (!dir.exists()) dir.mkdirs();
        File file = new File(dir, filename);
        try (java.io.FileOutputStream fos = new java.io.FileOutputStream(file, false)) {
            fos.write(json.getBytes(StandardCharsets.UTF_8));
            fos.flush();
        }
        return Uri.fromFile(file);
    }

    /** #3425: a cópia que o writePublicBackup sobrescreve, inclusive "backup-latest (n).json". */
    private Uri findBackup(ContentResolver resolver, Uri collection, String filename, String relativePath) {
        try {
            PublicBackupWriter.Entry e = PublicBackupWriter.find(new MediaStoreFolder(resolver, collection, relativePath), filename);
            return e != null ? ContentUris.withAppendedId(collection, e.id) : null;
        } catch (Exception ignore) {
            return null;
        }
    }

    /** #3425: uma pasta do MediaStore (Android Q+) entregue ao PublicBackupWriter. */
    private static final class MediaStoreFolder implements PublicBackupWriter.Store {
        private final ContentResolver resolver;
        private final Uri collection;
        private final String relativePath;

        MediaStoreFolder(ContentResolver resolver, Uri collection, String relativePath) {
            this.resolver = resolver;
            this.collection = collection;
            // RELATIVE_PATH no MediaStore sempre termina com '/'.
            this.relativePath = relativePath.endsWith("/") ? relativePath : relativePath + "/";
        }

        /** Sem engolir erro: consulta que falha não pode virar "sem cópia" e criar outra a cada envio. */
        @Override
        public List<PublicBackupWriter.Entry> listOwned(String prefix) throws IOException {
            String[] proj = { MediaStore.MediaColumns._ID, MediaStore.MediaColumns.DISPLAY_NAME };
            String sel = MediaStore.MediaColumns.RELATIVE_PATH + "=? AND "
                       + MediaStore.MediaColumns.DISPLAY_NAME + " LIKE ?";
            String[] args = { relativePath, prefix + "%" };
            List<PublicBackupWriter.Entry> r = new ArrayList<>();
            try (Cursor c = resolver.query(collection, proj, sel, args, null)) {
                if (c == null) throw new IOException("MediaStore query retornou null");
                while (c.moveToNext()) r.add(new PublicBackupWriter.Entry(c.getLong(0), c.getString(1)));
            }
            return r;
        }

        @Override
        public void overwrite(long id, byte[] data) throws IOException {
            try (OutputStream out = resolver.openOutputStream(uri(id), "wt")) {
                if (out == null) throw new IOException("openOutputStream retornou null");
                out.write(data);
                out.flush();
            }
        }

        @Override
        public long insertPending(String name) throws IOException {
            ContentValues values = new ContentValues();
            values.put(MediaStore.MediaColumns.DISPLAY_NAME, name);
            values.put(MediaStore.MediaColumns.MIME_TYPE, "application/json");
            values.put(MediaStore.MediaColumns.RELATIVE_PATH, relativePath);
            values.put(MediaStore.MediaColumns.IS_PENDING, 1);
            Uri uri = resolver.insert(collection, values);
            if (uri == null) throw new IOException("MediaStore insert retornou null");
            return ContentUris.parseId(uri);
        }

        @Override
        public void publish(long id, String rename) {
            ContentValues values = new ContentValues();
            if (rename != null) values.put(MediaStore.MediaColumns.DISPLAY_NAME, rename);
            values.put(MediaStore.MediaColumns.IS_PENDING, 0);
            resolver.update(uri(id), values, null, null);
        }

        @Override
        public void delete(long id) {
            try {
                resolver.delete(uri(id), null, null);
            } catch (Exception ignore) {
                // A linha pendente expira sozinha em 7 dias.
            }
        }

        private Uri uri(long id) {
            return ContentUris.withAppendedId(collection, id);
        }
    }

    /**
     * Tenta ler o backup mais recente. Em Android Q+ procura via MediaStore.
     * Em P- lê direto do path. Falha silenciosa retornando null se não achar.
     */
    @PluginMethod
    public void readPublicBackup(PluginCall call) {
        final String filename = call.getString("filename", "backup-latest.json");
        try {
            String content = readFromAnyLocation(filename);
            JSObject ret = new JSObject();
            ret.put("found", content != null);
            ret.put("content", content);
            call.resolve(ret);
        } catch (Exception e) {
            JSObject ret = new JSObject();
            ret.put("found", false);
            ret.put("error", e.getMessage());
            call.resolve(ret);
        }
    }

    private String readFromAnyLocation(String filename) {
        ContentResolver resolver = getContext().getContentResolver();
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            // Tenta Documents primeiro, depois Downloads.
            Uri docsUri = findBackup(resolver,
                MediaStore.Files.getContentUri("external"),
                filename, Environment.DIRECTORY_DOCUMENTS + "/" + SUBDIR);
            if (docsUri != null) {
                String c = readUri(docsUri);
                if (c != null) return c;
            }
            Uri dlUri = findBackup(resolver,
                MediaStore.Downloads.EXTERNAL_CONTENT_URI,
                filename, Environment.DIRECTORY_DOWNLOADS + "/" + SUBDIR);
            if (dlUri != null) {
                String c = readUri(dlUri);
                if (c != null) return c;
            }
            return null;
        }

        // Android P-: filesystem direto
        for (String dir : new String[]{ Environment.DIRECTORY_DOCUMENTS, Environment.DIRECTORY_DOWNLOADS }) {
            File f = new File(new File(Environment.getExternalStorageDirectory(), dir + "/" + SUBDIR), filename);
            if (f.exists() && f.canRead()) {
                try (FileInputStream fis = new FileInputStream(f)) {
                    return readStream(fis);
                } catch (IOException ignore) {}
            }
        }
        return null;
    }

    private String readUri(Uri uri) {
        try (InputStream in = getContext().getContentResolver().openInputStream(uri)) {
            return in != null ? readStream(in) : null;
        } catch (Exception e) { return null; }
    }

    private String readStream(InputStream in) throws IOException {
        StringBuilder sb = new StringBuilder();
        try (BufferedReader br = new BufferedReader(new InputStreamReader(in, StandardCharsets.UTF_8))) {
            char[] buf = new char[8192];
            int n;
            while ((n = br.read(buf)) > 0) sb.append(buf, 0, n);
        }
        return sb.toString();
    }

    /**
     * Abre SAF picker (ACTION_OPEN_DOCUMENT). Usuário seleciona arquivo .json.
     * Necessário em Android 11+ pós-reinstall, quando o app perdeu ownership
     * dos arquivos que ele mesmo criou via MediaStore.
     */
    @PluginMethod
    public void pickBackupFile(PluginCall call) {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("*/*");
        intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[]{ "application/json", "text/plain", "*/*" });
        startActivityForResult(call, intent, "onPickResult");
    }

    @ActivityCallback
    private void onPickResult(PluginCall call, ActivityResult result) {
        if (call == null) return;
        if (result.getResultCode() != Activity.RESULT_OK || result.getData() == null) {
            JSObject ret = new JSObject();
            ret.put("cancelled", true);
            call.resolve(ret);
            return;
        }
        Uri uri = result.getData().getData();
        if (uri == null) {
            JSObject ret = new JSObject();
            ret.put("cancelled", true);
            call.resolve(ret);
            return;
        }
        try {
            String name = queryDisplayName(uri);
            String content = readUri(uri);
            JSObject ret = new JSObject();
            ret.put("cancelled", false);
            ret.put("filename", name);
            ret.put("content", content);
            call.resolve(ret);
        } catch (Exception e) {
            call.reject("Falha ao ler arquivo selecionado: " + e.getMessage(), e);
        }
    }

    private String queryDisplayName(Uri uri) {
        try (Cursor c = getContext().getContentResolver().query(uri, new String[]{ OpenableColumns.DISPLAY_NAME }, null, null, null)) {
            if (c != null && c.moveToFirst()) return c.getString(0);
        } catch (Exception ignore) {}
        return "backup.json";
    }
}
