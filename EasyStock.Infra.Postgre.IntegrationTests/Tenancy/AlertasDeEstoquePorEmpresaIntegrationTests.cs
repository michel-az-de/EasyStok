using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Services;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using Castle.DynamicProxy;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// #2016 (antes #1575/#1588): os dois jobs de estoque rodam POR EMPRESA, cada uma num escopo com o tenant ligado, e fazem
/// o trabalho. Antes, fora de request o tenant era <c>Guid.Empty</c>, o filtro global do DbContext e a RLS zeravam toda
/// entidade com empresa e os jobs percorriam as empresas sem fazer nada. DI de produção, banco real; com
/// <c>rls</c>, o login é sujeito à RLS como o da aplicação (nenhum bypass).
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class AlertasDeEstoquePorEmpresaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private sealed record Cenario(
        Empresa Empresa, Loja Loja, Produto Critico, ItemEstoque AVencer, ItemEstoque Vencido, ItemEstoque VencidoZerado);

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Analisador_AvisaCadaEmpresaDoQueEDela_MarcaOVencido_ENaoRepeteNaRodadaSeguinte(bool rls)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var a = await SemearAsync("Alertas A");
        var b = await SemearAsync("Alertas B");
        await using var provider = BuildProvider(rls);
        var analisador = new AnalisadorEstoqueBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AnalisadorEstoqueBackgroundService>.Instance);

        (await analisador.RodadaAsync(CancellationToken.None)).Should().BeTrue("ninguém detém a trava");

        foreach (var (dona, outra) in new[] { (a, b), (b, a) })
        {
            var avisos = await NotificacoesAsync(dona.Empresa.Id);
            avisos.Should().OnlyContain(n => n.EmpresaId == dona.Empresa.Id && n.UsuarioId == null,
                "o aviso é da empresa e vai para toda a equipe, sem destinatário");
            avisos.Should().Contain(n => n.TipoAlerta == TipoAlertaEstoque.ValidadeProxima && n.ReferenciaId == dona.AVencer.Id);
            avisos.Should().Contain(n => n.TipoAlerta == TipoAlertaEstoque.ProdutoVencido && n.ReferenciaId == dona.Vencido.Id);
            avisos.Should().Contain(n => n.TipoAlerta == TipoAlertaEstoque.EstoqueCritico && n.ReferenciaId == dona.Critico.Id);
            avisos.Should().Contain(n => n.TipoAlerta == TipoAlertaEstoque.ReposicaoSugerida && n.ReferenciaId == dona.Critico.Id);
            avisos.Should().NotContain(n => n.ReferenciaId == dona.VencidoZerado.Id, "lote zerado não vai para a prateleira nem para o sino");
            avisos.Select(n => n.ReferenciaId).Should().NotIntersectWith(
                new Guid?[] { outra.AVencer.Id, outra.Vencido.Id, outra.Critico.Id }, "uma empresa não recebe o aviso da outra");

            (await StatusAsync(dona.Empresa.Id, dona.Vencido.Id)).Should().Be(StatusItemEstoque.Vencido,
                "o lote que venceu parado é marcado na rodada (estoque-08)");
            (await StatusAsync(dona.Empresa.Id, dona.VencidoZerado.Id)).Should().NotBe(StatusItemEstoque.Vencido, "sem saldo, ninguém mexe");
        }

        var totalNaPrimeira = (await NotificacoesAsync(a.Empresa.Id)).Count + (await NotificacoesAsync(b.Empresa.Id)).Count;
        totalNaPrimeira.Should().BeGreaterThanOrEqualTo(8);

        (await analisador.RodadaAsync(CancellationToken.None)).Should().BeTrue();

        ((await NotificacoesAsync(a.Empresa.Id)).Count + (await NotificacoesAsync(b.Empresa.Id)).Count)
            .Should().Be(totalNaPrimeira, "no mesmo dia o alerta de cada item sai uma vez só, rodada após rodada");
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecalcularVelocidades_GravaAVelocidadeDeCadaEmpresa_SoDoQueMudou(bool rls)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var agora = DateTime.UtcNow;
        var a = await SemearVelocidadeAsync("Velocidades A", saidas: 60, agora); // 2 por dia em 30 dias
        var b = await SemearVelocidadeAsync("Velocidades B", saidas: 30, agora); // 1 por dia
        await using var provider = BuildProvider(rls);
        var job = new RecalcularVelocidadesJob(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<RecalcularVelocidadesJob>.Instance);

        (await job.RodadaAsync(CancellationToken.None)).Should().BeTrue("ninguém detém a trava");

        (await LoteAsync(a.Empresa.Id, a.Lote.Id)).VelocidadeSaidaDiaria.Should().Be(2m);
        (await LoteAsync(b.Empresa.Id, b.Lote.Id)).VelocidadeSaidaDiaria.Should().Be(1m, "cada empresa recebe a velocidade dos próprios produtos");
        (await LoteAsync(a.Empresa.Id, a.Zerado.Id)).VelocidadeSaidaDiaria.Should().Be(0m, "lote sem saldo não é mexido");

        var alteradoEm = (await LoteAsync(a.Empresa.Id, a.Lote.Id)).AlteradoEm;
        (await job.RodadaAsync(CancellationToken.None)).Should().BeTrue();

        var depois = await LoteAsync(a.Empresa.Id, a.Lote.Id);
        depois.VelocidadeSaidaDiaria.Should().Be(2m);
        depois.AlteradoEm.Should().Be(alteradoEm, "velocidade igual não grava de novo");
    }

    /// <summary>
    /// #2016, deduplicação por condição: dois ciclos em dias seguidos com a mesma condição geram UM alerta (o sino não
    /// recebe o mesmo aviso todo dia). Resolvida a condição, o alerta sai; se ela voltar, nasce um novo, mesmo dentro dos 7
    /// dias da janela.
    /// </summary>
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Condicao_que_persiste_gera_um_alerta_em_dias_seguidos_e_resolvida_e_voltada_gera_outro(bool rls)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var c = await SemearAsync("Dedup por condição");
        var relogio = new RelogioDeTeste(DateTimeOffset.UtcNow);
        await using var provider = BuildProvider(rls, relogio);
        var analisador = new AnalisadorEstoqueBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AnalisadorEstoqueBackgroundService>.Instance);
        async Task<List<Notificacao>> Criticos() => (await NotificacoesAsync(c.Empresa.Id))
            .Where(n => n.TipoAlerta == TipoAlertaEstoque.EstoqueCritico && n.ReferenciaId == c.Critico.Id).ToList();

        await analisador.RodadaAsync(CancellationToken.None);
        var primeiro = (await Criticos()).Should().ContainSingle().Subject;

        relogio.Avancar(TimeSpan.FromDays(1));
        await analisador.RodadaAsync(CancellationToken.None);

        (await Criticos()).Should().ContainSingle().Which.Id.Should().Be(primeiro.Id, "mesma condição no dia seguinte: o mesmo alerta, não outro");
        (await NotificacoesAsync(c.Empresa.Id)).Count(n => n.TipoAlerta == TipoAlertaEstoque.ReposicaoSugerida && n.ReferenciaId == c.Critico.Id)
            .Should().Be(1);

        // Repuseram o produto: a condição acabou e o aviso sai do sino.
        await DefinirSaldoAsync(c.Empresa.Id, c.AVencer.Id, 100);
        relogio.Avancar(TimeSpan.FromDays(1));
        await analisador.RodadaAsync(CancellationToken.None);

        (await Criticos()).Should().BeEmpty("a condição foi resolvida");

        // Acabou de novo, dois dias depois do primeiro alerta (dentro da janela de 7 dias): alerta novo.
        await DefinirSaldoAsync(c.Empresa.Id, c.AVencer.Id, 1);
        relogio.Avancar(TimeSpan.FromDays(1));
        await analisador.RodadaAsync(CancellationToken.None);

        (await Criticos()).Should().ContainSingle().Which.Id.Should().NotBe(primeiro.Id, "condição nova, alerta novo");
    }

    /// <summary>
    /// #2016, retenção: na rodada da empresa saem os alertas automáticos de estoque lidos que passaram da janela de
    /// repetição e os de qualquer estado com mais de 30 dias. Não saem os que ainda seguram a repetição, os que têm
    /// destinatário nem os de outra empresa.
    /// </summary>
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retencao_expurga_so_o_alerta_automatico_vencido_da_empresa_da_rodada(bool rls)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var a = await SemearAsync("Retenção A");
        var b = await SemearAsync("Retenção B");
        var agora = DateTime.UtcNow;
        var naoLidoMuitoAntigo = await GravarAlertaAsync(a.Empresa.Id, agora.AddDays(-40), lida: false);
        var lidoForaDaJanela = await GravarAlertaAsync(a.Empresa.Id, agora.AddDays(-10), lida: true);
        var lidoRecente = await GravarAlertaAsync(a.Empresa.Id, agora.AddDays(-2), lida: true);
        var naoLidoComTempo = await GravarAlertaAsync(a.Empresa.Id, agora.AddDays(-10), lida: false);
        var comDestinatario = await GravarAlertaAsync(a.Empresa.Id, agora.AddDays(-60), lida: true, usuarioId: Guid.NewGuid());
        var deOutraEmpresa = await GravarAlertaAsync(b.Empresa.Id, agora.AddDays(-60), lida: true);
        await using var provider = BuildProvider(rls);

        // A rodada de UMA empresa, como o analisador a faz: escopo próprio com o tenant ligado.
        await using (var escopo = provider.CreateAsyncScope())
        {
            escopo.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(a.Empresa.Id);
            var resultado = await escopo.ServiceProvider.GetRequiredService<GeradorNotificacoesAutomaticas>().ExecutarAsync(a.Empresa.Id);
            resultado.Expurgados.Should().Be(2);
        }

        var restantesA = (await NotificacoesAsync(a.Empresa.Id)).Select(n => n.Id).ToList();
        restantesA.Should().NotContain([naoLidoMuitoAntigo, lidoForaDaJanela], "passaram da retenção");
        restantesA.Should().Contain([lidoRecente, naoLidoComTempo, comDestinatario],
            "ainda seguram a repetição ou não são alerta automático");
        (await NotificacoesAsync(b.Empresa.Id)).Select(n => n.Id).Should().Contain(deOutraEmpresa, "a rodada de A não toca em B");
    }

    /// <summary>
    /// #2031: a limpeza (retenção e condição resolvida) e a gravação dos alertas saem numa transação só. Se o commit cai (um
    /// lote mexido por outra operação no meio da rodada, token xmin), nada do que a rodada apagou fica apagado.
    /// </summary>
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Commit_que_cai_por_xmin_desfaz_tambem_a_limpeza_dos_alertas(bool rls)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var c = await SemearAsync("Atomicidade da rodada");
        var agora = DateTime.UtcNow;
        var resolvido = Notificacao.Criar(c.Empresa.Id, TipoAlertaEstoque.EstoqueCritico, "Estoque Crítico", "Produto reposto.",
            SeveridadeNotificacao.Alta, Guid.NewGuid()); // a condição não vale mais
        await GravarAsync(resolvido);
        var expurgavel = await GravarAlertaAsync(c.Empresa.Id, agora.AddDays(-10), lida: true); // lido, fora da janela de 7 dias

        // Antes do primeiro alerta novo ser gravado, outra operação mexe no lote que a rodada marcou como vencido.
        var conflito = new AntesDaChamada("AddAsync", 1, () => MexerNoLoteAsync(c.Empresa.Id, c.Vencido.Id));
        await using var provider = BuildProvider(rls, aoInterceptar: (s, tipo, alvo) =>
            tipo == typeof(INotificacaoRepository) ? Proxy((INotificacaoRepository)alvo, conflito) : alvo);

        await using (var escopo = provider.CreateAsyncScope())
        {
            escopo.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(c.Empresa.Id);
            var rodada = () => escopo.ServiceProvider.GetRequiredService<GeradorNotificacoesAutomaticas>().ExecutarAsync(c.Empresa.Id);

            await rodada.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        var depois = (await NotificacoesAsync(c.Empresa.Id)).Select(n => n.Id).ToList();
        depois.Should().Contain(resolvido.Id, "o commit caiu: o alerta resolvido não pode ter saído sozinho");
        depois.Should().Contain(expurgavel, "o commit caiu: a retenção também volta");
    }

    /// <summary>
    /// #2031: a paginação por <c>Skip</c> deixava um lote para trás quando outro saía da fila entre duas páginas, e o
    /// alerta dele era lido como "condição resolvida" e apagado. A leitura passa a ser por chave (Id): o que muda de
    /// lugar na fila não desloca os demais.
    /// </summary>
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lote_que_sai_da_fila_entre_as_paginas_nao_leva_o_alerta_de_outro_junto(bool rls)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Paginação estável", null);
        var loja = new Loja { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Loja", Ativa = true, CriadoEm = agora, AlteradoEm = agora };
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora };
        var produto = NovoProduto(empresa, categoria, "Lasanha", agora);
        // 105 lotes perto de vencer (2 páginas de 100), do que vence primeiro ao que vence por último.
        var lotes = Enumerable.Range(0, 105)
            .Select(i => NovoLote(empresa, loja, produto, saldo: 5, validade: agora.Date.AddDays(1 + i % 10), agora))
            .ToList();
        var avisos = lotes.ToDictionary(l => l.Id, l => Notificacao.Criar(empresa.Id, TipoAlertaEstoque.ValidadeProxima, "Validade Próxima",
            "Vence em breve.", SeveridadeNotificacao.Media, l.Id));
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresa.Id);
            seed.Empresas.Add(empresa);
            seed.Lojas.Add(loja);
            seed.Categorias.Add(categoria);
            seed.Produtos.Add(produto);
            seed.ItensEstoque.AddRange(lotes);
            seed.Notificacoes.AddRange(avisos.Values);
            await seed.SaveChangesAsync();
        }

        // Entre a primeira e a segunda página, o lote da frente da fila acaba (saldo zerado) e sai dela.
        var saiu = Guid.Empty;
        var saida = new AntesDaChamada("GetProximoVencimento", 2, async () =>
        {
            await using var db = fixture.CreateDbContext();
            db.SetMobileTenantContext(empresa.Id);
            saiu = (await db.ItensEstoque.OrderBy(i => (DateTime?)i.ValidadeEm).ThenBy(i => i.Id).FirstAsync()).Id;
            await DefinirSaldoAsync(empresa.Id, saiu, 0);
        });
        await using var provider = BuildProvider(rls, aoInterceptar: (s, tipo, alvo) =>
            tipo == typeof(IItemEstoqueRepository) ? Proxy((IItemEstoqueRepository)alvo, saida) : alvo);

        await using (var escopo = provider.CreateAsyncScope())
        {
            escopo.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(empresa.Id);
            await escopo.ServiceProvider.GetRequiredService<GeradorNotificacoesAutomaticas>().ExecutarAsync(empresa.Id);
        }

        saida.Disparou.Should().BeTrue("o teste só vale se a fila mudou entre as duas páginas");
        var restantes = (await NotificacoesAsync(empresa.Id)).Where(n => n.TipoAlerta == TipoAlertaEstoque.ValidadeProxima).ToList();
        // O lote que acabou pode já ter sido lido na primeira página; o aviso dele é o único que pode ou não sobrar. Os outros
        // 104 seguem com o mesmo id: nenhum foi apagado como "condição resolvida" nem recriado.
        restantes.Where(n => n.ReferenciaId != saiu).Select(n => n.Id).Should().BeEquivalentTo(
            avisos.Where(a => a.Key != saiu).Select(a => a.Value.Id),
            "os outros 104 avisos seguem, sem apagar e recriar");
    }

    private async Task GravarAsync(Notificacao alerta)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(alerta.EmpresaId);
        db.Notificacoes.Add(alerta);
        await db.SaveChangesAsync();
    }

    /// <summary>Mexe na linha do lote por outra conexão: o xmin muda e o commit de quem leu antes cai.</summary>
    private async Task MexerNoLoteAsync(Guid empresaId, Guid loteId)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        var lote = await db.ItensEstoque.SingleAsync(i => i.Id == loteId);
        lote.Observacoes = "mexido por outra operação";
        await db.SaveChangesAsync();
    }

    private static T Proxy<T>(T alvo, AntesDaChamada antes) where T : class =>
        new ProxyGenerator().CreateInterfaceProxyWithTarget(alvo, antes);

    /// <summary>Roda o efeito, uma vez, imediatamente antes da n-ésima chamada de um método (pelo prefixo do nome).</summary>
    private sealed class AntesDaChamada(string prefixoDoMetodo, int ocorrencia, Func<Task> efeito) : IInterceptor
    {
        private int _chamadas;
        public bool Disparou { get; private set; }

        public void Intercept(IInvocation invocation)
        {
            if (!Disparou && invocation.Method.Name.StartsWith(prefixoDoMetodo, StringComparison.Ordinal) && ++_chamadas == ocorrencia)
            {
                Disparou = true;
                efeito().GetAwaiter().GetResult();
            }

            invocation.Proceed();
        }
    }

    private sealed class RelogioDeTeste(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _agora = inicio;
        public override DateTimeOffset GetUtcNow() => _agora;
        public void Avancar(TimeSpan delta) => _agora += delta;
    }

    /// <summary>Alerta automático de pedido recebido (evento: a resolução não mexe nele), com a idade e o estado pedidos.</summary>
    private async Task<Guid> GravarAlertaAsync(Guid empresaId, DateTime criadaEm, bool lida, Guid? usuarioId = null)
    {
        var alerta = Notificacao.Criar(empresaId, TipoAlertaEstoque.PedidoRecebido, "Pedido Recebido", "Pedido confirmado.",
            SeveridadeNotificacao.Informativa, Guid.NewGuid());
        alerta.CriadaEm = criadaEm;
        alerta.UsuarioId = usuarioId;
        if (lida)
            alerta.MarcarComoLida();

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        db.Notificacoes.Add(alerta);
        await db.SaveChangesAsync();
        return alerta.Id;
    }

    private async Task DefinirSaldoAsync(Guid empresaId, Guid loteId, decimal saldo)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        var lote = await db.ItensEstoque.SingleAsync(i => i.Id == loteId);
        lote.QuantidadeAtual = Quantidade.From(saldo);
        await db.SaveChangesAsync();
    }

    private async Task<Cenario> SemearAsync(string nome)
    {
        var agora = DateTime.UtcNow;
        var hoje = agora.Date;
        var empresa = Empresa.Criar(nome, null);
        var loja = new Loja { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Loja", Ativa = true, CriadoEm = agora, AlteradoEm = agora };
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora };
        // Mínimo 5 e crítico 2: com 1 unidade na prateleira o produto está crítico.
        var critico = NovoProduto(empresa, categoria, "Lasanha", agora, minima: 5, critica: 2);
        var comum = NovoProduto(empresa, categoria, "Nhoque", agora);
        var aVencer = NovoLote(empresa, loja, critico, saldo: 1, validade: hoje.AddDays(2), agora);
        var vencido = NovoLote(empresa, loja, comum, saldo: 10, validade: hoje.AddDays(-3), agora);
        var vencidoZerado = NovoLote(empresa, loja, comum, saldo: 0, validade: hoje.AddDays(-30), agora);

        await using var seed = fixture.CreateDbContext();
        seed.SetMobileTenantContext(empresa.Id);
        seed.Empresas.Add(empresa);
        seed.Lojas.Add(loja);
        seed.Categorias.Add(categoria);
        seed.Produtos.AddRange(critico, comum);
        seed.ItensEstoque.AddRange(aVencer, vencido, vencidoZerado);
        await seed.SaveChangesAsync();
        return new Cenario(empresa, loja, critico, aVencer, vencido, vencidoZerado);
    }

    private sealed record CenarioVelocidade(Empresa Empresa, ItemEstoque Lote, ItemEstoque Zerado);

    private async Task<CenarioVelocidade> SemearVelocidadeAsync(string nome, int saidas, DateTime agora)
    {
        var empresa = Empresa.Criar(nome, null);
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora };
        var produto = NovoProduto(empresa, categoria, "Lasanha", agora);
        var lote = NovoLote(empresa, null, produto, saldo: 40, validade: null, agora);
        var zerado = NovoLote(empresa, null, produto, saldo: 0, validade: null, agora);
        var saida = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = empresa.Id, ItemEstoqueId = lote.Id, ProdutoId = produto.Id,
            Tipo = TipoMovimentacaoEstoque.Saida, Natureza = NaturezaMovimentacaoEstoque.Venda, Quantidade = Quantidade.From(saidas),
            DataMovimentacao = agora.AddDays(-5), CriadoEm = agora,
        };

        await using var seed = fixture.CreateDbContext();
        seed.SetMobileTenantContext(empresa.Id);
        seed.Empresas.Add(empresa);
        seed.Categorias.Add(categoria);
        seed.Produtos.Add(produto);
        seed.ItensEstoque.AddRange(lote, zerado);
        seed.MovimentacoesEstoque.Add(saida);
        await seed.SaveChangesAsync();
        return new CenarioVelocidade(empresa, lote, zerado);
    }

    private static Produto NovoProduto(Empresa empresa, Categoria categoria, string nome, DateTime agora, int? minima = null, int? critica = null) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresa.Id, CategoriaId = categoria.Id, Nome = nome,
        Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, QuantidadeMinima = minima, QuantidadeCritica = critica,
        CriadoEm = agora, AlteradoEm = agora,
    };

    private static ItemEstoque NovoLote(Empresa empresa, Loja? loja, Produto produto, decimal saldo, DateTime? validade, DateTime agora) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresa.Id, LojaId = loja?.Id, ProdutoId = produto.Id,
        QuantidadeInicial = Quantidade.From(100), QuantidadeAtual = Quantidade.From(saldo),
        CustoUnitario = Dinheiro.FromDecimal(10m), EntradaEm = agora.AddDays(-40),
        ValidadeEm = validade.HasValue ? Validade.From(DateTime.SpecifyKind(validade.Value, DateTimeKind.Utc)) : null,
        Status = StatusItemEstoque.Ok, CriadoEm = agora, AlteradoEm = agora,
    };

    private async Task<List<Notificacao>> NotificacoesAsync(Guid empresaId)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return await db.Notificacoes.AsNoTracking().Where(n => n.EmpresaId == empresaId).ToListAsync();
    }

    private async Task<ItemEstoque> LoteAsync(Guid empresaId, Guid loteId)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return await db.ItensEstoque.AsNoTracking().SingleAsync(i => i.Id == loteId);
    }

    private async Task<StatusItemEstoque> StatusAsync(Guid empresaId, Guid loteId) => (await LoteAsync(empresaId, loteId)).Status;

    /// <summary>DI de produção. Com <paramref name="rls"/>, o login do banco é sujeito a RLS, como o da aplicação.</summary>
    private ServiceProvider BuildProvider(
        bool rls, TimeProvider? relogio = null, Func<IServiceProvider, Type, object, object>? aoInterceptar = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>()); // sem JWT, como no job
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(rls ? fixture.RlsClientConnectionString : fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        if (relogio is not null)
            services.Replace(ServiceDescriptor.Singleton(relogio));
        if (aoInterceptar is not null)
        {
            // Envolve o repositório real num proxy que roda um efeito no meio da rodada (simula outra operação concorrente).
            services.Replace(ServiceDescriptor.Scoped<IItemEstoqueRepository>(sp =>
                (IItemEstoqueRepository)aoInterceptar(sp, typeof(IItemEstoqueRepository), ActivatorUtilities.CreateInstance<ItemEstoqueRepository>(sp))));
            services.Replace(ServiceDescriptor.Scoped<INotificacaoRepository>(sp =>
                (INotificacaoRepository)aoInterceptar(sp, typeof(INotificacaoRepository), ActivatorUtilities.CreateInstance<NotificacaoRepository>(sp))));
        }
        services.AddScoped<GeradorNotificacoesAutomaticas>();
        return services.BuildServiceProvider();
    }
}
