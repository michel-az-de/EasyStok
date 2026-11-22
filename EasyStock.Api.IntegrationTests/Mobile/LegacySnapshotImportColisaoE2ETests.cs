using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// #2813: colisao de cadastro entre o backup do PWA e o ERP nao trava a importacao inteira. Cliente de mesmo telefone
/// vincula, produto de mesmo nome sem saldo nem lote vincula, o resto recusa listando TODAS as colisoes de uma vez.
/// Tambem prova que o pedido importado guarda as datas originais de entrega e cancelamento.
/// </summary>
[Collection("MobileE2E")]
public class LegacySnapshotImportColisaoE2ETests(MobileE2EFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string CodigoDeConflito = "LEGACY_RECONCILIATION_REQUIRED";
    private sealed record Ctx(MobileDeviceCredentials Device, HttpClient Http);

    [SkippableFact]
    public async Task Cliente_ComMesmoTelefoneDoErp_VinculaSemDuplicarNemSobrescrever()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        // O ERP guarda o telefone do WhatsApp com DDI; o backup guarda mascarado. Mesma pessoa.
        var maria = await SeedClienteAsync(empresa, "Maria do Zap", "5511988887777", "Rua do ERP, 10");
        var homonimo = await SeedClienteAsync(empresa, "Cliente Sem Telefone", null, null);
        var pid = Id("p"); var ca = Id("c"); var cb = Id("c"); var cc = Id("c"); var oid = Id("o");
        var quando = DateTimeOffset.UtcNow.AddDays(-20).ToUnixTimeMilliseconds();
        var request = Pedir(c.Device, [Produto(pid, "Produto " + pid)],
            [Cliente(ca, "Maria Backup", "(11) 98888-7777", quando), Cliente(cb, "Cliente Sem Telefone", null, quando), Cliente(cc, "Ana Nova", "21977776666", quando)],
            [Pedido(oid, ca, "Maria Backup", pid, "Produto " + pid, "entregue", quando, quando)]);

        var recibo = await ImportarAsync(c, request);

        recibo.Warnings.Should().Contain(w => w.Contains("1 cliente ja existia no ERP (mesmo telefone)"), string.Join(" | ", recibo.Warnings));
        await LerAsync(empresa, async db =>
        {
            var clientes = await db.Set<Cliente>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync();
            clientes.Should().HaveCount(4, "2 do ERP + 2 criados; o de mesmo telefone vincula e nao duplica");
            var existente = clientes.Single(x => x.Id == maria.Id);
            existente.Nome.Should().Be("Maria do Zap");
            existente.Endereco.Should().Be("Rua do ERP, 10");
            existente.Telefone.Should().Be("5511988887777");
            clientes.Count(x => x.Nome == "Cliente Sem Telefone").Should().Be(2, "sem telefone nunca vincula por nome: cria");
            clientes.Should().Contain(x => x.Nome == "Ana Nova" && x.Telefone == "21977776666");
            (await db.Set<Client>().SingleAsync(x => x.Id == ca)).ErpClienteId.Should().Be(maria.Id);
            var semTelefone = (await db.Set<Client>().SingleAsync(x => x.Id == cb)).ErpClienteId;
            semTelefone.Should().NotBeNull(); semTelefone.Should().NotBe(homonimo.Id);
            (await db.Set<Pedido>().SingleAsync(x => x.MobileOrderId == oid)).ClienteId.Should().Be(maria.Id);
            return 0;
        });
    }

    [SkippableFact]
    public async Task Cliente_ComTelefoneEmDoisClientesDoErp_RecusaListandoTodosOsCasos()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var joaoA = await SeedClienteAsync(empresa, "Joao Zap", "5511977771111", null);
        var joaoB = await SeedClienteAsync(empresa, "Joao Trabalho", "(11) 97777-1111", null);
        var anaA = await SeedClienteAsync(empresa, "Ana Casa", "21955554444", null);
        var anaB = await SeedClienteAsync(empresa, "Ana Loja", "+55 21 95555-4444", null);
        var pid = Id("p"); var quando = DateTimeOffset.UtcNow.AddDays(-20).ToUnixTimeMilliseconds();
        var request = Pedir(c.Device, [Produto(pid, "Produto " + pid)],
            [Cliente(Id("c"), "Joao Backup", "11977771111", quando), Cliente(Id("c"), "Ana Backup", "21 95555-4444", quando), Cliente(Id("c"), "Livre Backup", "31900000000", quando)], []);

        var erro = await RecusadaAsync(c, request);

        foreach (var id in new[] { joaoA.Id, joaoB.Id, anaA.Id, anaB.Id }) erro.Should().Contain(id.ToString());
        erro.Should().Contain("Joao Backup").And.Contain("Ana Backup").And.NotContain("Livre Backup");
        await AssertNadaGravadoAsync(empresa, clientesDoErp: 4, produtosDoErp: 0);
    }

    [SkippableFact]
    public async Task Produto_DoErpSemSaldoNemLote_VinculaEAbreSoOSaldoDoBackup()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var erp = await SeedProdutoAsync(empresa, c.Device.LojaId, "Lasanha bolonhesa classica");
        var pid = Id("p");
        var request = Pedir(c.Device, [Produto(pid, "LASANHA BOLONHESA CLASSICA", stock: 7)], [], []);

        var recibo = await ImportarAsync(c, request);

        recibo.Warnings.Should().Contain(w => w.Contains("1 produto vinculado ao cadastro existente"), string.Join(" | ", recibo.Warnings));
        await LerAsync(empresa, async db =>
        {
            var produtos = await db.Set<Produto>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync();
            var unico = produtos.Should().ContainSingle("vincula, nao duplica o cadastro").Subject;
            unico.Id.Should().Be(erp.Id);
            unico.Nome.Should().Be("Lasanha bolonhesa classica");
            (unico.PrecoReferencia?.Valor).Should().Be(55m, "preco do ERP nao e sobrescrito pelo backup");
            (unico.CustoReferencia?.Valor).Should().Be(20m, "custo do ERP nao e sobrescrito pelo backup");
            (await db.Set<Product>().SingleAsync(x => x.Id == pid)).ErpProductId.Should().Be(erp.Id);
            var estoque = await db.Set<ItemEstoque>().Where(x => x.ProdutoId == erp.Id).ToListAsync();
            estoque.Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(7, "a abertura vale so para o que veio no backup");
            (await db.Set<MovimentacaoEstoque>().CountAsync()).Should().Be(1);
            return 0;
        });
    }

    [SkippableFact]
    public async Task Produto_DoErpComSaldoOuLoteOuDuplicado_RecusaListandoTodos()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId; var loja = c.Device.LojaId;
        var comSaldo = await SeedProdutoAsync(empresa, loja, "Pao de queijo", saldo: 4);
        var comLote = await SeedProdutoAsync(empresa, loja, "Nhoque", comLote: true);
        var raviolA = await SeedProdutoAsync(empresa, loja, "Ravioli");
        var raviolB = await SeedProdutoAsync(empresa, loja, "RAVIOLI "); // #3101: o índice único ignora a caixa mas não o espaço na ponta; o vínculo ignora os dois
        var livre = await SeedProdutoAsync(empresa, loja, "Tapioca");
        var request = Pedir(c.Device,
            [Produto(Id("p"), "pao de queijo"), Produto(Id("p"), "NHOQUE"), Produto(Id("p"), "ravioli"), Produto(Id("p"), "Tapioca"), Produto(Id("p"), "Sorvete de uva")], [], []);

        var erro = await RecusadaAsync(c, request);

        foreach (var id in new[] { comSaldo.Id, comLote.Id, raviolA.Id, raviolB.Id }) erro.Should().Contain(id.ToString());
        erro.Should().Contain("pao de queijo").And.Contain("NHOQUE").And.Contain("ravioli");
        erro.Should().NotContain("Sorvete de uva", "so entra na lista quem colide").And.NotContain(livre.Id.ToString());
        await AssertNadaGravadoAsync(empresa, clientesDoErp: 0, produtosDoErp: 5);
    }

    [SkippableFact]
    public async Task Produto_ComNomeRepetidoNoArquivo_NaoEColisao_RecusaSoOQueColideComOErp()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        // #2813 B2: nome repetido DENTRO do arquivo deixou de recusar (une no mais recente). Recusa so quem colide com o ERP.
        var comSaldo = await SeedProdutoAsync(empresa, c.Device.LojaId, "Pao de queijo", saldo: 4);
        var request = Pedir(c.Device, [Produto(Id("p"), "Pao de queijo"), Produto(Id("p"), "  pao de QUEIJO "), Produto(Id("p"), "Bolo"), Produto(Id("p"), "BOLO")], [], []);

        var erro = await RecusadaAsync(c, request);

        erro.Should().Contain(comSaldo.Id.ToString()).And.Contain("Ha 1 colisao");
        erro.Should().NotContain("repetido no arquivo").And.NotContain("Deixe um so").And.NotContain("Bolo");
        await AssertNadaGravadoAsync(empresa, clientesDoErp: 0, produtosDoErp: 1);
    }

    [SkippableFact]
    public async Task Reenvio_ComMesmoImportId_DevolveReplayedSemDuplicarVinculos()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var maria = await SeedClienteAsync(empresa, "Maria do Zap", "5511988887777", "Rua do ERP, 10");
        var erp = await SeedProdutoAsync(empresa, c.Device.LojaId, "Lasanha bolonhesa classica");
        var pid = Id("p"); var cid = Id("c"); var oid = Id("o");
        var quando = DateTimeOffset.UtcNow.AddDays(-20).ToUnixTimeMilliseconds();
        var request = Pedir(c.Device, [Produto(pid, "Lasanha Bolonhesa Classica", stock: 7)], [Cliente(cid, "Maria Backup", "11988887777", quando)],
            [Pedido(oid, cid, "Maria Backup", pid, "Lasanha Bolonhesa Classica", "entregue", quando, quando)]);

        var primeiro = await ImportarAsync(c, request);
        var segundo = await ImportarAsync(c, request);

        primeiro.Replayed.Should().BeFalse();
        segundo.Replayed.Should().BeTrue();
        segundo.Warnings.Should().Equal(primeiro.Warnings);
        await LerAsync(empresa, async db =>
        {
            (await db.Set<Cliente>().IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresa)).Should().Be(1);
            (await db.Set<Produto>().IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresa)).Should().Be(1);
            (await db.Set<Pedido>().CountAsync(x => x.EmpresaId == empresa)).Should().Be(1);
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == erp.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(7);
            (await db.Set<MovimentacaoEstoque>().CountAsync()).Should().Be(1);
            (await db.Set<Pedido>().SingleAsync()).ClienteId.Should().Be(maria.Id);
            return 0;
        });
    }

    [SkippableFact]
    public async Task Pedidos_EntregueECancelado_GuardamDatasOriginaisESemDataDeHoje()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var pid = Id("p"); var entregue = Id("o"); var cancelado = Id("o");
        var criado = DateTimeOffset.UtcNow.AddMonths(-5).ToUnixTimeMilliseconds();
        var entregueEm = criado + 3 * 3_600_000L;
        var canceladoEm = criado + 26 * 3_600_000L;
        var request = Pedir(c.Device, [Produto(pid, "Produto " + pid)], [],
            [Pedido(entregue, null, "Cliente A", pid, "Produto " + pid, "entregue", criado, entregueEm),
             Pedido(cancelado, null, "Cliente B", pid, "Produto " + pid, "cancelado", criado, canceladoEm)]);

        await ImportarAsync(c, request);

        var pedidos = await LerAsync(empresa, db => db.Set<Pedido>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync());
        var e = pedidos.Single(x => x.MobileOrderId == entregue);
        var x1 = pedidos.Single(x => x.MobileOrderId == cancelado);
        var um = TimeSpan.FromSeconds(1);
        e.Status.Should().Be("entregue");
        e.CriadoEm.Should().BeCloseTo(Utc(criado), um);
        e.EntreguEm.Should().NotBeNull("relatorio do dia e tela de Entregas leem EntreguEm");
        e.EntreguEm!.Value.Should().BeCloseTo(Utc(entregueEm), um);
        e.CanceladoEm.Should().BeNull();
        x1.Status.Should().Be("cancelado");
        x1.CriadoEm.Should().BeCloseTo(Utc(criado), um);
        x1.CanceladoEm.Should().NotBeNull();
        x1.CanceladoEm!.Value.Should().BeCloseTo(Utc(canceladoEm), um);
        x1.EntreguEm.Should().BeNull();
        var hoje = DateTime.UtcNow.Date;
        foreach (var pedido in pedidos)
            foreach (var propriedade in typeof(Pedido).GetProperties().Where(p => p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateTime?)))
            {
                var valor = propriedade.GetValue(pedido) as DateTime?;
                (valor is null || valor.Value < hoje).Should().BeTrue($"{propriedade.Name} do pedido {pedido.MobileOrderId} recebeu a data de hoje: {valor:O}");
            }
    }

    // ---------- #2813 B2: nome repetido no arquivo une no cadastro mais recente; saldo negativo abre com 0 ----------

    [SkippableFact]
    public async Task NomeRepetidoComAtivoEArquivado_ImportaEUneNoAtivo()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var antigo = DateTimeOffset.UtcNow.AddDays(-60);
        var ativo = IdDatado(antigo); var arquivado = IdDatado(antigo.AddDays(30)); // o arquivado e o mais novo: ativo vence arquivado
        var pudim = Id("p"); var oArquivado = Id("o"); var oAtivo = Id("o"); var bid = Id("b");
        var quando = DateTimeOffset.UtcNow.AddDays(-20).ToUnixTimeMilliseconds();
        var request = Pedir(c.Device,
            [ProdutoDe(ativo, "Lasanha de frango", 5, 20m, "un"), ProdutoDe(arquivado, "Lasanha de frango", 9, 30m, "un", archived: true), Produto(pudim, "Pudim")], [],
            [Pedido(oArquivado, null, "Cliente A", arquivado, "Lasanha de frango", "entregue", quando, quando, 30m),
             Pedido(oAtivo, null, "Cliente B", ativo, "Lasanha de frango", "entregue", quando, quando, 20m)],
            [LoteDe(bid, arquivado, "Lasanha de frango", quando)]);

        var recibo = await ImportarAsync(c, request);

        recibo.Counts["products"].Should().Be(3, "a resposta continua contando as linhas do arquivo");
        recibo.Warnings.Should().Contain(w => w.Contains("1 produto aparecia repetido") && w.Contains("1 registro a mais"), string.Join(" | ", recibo.Warnings));
        await LerAsync(empresa, async db =>
        {
            var erp = await db.Set<Produto>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync();
            erp.Should().HaveCount(2, "o nome repetido vira um cadastro so, mais o Pudim");
            var lasanha = erp.Single(x => x.Nome == "Lasanha de frango");
            (lasanha.PrecoReferencia?.Valor).Should().Be(20m, "vence o ativo, mesmo com o arquivado mais novo");
            var espelhos = await db.Set<Product>().Where(x => x.Id == ativo || x.Id == arquivado).ToListAsync();
            espelhos.Should().HaveCount(2, "as duas linhas do espelho continuam");
            espelhos.Should().OnlyContain(x => x.ErpProductId == lasanha.Id);
            var pedidos = await db.Set<Pedido>().Include(x => x.Itens).Where(x => x.MobileOrderId == oArquivado || x.MobileOrderId == oAtivo).ToListAsync();
            pedidos.Should().HaveCount(2);
            pedidos.SelectMany(x => x.Itens).Should().OnlyContain(i => i.ProdutoId == lasanha.Id, "pedido do id arquivado e do ativo apontam para o mesmo produto");
            var lote = await db.Set<Lote>().IgnoreQueryFilters().Include(x => x.Itens).SingleAsync(x => x.EmpresaId == empresa && x.MobileBatchId == bid);
            lote.Itens.Should().ContainSingle().Which.ProdutoId.Should().Be(lasanha.Id);
            var estoque = await db.Set<ItemEstoque>().Where(x => x.ProdutoId == lasanha.Id).ToListAsync();
            estoque.Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(5, "o arquivado (9) fica fora da soma; so o ativo conta");
            (await db.Set<ItemEstoque>().CountAsync(x => x.EmpresaId == empresa)).Should().Be(2, "Lasanha e Pudim; nenhum item de estoque do arquivado");
            return 0;
        });
    }

    [SkippableFact]
    public async Task NomeRepetidoComDoisAtivos_VenceOMaisRecente()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var origem = DateTimeOffset.UtcNow.AddDays(-90);
        var velho = IdDatado(origem); var meio = IdDatado(origem.AddDays(30)); var recente = IdDatado(origem.AddDays(60));
        var oVelho = Id("o"); var oRecente = Id("o");
        var quando = DateTimeOffset.UtcNow.AddDays(-20).ToUnixTimeMilliseconds();
        // O mais recente pela data do id vem no meio do arquivo: vale a data, nao a posicao.
        var request = Pedir(c.Device,
            [ProdutoDe(velho, "Nhoque", 3, 12m, "300g"), ProdutoDe(recente, "Nhoque", 8, 15m, "300 G"), ProdutoDe(meio, "Nhoque", 5, 14m, "300 g")], [],
            [Pedido(oVelho, null, "Cliente A", velho, "Nhoque", "entregue", quando, quando, 12m),
             Pedido(oRecente, null, "Cliente B", recente, "Nhoque", "entregue", quando, quando, 15m)]);

        await ImportarAsync(c, request);

        await LerAsync(empresa, async db =>
        {
            var nhoque = (await db.Set<Produto>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync()).Should().ContainSingle().Subject;
            nhoque.Nome.Should().Be("Nhoque 300 g", "mesmo nome e mesma embalagem, escrita de tres jeitos, e um produto so");
            (nhoque.PrecoReferencia?.Valor).Should().Be(15m, "preco do mais recente");
            var espelhos = await db.Set<Product>().Where(x => x.Id == velho || x.Id == meio || x.Id == recente).ToListAsync();
            espelhos.Should().HaveCount(3).And.OnlyContain(x => x.ErpProductId == nhoque.Id);
            espelhos.Single(x => x.Id == recente).Unit.Should().Be("300 G", "embalagem do vencedor, como veio");
            espelhos.Single(x => x.Id == velho).Unit.Should().Be("300g", "o perdedor mantem a propria linha do espelho");
            var estoque = await db.Set<ItemEstoque>().Where(x => x.ProdutoId == nhoque.Id).ToListAsync();
            estoque.Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(16, "#2813 C: o vencedor abre com a soma dos tres saldos ativos distintos (3 + 5 + 8)");
            (await db.Set<MovimentacaoEstoque>().CountAsync()).Should().Be(1, "so o vencedor abre saldo, num movimento so; o perdedor nao abre o proprio");
            var pedidos = await db.Set<Pedido>().Include(x => x.Itens).Where(x => x.MobileOrderId == oVelho || x.MobileOrderId == oRecente).ToListAsync();
            var doVelho = pedidos.Single(x => x.MobileOrderId == oVelho).Itens.Single();
            doVelho.ProdutoId.Should().Be(nhoque.Id, "pedido do id antigo cai no mesmo produto");
            doVelho.PrecoUnitario.Should().Be(12m, "o preco do item do pedido e o da venda, nao o do cadastro");
            pedidos.Single(x => x.MobileOrderId == oRecente).Itens.Single().ProdutoId.Should().Be(nhoque.Id);
            return 0;
        });
    }

    [SkippableFact]
    public async Task GrupoSoDeArquivados_Importa()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var origem = DateTimeOffset.UtcNow.AddDays(-90);
        var velho = IdDatado(origem); var novo = IdDatado(origem.AddDays(30)); var avulso = Id("p"); var oVelho = Id("o");
        var quando = DateTimeOffset.UtcNow.AddDays(-20).ToUnixTimeMilliseconds();
        var request = Pedir(c.Device,
            [ProdutoDe(velho, "Ravioli de queijo", 2, 18m, "un", archived: true), ProdutoDe(novo, "Ravioli de queijo", 4, 22m, "un", archived: true), ProdutoDe(avulso, "Pudim antigo", 6, 9m, "un", archived: true)], [],
            [Pedido(oVelho, null, "Cliente A", velho, "Ravioli de queijo", "entregue", quando, quando, 18m)]);

        var recibo = await ImportarAsync(c, request);

        recibo.Counts["products"].Should().Be(3);
        await LerAsync(empresa, async db =>
        {
            var erp = await db.Set<Produto>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync();
            erp.Should().HaveCount(2, "o grupo de arquivados vira um cadastro so, como o arquivado avulso");
            var grupo = erp.Single(x => x.Nome == "Ravioli de queijo"); var unico = erp.Single(x => x.Nome == "Pudim antigo");
            (grupo.PrecoReferencia?.Valor).Should().Be(22m, "sem nenhum ativo, vale o arquivado mais recente");
            grupo.Status.Should().Be(unico.Status, "o grupo de arquivados recebe o mesmo tratamento do arquivado avulso");
            (await db.Set<Product>().Where(x => x.Id == velho || x.Id == novo).ToListAsync()).Should().HaveCount(2).And.OnlyContain(x => x.ErpProductId == grupo.Id);
            (await db.Set<Pedido>().Include(x => x.Itens).SingleAsync(x => x.MobileOrderId == oVelho)).Itens.Single().ProdutoId.Should().Be(grupo.Id);
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == grupo.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(4);
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == unico.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(6, "igual ao arquivado avulso");
            return 0;
        });
    }

    [SkippableFact]
    public async Task NomeComEspacoEMaiusculaDiferente_UneNoMesmoGrupo()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var origem = DateTimeOffset.UtcNow.AddDays(-90);
        var velho = IdDatado(origem); var novo = IdDatado(origem.AddDays(30));
        var request = Pedir(c.Device, [ProdutoDe(velho, "Lasanha ", 4, 11m, "un"), ProdutoDe(novo, "lasanha", 6, 13m, "un")], [], []);

        var recibo = await ImportarAsync(c, request);

        recibo.Warnings.Should().Contain(w => w.Contains("1 produto aparecia repetido"), string.Join(" | ", recibo.Warnings));
        await LerAsync(empresa, async db =>
        {
            var erp = await db.Set<Produto>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync();
            var unico = erp.Should().ContainSingle("\"Lasanha \" e \"lasanha\" sao o mesmo nome").Subject;
            (unico.PrecoReferencia?.Valor).Should().Be(13m, "vale o mais recente");
            (await db.Set<Product>().Where(x => x.Id == velho || x.Id == novo).ToListAsync()).Should().HaveCount(2).And.OnlyContain(x => x.ErpProductId == unico.Id);
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == unico.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(10, "#2813 C: soma dos dois ativos distintos (4 + 6)");
            return 0;
        });
    }

    [SkippableFact]
    public async Task AvisoDeUniaoNaResposta_TrazNomesERegistrosExtras()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var o = DateTimeOffset.UtcNow.AddDays(-90);
        var request = Pedir(c.Device,
            [Produto(IdDatado(o), "Torta"), Produto(IdDatado(o.AddDays(1)), "torta"), Produto(IdDatado(o.AddDays(2)), "TORTA "),
             Produto(IdDatado(o.AddDays(3)), "Quiche"), Produto(IdDatado(o.AddDays(4)), "quiche"),
             Produto(IdDatado(o.AddDays(5)), "Empada"), Produto(IdDatado(o.AddDays(6)), "EMPADA"), Produto(Id("p"), "Sorvete")], [], []);

        var recibo = await ImportarAsync(c, request);

        recibo.Counts["products"].Should().Be(8, "a resposta continua contando as linhas do arquivo");
        recibo.Warnings.Should().ContainSingle(w => w.Contains("3 produtos apareciam repetidos") && w.Contains("4 registros a mais"), string.Join(" | ", recibo.Warnings));
        (await LerAsync(empresa, db => db.Set<Produto>().IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresa))).Should().Be(4, "Torta, Quiche, Empada e Sorvete");
    }

    [SkippableFact]
    public async Task Reenvio_ComMesmoImportId_ComNomesRepetidos_NaoDuplicaNada()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var origem = DateTimeOffset.UtcNow.AddDays(-90);
        var velho = IdDatado(origem); var novo = IdDatado(origem.AddDays(30)); var oid = Id("o");
        var quando = DateTimeOffset.UtcNow.AddDays(-20).ToUnixTimeMilliseconds();
        var request = Pedir(c.Device, [Produto(velho, "Brigadeiro"), Produto(novo, "brigadeiro"), Produto(Id("p"), "Beijinho")], [],
            [Pedido(oid, null, "Cliente A", velho, "Brigadeiro", "entregue", quando, quando)]);

        var primeiro = await ImportarAsync(c, request);
        var segundo = await ImportarAsync(c, request);

        primeiro.Replayed.Should().BeFalse();
        segundo.Replayed.Should().BeTrue();
        segundo.Warnings.Should().Equal(primeiro.Warnings);
        segundo.Counts["products"].Should().Be(3);
        await LerAsync(empresa, async db =>
        {
            (await db.Set<Produto>().IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresa)).Should().Be(2);
            (await db.Set<Product>().CountAsync(x => x.EmpresaId == empresa)).Should().Be(3);
            (await db.Set<ItemEstoque>().CountAsync(x => x.EmpresaId == empresa)).Should().Be(2);
            (await db.Set<MovimentacaoEstoque>().CountAsync()).Should().Be(2);
            (await db.Set<Pedido>().CountAsync(x => x.EmpresaId == empresa)).Should().Be(1);
            return 0;
        });
    }

    [SkippableFact]
    public async Task SaldoNegativo_EmProdutoUnico_ImportaAbreComZeroEAvisa()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var negativo = Id("p"); var positivo = Id("p");
        var request = Pedir(c.Device, [Produto(negativo, "Torta salgada", stock: -13), Produto(positivo, "Pudim", stock: 7)], [], []);

        var recibo = await ImportarAsync(c, request);

        recibo.Warnings.Should().Contain(w => w.Contains("1 produto com saldo negativo entrou com saldo 0") && w.Contains("soma zerada: -13"), string.Join(" | ", recibo.Warnings));
        await LerAsync(empresa, async db =>
        {
            var espelho = await db.Set<Product>().SingleAsync(x => x.Id == negativo);
            var erpId = espelho.ErpProductId!.Value;
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == erpId).ToListAsync()).Should().BeEmpty("saldo negativo abre com 0: sem item de estoque");
            espelho.Stock.Should().Be(-13, "o espelho guarda o numero do aparelho; o arquivo original segue intacto no backup");
            (await db.Set<ItemEstoque>().CountAsync(x => x.EmpresaId == empresa)).Should().Be(1, "so o Pudim abre saldo");
            return 0;
        });
    }

    // #2813 C: o vencedor negativo abre com o saldo dos outros ativos positivos (Ravioli); so abre 0 e avisa quando o grupo nao tem nenhum ativo positivo (Capelete).
    [SkippableFact]
    public async Task SaldoNegativo_NoVencedorDoGrupo_SoAbreComZeroSeNaoHaOutroAtivoPositivo()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var origem = DateTimeOffset.UtcNow.AddDays(-90);
        var raviolVelho = IdDatado(origem); var raviolNovo = IdDatado(origem.AddDays(30));
        var capeleteVelho = IdDatado(origem.AddDays(1)); var capeleteNovo = IdDatado(origem.AddDays(31));
        var request = Pedir(c.Device,
            [ProdutoDe(raviolVelho, "Ravioli", 6, 10m, "un"), ProdutoDe(raviolNovo, "Ravioli", -4, 12m, "un"),
             ProdutoDe(capeleteVelho, "Capelete", -2, 10m, "un"), ProdutoDe(capeleteNovo, "Capelete", -13, 12m, "un")], [], []);

        var recibo = await ImportarAsync(c, request);

        recibo.Warnings.Should().Contain(w => w.Contains("1 produto com saldo negativo entrou com saldo 0") && w.Contains("soma zerada: -13"), string.Join(" | ", recibo.Warnings));
        recibo.Warnings.Should().NotContain(w => w.Contains("2 produtos com saldo negativo"), "o Ravioli abre com o saldo do outro ativo: nao entrou com 0");
        await LerAsync(empresa, async db =>
        {
            var erp = await db.Set<Produto>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync();
            erp.Should().HaveCount(2);
            var ravioli = erp.Single(x => x.Nome == "Ravioli");
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == ravioli.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(6, "o vencedor negativo abre com o saldo do outro ativo");
            (await db.Set<ItemEstoque>().CountAsync(x => x.EmpresaId == empresa)).Should().Be(1, "o Capelete nao tem ativo positivo: abre com 0, sem item de estoque");
            return 0;
        });
    }

    // ---------- #2813 C ("Somar, menos copias"): o vencedor abre com a soma dos saldos positivos DISTINTOS dos registros ativos do grupo ----------

    // Cada linha e um grupo de registros do mesmo nome composto, do mais antigo ao mais novo (o ultimo ativo vence). "*" marca o arquivado.
    [SkippableTheory]
    [InlineData("dois ativos com saldos diferentes somam", "3, 5", "8")]
    [InlineData("dois ativos com saldo igual contam uma vez (copia)", "4, 4", "4")]
    [InlineData("tres ativos 5, 5 e 3 somam 8", "5, 5, 3", "8")]
    [InlineData("a copia no meio nao muda a soma", "5, 3, 5", "8")]
    [InlineData("arquivado com saldo positivo fica fora", "6, 9*", "6")]
    [InlineData("saldo negativo fica fora", "5, -3", "5")]
    [InlineData("vencedor zerado abre com o saldo do outro ativo", "7, 0", "7")]
    [InlineData("vencedor negativo abre com o saldo do outro ativo", "7, -2", "7")]
    [InlineData("fracoes somam sem arredondar", "2.5, 1.25", "3.75")]
    [InlineData("mesmo saldo com escala diferente e a mesma copia", "2.5, 2.50", "2.5")]
    [InlineData("ativos, copias, arquivados e negativo juntos", "5, 5*, 3, 4*, -2, 5", "8")]
    [InlineData("sem ativo positivo: nada abre", "0, -2", "0")]
    [InlineData("arquivado positivo nao abre quando o ativo esta zerado", "5*, 0", "0")]
    [InlineData("so arquivados: abre o saldo do mais recente, como antes", "2*, 4*", "4")]
    [InlineData("registro unico abre o proprio saldo", "7", "7")]
    [InlineData("registro unico negativo nao abre nada", "-3", "0")]
    [InlineData("registro unico arquivado abre o proprio saldo, como antes", "4*", "4")]
    public async Task GrupoRepetido_AbreComASomaDosSaldosPositivosDistintosDosAtivos(string caso, string registros, string esperado)
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var origem = DateTimeOffset.UtcNow.AddDays(-90);
        var produtos = registros.Split(',').Select(x => x.Trim()).Select((x, i) =>
            ProdutoDe(IdDatado(origem.AddDays(i)), "Produto A", decimal.Parse(x.TrimEnd('*'), System.Globalization.CultureInfo.InvariantCulture), 10m, "un", archived: x.EndsWith('*'))).ToArray<object>();

        await ImportarAsync(c, Pedir(c.Device, produtos, [], []));

        var saldo = decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture);
        await LerAsync(empresa, async db =>
        {
            var erp = (await db.Set<Produto>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync()).Should().ContainSingle(caso).Subject;
            var itens = await db.Set<ItemEstoque>().Where(x => x.ProdutoId == erp.Id).ToListAsync();
            if (saldo == 0) itens.Should().BeEmpty(caso);
            else itens.Should().ContainSingle(caso).Which.QuantidadeAtual.Value.Should().Be(saldo, caso);
            (await db.Set<MovimentacaoEstoque>().CountAsync()).Should().Be(saldo == 0 ? 0 : 1, "so o vencedor abre saldo, num movimento so: " + caso);
            return 0;
        });
    }

    [SkippableFact]
    public async Task Reenvio_ComMesmoImportId_ComSaldosSomados_NaoSomaDeNovo()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var origem = DateTimeOffset.UtcNow.AddDays(-90);
        var request = Pedir(c.Device, [ProdutoDe(IdDatado(origem), "Produto A", 3, 10m, "un"), ProdutoDe(IdDatado(origem.AddDays(30)), "Produto A", 5, 12m, "un")], [], []);

        var primeiro = await ImportarAsync(c, request);
        var segundo = await ImportarAsync(c, request);

        primeiro.Replayed.Should().BeFalse();
        segundo.Replayed.Should().BeTrue();
        await LerAsync(empresa, async db =>
        {
            var erp = (await db.Set<Produto>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync()).Should().ContainSingle().Subject;
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == erp.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(8, "o reenvio e reconhecido pelo ImportId e nao abre saldo outra vez");
            (await db.Set<MovimentacaoEstoque>().CountAsync()).Should().Be(1, "um movimento de abertura so");
            return 0;
        });
    }

    [SkippableFact]
    public async Task NomeRepetidoQueCasaComProdutoDoErp_VinculaSoOVencedor_EAliasNaoViraSegundoLink()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var erp = await SeedProdutoAsync(empresa, c.Device.LojaId, "Tapioca"); // sem saldo e sem lote
        var origem = DateTimeOffset.UtcNow.AddDays(-90);
        var velho = IdDatado(origem); var novo = IdDatado(origem.AddDays(30));
        var request = Pedir(c.Device, [ProdutoDe(velho, "tapioca", 2, 11m, "un"), ProdutoDe(novo, "TAPIOCA", 6, 13m, "un")], [], []);

        var recibo = await ImportarAsync(c, request);

        recibo.Warnings.Should().Contain(w => w.Contains("1 produto vinculado ao cadastro existente"), string.Join(" | ", recibo.Warnings));
        recibo.Warnings.Should().Contain(w => w.Contains("1 produto aparecia repetido"), string.Join(" | ", recibo.Warnings));
        await LerAsync(empresa, async db =>
        {
            var produtos = await db.Set<Produto>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync();
            var unico = produtos.Should().ContainSingle("vincula ao cadastro do ERP, nao duplica").Subject;
            unico.Id.Should().Be(erp.Id);
            (unico.PrecoReferencia?.Valor).Should().Be(55m, "preco do ERP nao e sobrescrito pelo backup");
            (await db.Set<Product>().Where(x => x.Id == velho || x.Id == novo).ToListAsync()).Should().HaveCount(2).And.OnlyContain(x => x.ErpProductId == erp.Id);
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == erp.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(8, "#2813 C: o saldo aberto e a soma dos dois ativos distintos (2 + 6)");
            return 0;
        });
    }

    // #2813: a embalagem faz parte da chave do produto. "300g" e "300 G" sao a mesma embalagem; "un" e " UN " sao a unidade padrao
    // do app, nao embalagem. Cadastro e saldo vem do vencedor, mas a linha de pedido e a de lote do alias guardam a unidade que tinham.
    [SkippableFact]
    public async Task NomeEEmbalagemRepetidos_EscritosDeOutroJeito_UnemEAsLinhasDoAliasGuardamAPropriaUnidade()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var origem = DateTimeOffset.UtcNow.AddDays(-90);
        var coalhoVelho = IdDatado(origem); var coalhoNovo = IdDatado(origem.AddDays(30));
        var paoVelho = IdDatado(origem.AddDays(1)); var paoNovo = IdDatado(origem.AddDays(31));
        var cliente = Id("c"); var pedido = Id("o"); var lote = Id("l");
        var quando = origem.AddDays(40).ToUnixTimeMilliseconds();
        var request = Pedir(c.Device,
            [ProdutoDe(coalhoVelho, "Queijo coalho", 3, 9m, "300g"), ProdutoDe(coalhoNovo, "queijo coalho ", 7, 20m, "300 G"),
             ProdutoDe(paoVelho, "Pao frances", 2, 1m, "un"), ProdutoDe(paoNovo, "Pao Frances", 5, 1m, " UN ")],
            [Cliente(cliente, "Cliente do queijo", "11955554444", quando)],
            [PedidoComUnidade(pedido, cliente, "Cliente do queijo", coalhoVelho, "Queijo coalho", "300g", quando)],
            [LoteComUnidade(lote, coalhoVelho, "Queijo coalho", quando, "300g")]);

        var recibo = await ImportarAsync(c, request);

        recibo.Counts["products"].Should().Be(4, "o recibo conta as linhas do arquivo, nao os cadastros");
        recibo.Warnings.Should().Contain(w => w.Contains("2 produtos apareciam repetidos") && w.Contains("2 registros a mais"), string.Join(" | ", recibo.Warnings));
        recibo.Warnings.Should().NotContain(w => w.Contains("unidades diferentes"), "embalagem diferente nao une mais; nao ha o que avisar");
        recibo.Warnings.Should().NotContain(w => w.Contains("coalho", StringComparison.OrdinalIgnoreCase) || w.Contains("frances", StringComparison.OrdinalIgnoreCase), "o aviso conta, nunca cita nomes");
        await LerAsync(empresa, async db =>
        {
            var vencedor = await db.Set<Product>().SingleAsync(x => x.Id == coalhoNovo);
            var alias = await db.Set<Product>().SingleAsync(x => x.Id == coalhoVelho);
            vencedor.Unit.Should().Be("300 G", "o espelho do vencedor guarda a embalagem como veio");
            alias.Unit.Should().Be("300g", "o espelho do alias guarda a embalagem como veio");
            alias.ErpProductId.Should().Be(vencedor.ErpProductId);
            var produtos = await db.Set<Produto>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync();
            produtos.Select(x => x.Nome).Should().BeEquivalentTo(new[] { "queijo coalho 300 g", "Pao Frances" }, "um Produto do ERP por nome e embalagem; o nome vem do vencedor e o un nao entra");
            var coalho = produtos.Single(x => x.Id == vencedor.ErpProductId);
            (coalho.PrecoReferencia?.Valor).Should().Be(20m, "o cadastro do ERP vem do vencedor");
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == coalho.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(10, "#2813 C: soma dos dois ativos distintos (3 do alias + 7 do vencedor)");
            var pao = produtos.Single(x => x.Nome == "Pao Frances");
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == pao.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(7, "#2813 C: o outro grupo tambem soma (2 + 5)");
            var linha = (await db.Set<Pedido>().Include(x => x.Itens).SingleAsync(x => x.MobileOrderId == pedido)).Itens.Single();
            linha.ProdutoId.Should().Be(coalho.Id, "o pedido do alias aponta para o cadastro unico");
            linha.Unidade.Should().Be("300g", "a linha do pedido guarda a unidade que tinha, nao a do vencedor");
            var itemDoLote = (await db.Set<Lote>().IgnoreQueryFilters().Include(x => x.Itens).SingleAsync(x => x.EmpresaId == empresa && x.MobileBatchId == lote)).Itens.Single();
            itemDoLote.ProdutoId.Should().Be(coalho.Id, "o lote do alias aponta para o cadastro unico");
            itemDoLote.Unidade.Should().Be("300g", "a linha do lote guarda a unidade que tinha, nao a do vencedor");
            return 0;
        });
    }

    // #2813 (a): o produto do ERP e nome + embalagem. Mesmo nome com duas embalagens sao dois produtos, cada um com o proprio
    // saldo de abertura: nenhum saldo de variante ativa se perde.
    [SkippableFact]
    public async Task MesmoNomeComDuasEmbalagens_ViraDoisProdutos_CadaUmComSeuSaldo()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var origem = DateTimeOffset.UtcNow.AddDays(-90);
        var pequena = IdDatado(origem); var grande = IdDatado(origem.AddDays(30)); var oPequena = Id("o"); var oGrande = Id("o");
        var quando = origem.AddDays(40).ToUnixTimeMilliseconds();
        var request = Pedir(c.Device,
            [ProdutoDe(pequena, "Lasanha de frango", 5, 20m, "300g"), ProdutoDe(grande, "Lasanha de frango", 9, 30m, "500g")], [],
            [Pedido(oPequena, null, "Cliente A", pequena, "Lasanha de frango", "entregue", quando, quando, 20m),
             Pedido(oGrande, null, "Cliente B", grande, "Lasanha de frango", "entregue", quando, quando, 30m)]);

        var recibo = await ImportarAsync(c, request);

        recibo.Counts["products"].Should().Be(2);
        recibo.Warnings.Should().NotContain(w => w.Contains("repetido"), "embalagens diferentes nao sao repeticao: " + string.Join(" | ", recibo.Warnings));
        await LerAsync(empresa, async db =>
        {
            var erp = await db.Set<Produto>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync();
            erp.Select(x => x.Nome).Should().BeEquivalentTo(new[] { "Lasanha de frango 300 g", "Lasanha de frango 500 g" });
            var p300 = erp.Single(x => x.Nome == "Lasanha de frango 300 g"); var p500 = erp.Single(x => x.Nome == "Lasanha de frango 500 g");
            (p300.PrecoReferencia?.Valor).Should().Be(20m); (p500.PrecoReferencia?.Valor).Should().Be(30m);
            (await db.Set<Product>().SingleAsync(x => x.Id == pequena)).ErpProductId.Should().Be(p300.Id);
            (await db.Set<Product>().SingleAsync(x => x.Id == grande)).ErpProductId.Should().Be(p500.Id);
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == p300.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(5);
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == p500.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(9);
            var pedidos = await db.Set<Pedido>().Include(x => x.Itens).Where(x => x.MobileOrderId == oPequena || x.MobileOrderId == oGrande).ToListAsync();
            pedidos.Single(x => x.MobileOrderId == oPequena).Itens.Single().ProdutoId.Should().Be(p300.Id, "cada pedido aponta para a embalagem que vendeu");
            pedidos.Single(x => x.MobileOrderId == oGrande).Itens.Single().ProdutoId.Should().Be(p500.Id);
            return 0;
        });
    }

    // #2813 (c): sem embalagem (vazia, em branco ou a unidade padrao un) o produto do ERP fica so com o nome e convive com a variante embalada.
    [SkippableFact]
    public async Task SemEmbalagem_FicaSoComONome_EConviveComAVarianteEmbalada()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var request = Pedir(c.Device,
            [ProdutoDe(Id("p"), "Pudim", 4, 9m, "un"), ProdutoDe(Id("p"), "Torta", 6, 15m, ""), ProdutoDe(Id("p"), "Quiche", 2, 12m, "  "),
             ProdutoDe(Id("p"), "Torta", 3, 18m, "400g")], [], []);

        await ImportarAsync(c, request);

        await LerAsync(empresa, async db =>
        {
            var erp = await db.Set<Produto>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync();
            erp.Select(x => x.Nome).Should().BeEquivalentTo(new[] { "Pudim", "Torta", "Quiche", "Torta 400 g" }, "sem embalagem fica so o nome; a variante embalada e outro produto");
            (await db.Set<ItemEstoque>().CountAsync(x => x.EmpresaId == empresa)).Should().Be(4, "a variante embalada nao absorve o saldo da simples");
            return 0;
        });
    }

    // #2813 (d): a colisao com o ERP compara pelo nome composto. O mesmo nome composto sem saldo nem lote vincula; o nome simples nao colide.
    [SkippableFact]
    public async Task Produto_ComEmbalagem_VinculaAoProdutoDoErpComOMesmoNomeComposto()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var composto = await SeedProdutoAsync(empresa, c.Device.LojaId, "Lasanha de frango 300 g"); // sem saldo e sem lote
        var simples = await SeedProdutoAsync(empresa, c.Device.LojaId, "Lasanha de frango", saldo: 8); // outro nome no ERP: nao colide com a variante
        var origem = DateTimeOffset.UtcNow.AddDays(-90);
        var pequena = IdDatado(origem); var grande = IdDatado(origem.AddDays(30));
        var request = Pedir(c.Device, [ProdutoDe(pequena, "Lasanha de frango", 5, 20m, "300g"), ProdutoDe(grande, "Lasanha de frango", 9, 30m, "500g")], [], []);

        var recibo = await ImportarAsync(c, request);

        recibo.Warnings.Should().Contain(w => w.Contains("1 produto vinculado ao cadastro existente"), string.Join(" | ", recibo.Warnings));
        await LerAsync(empresa, async db =>
        {
            var erp = await db.Set<Produto>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync();
            erp.Should().HaveCount(3, "o 300 g vincula, o 500 g nasce e o de nome simples fica como estava");
            var nova = erp.Single(x => x.Nome == "Lasanha de frango 500 g");
            (await db.Set<Product>().SingleAsync(x => x.Id == pequena)).ErpProductId.Should().Be(composto.Id);
            (await db.Set<Product>().SingleAsync(x => x.Id == grande)).ErpProductId.Should().Be(nova.Id);
            (erp.Single(x => x.Id == composto.Id).PrecoReferencia?.Valor).Should().Be(55m, "preco do ERP nao e sobrescrito pelo backup");
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == composto.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(5);
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == nova.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(9);
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == simples.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(8, "o produto de nome simples do ERP nao foi tocado");
            return 0;
        });
    }

    [SkippableFact]
    public async Task Produto_ComEmbalagem_RecusaSeOProdutoDoErpComOMesmoNomeCompostoTemSaldo()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var comSaldo = await SeedProdutoAsync(empresa, c.Device.LojaId, "Lasanha de frango 300 g", saldo: 4);
        var request = Pedir(c.Device, [ProdutoDe(Id("p"), "Lasanha de frango", 5, 20m, "300g"), ProdutoDe(Id("p"), "Lasanha de frango", 9, 30m, "500g")], [], []);

        var erro = await RecusadaAsync(c, request);

        erro.Should().Contain(comSaldo.Id.ToString()).And.Contain("Lasanha de frango 300 g").And.Contain("Ha 1 colisao");
        erro.Should().NotContain("500 g", "a outra embalagem nao colide");
        await AssertNadaGravadoAsync(empresa, clientesDoErp: 0, produtosDoErp: 1);
    }

    // #2813 (e): o nome composto e estavel: nao repete a embalagem que o nome ja traz, usa sempre o mesmo formato e o reenvio nao duplica.
    [SkippableFact]
    public async Task NomeComposto_EEstavel_NaoRepeteAEmbalagemDoNome_EReenvioNaoDuplica()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var request = Pedir(c.Device,
            [ProdutoDe(Id("p"), "Lasanha de frango 300 g", 5, 20m, "300g"), ProdutoDe(Id("p"), "  PUDIM  ", 2, 9m, " 250ML "), ProdutoDe(Id("p"), "Torta", 1, 5m, "un")], [], []);

        var primeiro = await ImportarAsync(c, request);
        var segundo = await ImportarAsync(c, request);

        primeiro.Replayed.Should().BeFalse();
        segundo.Replayed.Should().BeTrue();
        segundo.Warnings.Should().Equal(primeiro.Warnings);
        await LerAsync(empresa, async db =>
        {
            var erp = await db.Set<Produto>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresa).ToListAsync();
            erp.Select(x => x.Nome).Should().BeEquivalentTo(new[] { "Lasanha de frango 300 g", "PUDIM 250 ml", "Torta" }, "a embalagem do nome nao se repete e o formato e sempre o mesmo");
            (await db.Set<Product>().CountAsync(x => x.EmpresaId == empresa)).Should().Be(3);
            (await db.Set<ItemEstoque>().CountAsync(x => x.EmpresaId == empresa)).Should().Be(3);
            return 0;
        });
    }

    // #2813 B2: 73 produtos do arquivo real trazem price null. Passa na validacao e grava sem preco (nao inventa 0).
    [SkippableFact]
    public async Task ProdutoComPriceNull_PassaNaValidacaoEGravaSemPreco()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var semPreco = Id("p"); var comPreco = Id("p");
        var request = Pedir(c.Device, [ProdutoPriceNull(semPreco, "Brigadeiro", 4), Produto(comPreco, "Beijinho")], [], []);

        var recibo = await ImportarAsync(c, request);

        recibo.Counts["products"].Should().Be(2);
        await LerAsync(empresa, async db =>
        {
            var espelho = await db.Set<Product>().SingleAsync(x => x.Id == semPreco);
            espelho.Price.Should().BeNull("o arquivo trouxe price null: o espelho guarda sem preco");
            var erp = await db.Set<Produto>().IgnoreQueryFilters().SingleAsync(x => x.Id == espelho.ErpProductId);
            (erp.PrecoReferencia?.Valor).Should().BeNull("sem preco informado o ERP fica sem preco de referencia, nao 0");
            erp.Status.Should().Be(StatusProduto.Ativo);
            (await db.Set<ItemEstoque>().Where(x => x.ProdutoId == erp.Id).ToListAsync()).Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(4, "o saldo abre normalmente");
            return 0;
        });
    }

    private static object PedidoComUnidade(string id, string clientId, string clientName, string productId, string productName, string unit, long quando) =>
        new { id, clientId, clientSnapshot = new { name = clientName }, items = new[] { new { productId, name = productName, qty = 1, unitPrice = 10m, unit } }, total = 10m, status = "entregue", createdAt = quando, updatedAt = quando };

    private static object LoteComUnidade(string id, string productId, string productName, long createdAt, string unit) =>
        new { id, code = "LEG-" + id[^8..], createdAt, items = new[] { new { productId, name = productName, qty = 5, weightG = 100, validityDays = 30, unit } } };

    // price null EXPLICITO, como o PWA antigo grava (JsonNode escreve o null mesmo com a politica de ignorar nulos).
    private static object ProdutoPriceNull(string id, string name, int stock) =>
        System.Text.Json.Nodes.JsonNode.Parse($$"""{"id":"{{id}}","name":"{{name}}","category":"massa","stock":{{stock}},"price":null,"cost":null,"unit":"un"}""")!;

    // ---------- apoio ----------

    private static string Id(string prefixo) => prefixo + "-" + Guid.NewGuid().ToString("N");
    private static DateTime Utc(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    private static string Sha(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    private static object Produto(string id, string name, int stock = 7) => new { id, name, category = "massa", stock, price = 10m, cost = 3m, unit = "un" };
    private static object Cliente(string id, string name, string? phone, long lastOrder) => new { id, name, apt = "62", address = "Endereco do backup", phone, lastOrder, orderCount = 1 };
    private static object Pedido(string id, string? clientId, string clientName, string productId, string productName, string status, long createdAt, long updatedAt, decimal unitPrice = 10m) =>
        new { id, clientId, clientSnapshot = new { name = clientName }, items = new[] { new { productId, name = productName, qty = 1, unitPrice } }, total = unitPrice, status, createdAt, updatedAt };

    // #2813 B2: o id do app e "<prefixo>-<data em ms>"; o prefixo e unico por teste porque mobile_products.Id e global.
    private static string IdDatado(DateTimeOffset quando) => "p" + Guid.NewGuid().ToString("N")[..10] + "-" + quando.ToUnixTimeMilliseconds();
    private static object ProdutoDe(string id, string name, decimal stock, decimal price, string unit, bool archived = false) =>
        new { id, name, category = "massa", stock, price, cost = 3m, unit, archived };
    private static object LoteDe(string id, string productId, string productName, long createdAt) =>
        new { id, code = "LEG-" + id[^8..], createdAt, items = new[] { new { productId, name = productName, qty = 5, weightG = 100, validityDays = 30 } } };

    private static LegacySnapshotRequest Pedir(MobileDeviceCredentials d, object[] products, object[] clients, object[] orders, object[]? lotes = null)
    {
        var json = JsonSerializer.Serialize(new { products, clients, orders, batches = lotes ?? Array.Empty<object>(), cashEntries = Array.Empty<object>(), cashClosings = Array.Empty<object>() }, Json);
        return new LegacySnapshotRequest(Guid.NewGuid(), json, Sha(json), ["mut_" + Guid.NewGuid()], true, d.EmpresaId, d.LojaId, "Operacao");
    }

    private async Task<Ctx> NovoContextoAsync()
    {
        var (empresa, loja) = await fixture.SeedEmpresaELojaAsync();
        var device = await fixture.SeedMobileDeviceAsync(empresa, loja);
        return new Ctx(device, fixture.CreateMobileClient(device.ApiKey));
    }

    private static async Task<LegacySnapshotResponse> ImportarAsync(Ctx c, LegacySnapshotRequest request)
    {
        var response = await c.Http.PostAsJsonAsync("/api/mobile/legacy-import", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LegacySnapshotResponse>())!;
    }

    /// <summary>Recusa de conflito: devolve a mensagem que a tela do APK mostra, com o codigo que ele ja trata.</summary>
    private static async Task<string> RecusadaAsync(Ctx c, LegacySnapshotRequest request)
    {
        var response = await c.Http.PostAsJsonAsync("/api/mobile/legacy-import", request);
        var corpo = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, corpo);
        var json = JsonDocument.Parse(corpo).RootElement;
        json.GetProperty("code").GetString().Should().Be(CodigoDeConflito);
        return json.GetProperty("error").GetString()!;
    }

    private async Task AssertNadaGravadoAsync(Guid empresa, int clientesDoErp, int produtosDoErp) => await LerAsync(empresa, async db =>
    {
        (await db.Set<Cliente>().IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresa)).Should().Be(clientesDoErp);
        (await db.Set<Produto>().IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresa)).Should().Be(produtosDoErp);
        (await db.Set<Product>().CountAsync(x => x.EmpresaId == empresa)).Should().Be(0, "recusa nao deixa cadastro parcial");
        (await db.Set<Client>().CountAsync(x => x.EmpresaId == empresa)).Should().Be(0);
        (await db.Set<Pedido>().CountAsync(x => x.EmpresaId == empresa)).Should().Be(0);
        (await db.Set<MobileProcessedMutation>().IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresa && x.Outcome == "accepted:legacy")).Should().Be(0);
        return 0;
    });

    private async Task<T> LerAsync<T>(Guid empresa, Func<EasyStockDbContext, Task<T>> acao)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(empresa);
        return await acao(db);
    }

    private Task<Cliente> SeedClienteAsync(Guid empresa, string nome, string? telefone, string? endereco) => LerAsync(empresa, async db =>
    {
        var cliente = EasyStock.Domain.Entities.Cliente.Criar(empresa, nome);
        cliente.Telefone = telefone; cliente.Endereco = endereco;
        db.Add(cliente);
        await db.SaveChangesAsync();
        return cliente;
    });

    private Task<Produto> SeedProdutoAsync(Guid empresa, Guid loja, string nome, int saldo = 0, bool comLote = false) => LerAsync(empresa, async db =>
    {
        var agora = DateTime.UtcNow;
        var categoria = await db.Set<Categoria>().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.EmpresaId == empresa);
        if (categoria is null)
        {
            categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresa, Nome = "Geral", Descricao = "Categoria de teste", CriadoEm = agora, AlteradoEm = agora };
            db.Add(categoria);
        }
        var produto = new Produto { Id = Guid.NewGuid(), EmpresaId = empresa, CategoriaId = categoria.Id, Nome = nome, Status = StatusProduto.Ativo,
            PrecoReferencia = Dinheiro.FromDecimal(55m), CustoReferencia = Dinheiro.FromDecimal(20m), CriadoEm = agora, AlteradoEm = agora };
        db.Add(produto);
        if (saldo > 0)
            db.Add(new ItemEstoque { Id = Guid.NewGuid(), EmpresaId = empresa, ProdutoId = produto.Id, LojaId = loja,
                QuantidadeInicial = Quantidade.From(saldo), QuantidadeAtual = Quantidade.From(saldo), CustoUnitario = Dinheiro.FromDecimal(20m),
                Status = StatusItemEstoque.Ok, EntradaEm = agora, CriadoEm = agora, AlteradoEm = agora });
        if (comLote)
        {
            var lote = Lote.Criar(empresa, "ERP-" + Guid.NewGuid().ToString("N")[..8], agora, loja);
            lote.Itens.Add(new LoteItem { Id = Guid.NewGuid(), LoteId = lote.Id, ProdutoId = produto.Id, Nome = nome, Unidade = "un", Quantidade = 5, CriadoEm = agora });
            db.Add(lote);
        }
        await db.SaveChangesAsync();
        return produto;
    });
}
