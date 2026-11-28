package com.casadababa.app.backup;

import java.io.FileNotFoundException;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * #3425: o backup público sobrescreve a própria cópia, mesmo com backup-latest.json de outra instalação.
 * O MediaStore falso segue o MediaProvider: o app só lista o que criou, a linha nasce pendente e oculta,
 * e o nome final sai de 32 tentativas ("nome", "nome (1)" ... "nome (31)"), no publicar (Android 11+)
 * ou no insert (Android 10). Esgotadas, lança "Failed to build unique file", como no log de 28/11/2026.
 */
public final class PublicBackupWriterTest {
    private static final String NOME = "backup-latest.json";
    private static int checks;

    public static void main(String[] args) throws Exception {
        pastaLimpaSobrescreveONomeExato();
        arquivoDeOutraInstalacaoNaoAcumulaCopias(false);
        arquivoDeOutraInstalacaoNaoAcumulaCopias(true);
        nomesEsgotadosGravamNoNomeAlternativo(false);
        nomesEsgotadosGravamNoNomeAlternativo(true);
        copiasDaFalhaAntigaSaoReaproveitadas();
        copiaMaisRecenteDoAppVence();
        nomesParecidosNaoSaoCopia();
        linhaQuebradaGanhaUmaCopiaNova();
        falhaNaLinhaNovaNaoDeixaPendente();
        System.out.println("PASS: " + checks + " public backup assertions; MediaStore real requires device validation");
    }

    private static void pastaLimpaSobrescreveONomeExato() throws IOException {
        Pasta p = new Pasta(false);
        for (int i = 1; i <= 3; i++) PublicBackupWriter.write(p, NOME, json(i), "20261128130000");
        check(p.publicados.size() == 1 && p.publicados.containsValue(NOME), "pasta limpa: uma cópia, no nome exato " + p.publicados);
        check(p.conteudoUnico().equals("envio-3"), "pasta limpa: a cópia tem o último envio");
        check(p.pendentes.isEmpty(), "pasta limpa: nenhuma linha pendente");
    }

    private static void arquivoDeOutraInstalacaoNaoAcumulaCopias(boolean android10) throws IOException {
        Pasta p = new Pasta(android10, NOME);
        for (int i = 1; i <= 40; i++) PublicBackupWriter.write(p, NOME, json(i), "20261128130000");
        String caso = (android10 ? "Android 10" : "Android 11+") + ", backup-latest.json de outra instalação: ";
        check(p.publicados.size() == 1, caso + "40 envios deixam uma cópia do app, não " + p.publicados.size());
        check(p.conteudoUnico().equals("envio-40"), caso + "a cópia tem o último envio");
        check(p.pendentes.isEmpty(), caso + "nenhuma linha pendente");
    }

    private static void nomesEsgotadosGravamNoNomeAlternativo(boolean android10) throws IOException {
        Pasta p = new Pasta(android10, nomesOcupados());
        PublicBackupWriter.write(p, NOME, json(1), "20261128132700");
        String caso = (android10 ? "Android 10" : "Android 11+") + ", 32 nomes de outra instalação: ";
        check(p.publicados.size() == 1 && p.publicados.containsValue("backup-latest-20261128132700.json"),
            caso + "grava no nome alternativo " + p.publicados);
        for (int i = 2; i <= 6; i++) PublicBackupWriter.write(p, NOME, json(i), "2026112813280" + i);
        check(p.publicados.size() == 1 && p.publicados.containsValue("backup-latest-20261128132700.json"),
            caso + "os envios seguintes sobrescrevem o alternativo " + p.publicados);
        check(p.conteudoUnico().equals("envio-6"), caso + "o alternativo tem o último envio");
        check(p.pendentes.isEmpty(), caso + "nenhuma linha pendente");
    }

    /** O aparelho do log: o app já criou "(1)" a "(31)" com o código antigo e o exato é de outra instalação. */
    private static void copiasDaFalhaAntigaSaoReaproveitadas() throws IOException {
        Pasta p = new Pasta(false, NOME);
        for (int i = 1; i <= 31; i++) p.publicar((long) i, "backup-latest (" + i + ").json");
        PublicBackupWriter.write(p, NOME, json(1), "20261128132700");
        check(p.publicados.size() == 31, "cópias antigas: nenhuma cópia nova, eram 31 e ficaram " + p.publicados.size());
        check("envio-1".equals(p.conteudo(31L)), "cópias antigas: sobrescreve a mais recente, a (31)");
        check(p.pendentes.isEmpty(), "cópias antigas: nenhuma linha pendente");
    }

    private static void copiaMaisRecenteDoAppVence() throws IOException {
        Pasta p = new Pasta(false);
        p.publicar(3L, NOME);
        p.publicar(5L, "backup-latest (2).json");
        PublicBackupWriter.write(p, NOME, json(1), "20261128132700");
        check("envio-1".equals(p.conteudo(5L)) && p.conteudo(3L) == null && p.publicados.size() == 2,
            "a cópia mais recente do app é a sobrescrita");
    }

    private static void nomesParecidosNaoSaoCopia() throws IOException {
        Pasta p = new Pasta(false);
        p.publicar(1L, "backup-latest-antigo.json");
        p.publicar(2L, "backup-latest.json.bak");
        p.publicar(3L, "backup-latestX.json");
        PublicBackupWriter.write(p, NOME, json(1), "20261128132700");
        check(p.publicados.size() == 4 && p.conteudo(1L) == null && p.conteudo(2L) == null && p.conteudo(3L) == null,
            "nome parecido não é cópia: cria backup-latest.json " + p.publicados);
    }

    /** Linha do app sem arquivo (apagado pelo gerenciador de arquivos): uma cópia nova, e só uma. */
    private static void linhaQuebradaGanhaUmaCopiaNova() throws IOException {
        Pasta p = new Pasta(false);
        p.publicar(7L, NOME);
        p.quebradas.add(7L);
        for (int i = 1; i <= 5; i++) PublicBackupWriter.write(p, NOME, json(i), "20261128132700");
        check(p.publicados.size() == 2 && "envio-5".equals(p.conteudo(100L)) && p.pendentes.isEmpty(),
            "linha quebrada: 5 envios criam uma única cópia nova, com o último envio " + p.publicados);
    }

    /** Disco cheio na linha nova: a pendente do app é apagada, não fica oculta por 7 dias. */
    private static void falhaNaLinhaNovaNaoDeixaPendente() {
        Pasta p = new Pasta(false);
        p.quebradas.add(p.proximo);
        boolean falhou = false;
        try { PublicBackupWriter.write(p, NOME, json(1), "20261128132700"); } catch (IOException e) { falhou = true; }
        check(falhou && p.publicados.isEmpty() && p.pendentes.isEmpty(), "falha na linha nova: nenhuma linha pendente sobra");
    }

    private static String[] nomesOcupados() {
        List<String> nomes = new ArrayList<>();
        nomes.add(NOME);
        for (int i = 1; i <= 31; i++) nomes.add("backup-latest (" + i + ").json");
        return nomes.toArray(new String[0]);
    }

    private static byte[] json(int envio) { return ("envio-" + envio).getBytes(StandardCharsets.UTF_8); }

    private static void check(boolean ok, String regra) {
        checks++;
        if (!ok) throw new AssertionError(regra);
    }

    /** Uma pasta do MediaStore vista por esta instalação do app. */
    private static final class Pasta implements PublicBackupWriter.Store {
        final boolean nomeNoInsert;
        final Set<String> deOutraInstalacao = new HashSet<>();
        final Map<Long, String> publicados = new LinkedHashMap<>();
        final Map<Long, String> pendentes = new LinkedHashMap<>();
        final Map<Long, byte[]> dados = new HashMap<>();
        final Set<Long> quebradas = new HashSet<>();
        long proximo = 100;

        Pasta(boolean nomeNoInsert, String... deOutraInstalacao) {
            this.nomeNoInsert = nomeNoInsert;
            this.deOutraInstalacao.addAll(Arrays.asList(deOutraInstalacao));
        }

        void publicar(long id, String nome) { publicados.put(id, nome); }

        String conteudo(long id) {
            byte[] d = dados.get(id);
            return d == null ? null : new String(d, StandardCharsets.UTF_8);
        }

        String conteudoUnico() { return conteudo(publicados.keySet().iterator().next()); }

        @Override public List<PublicBackupWriter.Entry> listOwned(String prefix) {
            List<PublicBackupWriter.Entry> r = new ArrayList<>();
            for (Map.Entry<Long, String> e : publicados.entrySet()) {
                if (e.getValue().startsWith(prefix)) r.add(new PublicBackupWriter.Entry(e.getKey(), e.getValue()));
            }
            return r;
        }

        @Override public void overwrite(long id, byte[] data) throws IOException {
            if (quebradas.contains(id) || (!publicados.containsKey(id) && !pendentes.containsKey(id))) {
                throw new FileNotFoundException("sem arquivo para a linha " + id);
            }
            dados.put(id, data);
        }

        /** Android 11+ guarda o nome pedido e grava como ".pending-...", que não ocupa nome; o 10 já escolhe aqui. */
        @Override public long insertPending(String name) {
            String nome = nomeNoInsert ? unico(name) : name;
            long id = proximo++;
            pendentes.put(id, nome);
            return id;
        }

        @Override public void publish(long id, String rename) {
            String atual = pendentes.get(id);
            if (atual == null) throw new IllegalStateException("linha " + id + " não está pendente");
            String nome = rename != null ? unico(rename) : nomeNoInsert ? atual : unico(atual);
            pendentes.remove(id);
            publicados.put(id, nome);
        }

        @Override public void delete(long id) {
            publicados.remove(id);
            pendentes.remove(id);
            dados.remove(id);
        }

        private String unico(String name) {
            Set<String> ocupados = new HashSet<>(deOutraInstalacao);
            ocupados.addAll(publicados.values());
            if (nomeNoInsert) ocupados.addAll(pendentes.values());
            int ponto = name.lastIndexOf('.');
            String base = name.substring(0, ponto), ext = name.substring(ponto);
            for (int i = 0; i < 32; i++) {
                String candidato = i == 0 ? name : base + " (" + i + ")" + ext;
                if (!ocupados.contains(candidato)) return candidato;
            }
            throw new IllegalStateException("Failed to build unique file: /storage/emulated/0/Documents/casa-da-baba/"
                + name + " " + name + " application/json");
        }
    }
}
