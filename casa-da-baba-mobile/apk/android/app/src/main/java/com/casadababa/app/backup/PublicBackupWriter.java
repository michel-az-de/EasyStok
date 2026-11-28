package com.casadababa.app.backup;

import java.io.FileNotFoundException;
import java.io.IOException;
import java.util.List;
import java.util.regex.Pattern;

/**
 * #3425: decide em qual arquivo de uma pasta pública o backup é gravado. Não usa Android: o plugin
 * entrega o MediaStore pela interface {@link Store} e o teste host usa um MediaStore falso.
 *
 * O app só enxerga no MediaStore os arquivos que ele mesmo criou. Com backup-latest.json de outra
 * instalação, o Android publica a cópia nova como "backup-latest (1).json". Buscar só o nome exato
 * fazia cada envio criar mais uma, até as 32 tentativas de nome acabarem e o backup público parar.
 */
public final class PublicBackupWriter {
    private PublicBackupWriter() {}

    /** Arquivo publicado da pasta que pertence ao app. */
    public static final class Entry {
        final long id;
        final String name;

        public Entry(long id, String name) {
            this.id = id;
            this.name = name;
        }
    }

    /** Uma pasta do MediaStore vista pelo app: só enxerga o que ele mesmo criou. */
    public interface Store {
        /** Arquivos publicados do app na pasta cujo nome começa por {@code prefix}. */
        List<Entry> listOwned(String prefix) throws IOException;

        void overwrite(long id, byte[] data) throws IOException;

        /** Cria a linha pendente (oculta) com o nome pedido. */
        long insertPending(String name) throws IOException;

        /**
         * Tira a linha do pendente; {@code rename} nulo mantém o nome. O Android troca o nome por
         * "nome (n)" se ele já existir e lança IllegalStateException quando as 32 tentativas acabam.
         */
        void publish(long id, String rename) throws IOException;

        void delete(long id);
    }

    /**
     * Sobrescreve a cópia do app; sem cópia, cria uma. Nomes esgotados viram "base-{stamp}.ext", que
     * os próximos envios acham e sobrescrevem. A linha pendente que não publicou é apagada.
     */
    public static long write(Store store, String filename, byte[] data, String stamp) throws IOException {
        Entry atual = find(store, filename);
        if (atual != null) {
            try {
                store.overwrite(atual.id, data);
                return atual.id;
            } catch (FileNotFoundException semArquivo) {
                // Linha sem arquivo: cria outra, que vira a mais recente e é a do próximo envio. Outra falha
                // sobe, para não criar uma cópia por envio quando a sobrescrita falha sempre.
            }
        }
        String alternativo = base(filename) + "-" + stamp + ext(filename);
        String nome = filename;
        long id;
        try {
            id = store.insertPending(filename);
        } catch (IOException | RuntimeException esgotado) {
            // Android 10 escolhe o nome final já no insert.
            nome = alternativo;
            id = store.insertPending(alternativo);
        }
        boolean publicado = false;
        try {
            store.overwrite(id, data);
            try {
                store.publish(id, null);
            } catch (IOException | RuntimeException esgotado) {
                // Android 11+ escolhe o nome final ao publicar.
                if (nome.equals(alternativo)) throw esgotado;
                store.publish(id, alternativo);
            }
            publicado = true;
            return id;
        } finally {
            if (!publicado) store.delete(id);
        }
    }

    /** A cópia mais recente do app: "base.ext", "base (n).ext" ou "base-{dígitos}.ext". */
    public static Entry find(Store store, String filename) throws IOException {
        Pattern copia = Pattern.compile(Pattern.quote(base(filename)) + "( \\(\\d+\\)|-\\d+)?" + Pattern.quote(ext(filename)));
        Entry escolhida = null;
        for (Entry e : store.listOwned(base(filename))) {
            if (e.name != null && copia.matcher(e.name).matches() && (escolhida == null || e.id > escolhida.id)) escolhida = e;
        }
        return escolhida;
    }

    private static String base(String filename) {
        int ponto = filename.lastIndexOf('.');
        return ponto > 0 ? filename.substring(0, ponto) : filename;
    }

    private static String ext(String filename) {
        int ponto = filename.lastIndexOf('.');
        return ponto > 0 ? filename.substring(ponto) : "";
    }
}
