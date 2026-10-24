using EasyStock.Api.Configuration;
using EasyStock.Api.Services;
using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.Reposicao;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Reposicao;
using EasyStock.Domain.ValueObjects;
using EasyStock.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Services;

/// <summary>
/// Alertas de estoque de uma empresa (#2016): a rodada é de UMA empresa (o escopo e o tenant são do chamador), o alerta é
/// por condição (não por dia), a condição resolvida sai do sino, a retenção roda na própria rodada e o lote vencido parado
/// ganha o status.
/// </summary>
public class GeradorNotificacoesAutomaticasTests
{
    // 2026-10-24 12:00 em Brasília (15:00Z).
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 15, 0, 0, TimeSpan.Zero);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaId = Guid.NewGuid();
    private readonly ILojaRepository _lojas = Substitute.For<ILojaRepository>();
    private readonly IConfiguracaoLojaRepository _configuracoes = Substitute.For<IConfiguracaoLojaRepository>();
    private readonly IItemEstoqueRepository _estoque = Substitute.For<IItemEstoqueRepository>();
    private readonly IAnalyticsRepository _analytics = Substitute.For<IAnalyticsRepository>();
    private readonly IPedidoFornecedorRepository _pedidos = Substitute.For<IPedidoFornecedorRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _relogio = new(Agora);
    private readonly AlertasEstoqueOptions _opcoes = new() { DiasSemRepetir = 7, RetencaoDias = 30 };

    // O sino em memória: as rodadas seguintes enxergam o que as anteriores gravaram, apagaram e expurgaram.
    private readonly INotificacaoRepository _sino = Substitute.For<INotificacaoRepository>();
    private readonly List<Notificacao> _gravadas = [];

    public GeradorNotificacoesAutomaticasTests()
    {
        _lojas.GetByEmpresaAsync(_empresaId)
            .Returns(new[] { new Loja { Id = _lojaId, EmpresaId = _empresaId, Nome = "Loja", Ativa = true } });
        _configuracoes.GetOrDefaultAsync(_lojaId).Returns(ConfiguracaoLoja.CriarPadrao(_lojaId));
        _analytics.GetSnapshotReposicaoAsync(_empresaId, _lojaId,
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProdutoReposicaoSnapshot>());
        _estoque.GetProximoVencimentoAsync(_empresaId, Arg.Any<int>(), 1, 100, _lojaId, true).Returns((Array.Empty<ItemEstoque>(), 0));
        _estoque.GetItensParadosAsync(_empresaId, Arg.Any<int>(), 1, 100, _lojaId, true).Returns((Array.Empty<ItemEstoque>(), 0));
        _estoque.GetVencidosSemStatusAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ItemEstoque>());
        _pedidos.GetPedidosAtrasadosAsync(_empresaId, Arg.Any<DateTime>()).Returns(Array.Empty<PedidoFornecedor>());
        _pedidos.GetPedidosRecebidosNoPeriodoAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(Array.Empty<PedidoFornecedor>());

        _sino.GetAlertasAutomaticosAsync(_empresaId, Arg.Any<IReadOnlyCollection<TipoAlertaEstoque>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<AlertaAutomatico>)_gravadas
                .Where(n => call.ArgAt<IReadOnlyCollection<TipoAlertaEstoque>>(1).Contains(n.TipoAlerta)
                    && (!n.Lida || n.CriadaEm >= call.ArgAt<DateTime>(2)))
                .Select(n => new AlertaAutomatico(n.Id, n.TipoAlerta, n.ReferenciaId, n.Lida, n.CriadaEm))
                .ToList());
        _sino.RemoverAlertasAsync(_empresaId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => _gravadas.RemoveAll(n => call.ArgAt<IReadOnlyCollection<Guid>>(1).Contains(n.Id)));
        _sino.ExpurgarAlertasAutomaticosAsync(_empresaId, Arg.Any<IReadOnlyCollection<TipoAlertaEstoque>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => _gravadas.RemoveAll(n =>
                (n.Lida && n.CriadaEm < call.ArgAt<DateTime>(2)) || n.CriadaEm < call.ArgAt<DateTime>(3)));
        _sino.AddAsync(Arg.Do<Notificacao>(n => { n.CriadaEm = _relogio.GetUtcNow().UtcDateTime; _gravadas.Add(n); }))
            .Returns(Task.CompletedTask);
    }

    private GeradorNotificacoesAutomaticas CriarGerador() => new(
        _lojas,
        _configuracoes,
        _estoque,
        // ObterReposicaoUseCase é concreto (sem interface): monta-se um real com portas mockadas.
        new ObterReposicaoUseCase(_analytics, _configuracoes, Substitute.For<ILogger<ObterReposicaoUseCase>>()),
        _sino,
        _pedidos,
        _unitOfWork,
        _relogio,
        Options.Create(_opcoes),
        Substitute.For<ILogger<GeradorNotificacoesAutomaticas>>());

    private void ProdutoCritico(Guid produtoId) =>
        // Fonte única: produto com vigente 1, mínimo 5, crítico 2 -> estado CRÍTICO.
        _analytics.GetSnapshotReposicaoAsync(_empresaId, _lojaId,
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProdutoReposicaoSnapshot>
            {
                new(produtoId, null, "Cafe", 1m, 5, 2, null, null, null, null, 0m, 0, 7, 1, null, null)
            });

    private void SemProdutoCritico() =>
        _analytics.GetSnapshotReposicaoAsync(_empresaId, _lojaId,
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProdutoReposicaoSnapshot>());

    private ItemEstoque Lote(int diasParaVencer, decimal saldo = 10m, StatusItemEstoque status = StatusItemEstoque.Ok) => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = _empresaId,
        LojaId = _lojaId,
        ProdutoId = Guid.NewGuid(),
        QuantidadeInicial = Quantidade.From(10),
        QuantidadeAtual = Quantidade.From(saldo),
        ValidadeEm = Validade.From(HorarioBrasil.DataOperacional(Agora.UtcDateTime).AddDays(diasParaVencer).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
        EntradaEm = Agora.UtcDateTime.AddDays(-20),
        Status = status,
    };

    [Fact]
    public async Task Gera_estoque_critico_e_reposicao_por_produto_e_so_da_empresa_informada()
    {
        var produtoId = Guid.NewGuid();
        ProdutoCritico(produtoId);

        var resultado = await CriarGerador().ExecutarAsync(_empresaId);

        // EstoqueCritico referencia o PRODUTO (não o lote); o produto crítico também gera ReposicaoSugerida (mesma fonte).
        _gravadas.Should().Contain(n => n.EmpresaId == _empresaId && n.TipoAlerta == TipoAlertaEstoque.EstoqueCritico && n.ReferenciaId == produtoId);
        _gravadas.Should().Contain(n => n.TipoAlerta == TipoAlertaEstoque.ReposicaoSugerida && n.ReferenciaId == produtoId);
        _gravadas.Should().OnlyContain(n => n.EmpresaId == _empresaId && n.UsuarioId == null, "o alerta é da empresa, para toda a equipe");
        resultado.Criados.Should().Be(2);
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Condicao_que_continua_nao_gera_alerta_novo_nos_dias_seguintes()
    {
        ProdutoCritico(Guid.NewGuid());
        var gerador = CriarGerador();

        var primeira = await gerador.ExecutarAsync(_empresaId);
        _relogio.Advance(TimeSpan.FromHours(1));
        var mesmaTarde = await gerador.ExecutarAsync(_empresaId);
        _relogio.Advance(TimeSpan.FromDays(1));
        var diaSeguinte = await gerador.ExecutarAsync(_empresaId);
        _relogio.Advance(TimeSpan.FromDays(3));
        var tresDiasDepois = await gerador.ExecutarAsync(_empresaId);

        primeira.Criados.Should().Be(2);
        mesmaTarde.Criados.Should().Be(0);
        diaSeguinte.Criados.Should().Be(0, "a condição é a mesma: o sino não recebe o mesmo alerta todo dia");
        tresDiasDepois.Criados.Should().Be(0);
        _gravadas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Alerta_nao_lido_segura_a_repeticao_mesmo_depois_da_janela()
    {
        ProdutoCritico(Guid.NewGuid());
        var gerador = CriarGerador();
        await gerador.ExecutarAsync(_empresaId);

        _relogio.Advance(TimeSpan.FromDays(20)); // além dos 7 dias, aquém dos 30 da retenção
        var depois = await gerador.ExecutarAsync(_empresaId);

        depois.Criados.Should().Be(0, "ninguém leu; o alerta segue valendo");
        _gravadas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Alerta_lido_segura_a_repeticao_dentro_da_janela_e_volta_depois_dela()
    {
        ProdutoCritico(Guid.NewGuid());
        var gerador = CriarGerador();
        await gerador.ExecutarAsync(_empresaId);
        _gravadas.ForEach(n => n.MarcarComoLida());

        _relogio.Advance(TimeSpan.FromDays(3));
        var dentroDaJanela = await gerador.ExecutarAsync(_empresaId);
        _relogio.Advance(TimeSpan.FromDays(5)); // 8 dias depois do alerta lido
        var depoisDaJanela = await gerador.ExecutarAsync(_empresaId);

        dentroDaJanela.Criados.Should().Be(0, "lido há 3 dias, a condição é a mesma");
        depoisDaJanela.Criados.Should().Be(2, "passou a janela e a condição segue: lembra a equipe de novo");
        depoisDaJanela.Expurgados.Should().Be(2, "os lidos que passaram da janela saem na mesma rodada");
        _gravadas.Should().HaveCount(2).And.OnlyContain(n => !n.Lida);
    }

    [Fact]
    public async Task Condicao_resolvida_tira_o_alerta_do_sino_e_se_voltar_gera_um_novo()
    {
        var produtoId = Guid.NewGuid();
        ProdutoCritico(produtoId);
        var gerador = CriarGerador();
        await gerador.ExecutarAsync(_empresaId);
        var primeiroId = _gravadas.Single(n => n.TipoAlerta == TipoAlertaEstoque.EstoqueCritico).Id;

        SemProdutoCritico(); // repuseram o produto
        _relogio.Advance(TimeSpan.FromDays(1));
        var resolvida = await gerador.ExecutarAsync(_empresaId);

        resolvida.Resolvidos.Should().Be(2);
        resolvida.Criados.Should().Be(0);
        _gravadas.Should().BeEmpty("a condição acabou: o aviso não fica velho no sino");

        ProdutoCritico(produtoId); // acabou de novo, dentro da janela de 7 dias
        _relogio.Advance(TimeSpan.FromDays(1));
        var voltou = await gerador.ExecutarAsync(_empresaId);

        voltou.Criados.Should().Be(2, "condição nova, alerta novo");
        _gravadas.Single(n => n.TipoAlerta == TipoAlertaEstoque.EstoqueCritico).Id.Should().NotBe(primeiroId);
    }

    [Fact]
    public async Task Pedido_recebido_e_evento_nao_condicao_e_nao_some_quando_a_hora_passa()
    {
        var pedidoId = Guid.NewGuid();
        _pedidos.GetPedidosRecebidosNoPeriodoAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(new[]
        {
            new PedidoFornecedor { Id = pedidoId, EmpresaId = _empresaId, DataRecebimento = Agora.UtcDateTime, Status = StatusPedidoFornecedor.Recebido }
        });
        var gerador = CriarGerador();
        await gerador.ExecutarAsync(_empresaId);

        _pedidos.GetPedidosRecebidosNoPeriodoAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(Array.Empty<PedidoFornecedor>());
        _relogio.Advance(TimeSpan.FromHours(2));
        var depois = await gerador.ExecutarAsync(_empresaId);

        depois.Resolvidos.Should().Be(0);
        _gravadas.Should().ContainSingle(n => n.TipoAlerta == TipoAlertaEstoque.PedidoRecebido && n.ReferenciaId == pedidoId);
    }

    [Fact]
    public async Task Retencao_apaga_so_o_tipo_estoque_da_empresa_com_os_limites_configurados()
    {
        _opcoes.DiasSemRepetir = 5;
        _opcoes.RetencaoDias = 20;

        await CriarGerador().ExecutarAsync(_empresaId);

        await _sino.Received(1).ExpurgarAlertasAutomaticosAsync(
            _empresaId,
            Arg.Is<IReadOnlyCollection<TipoAlertaEstoque>>(t => t.SequenceEqual(GeradorNotificacoesAutomaticas.TiposAutomaticos)),
            Agora.UtcDateTime.AddDays(-5),
            Agora.UtcDateTime.AddDays(-20),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Le_os_alertas_vigentes_numa_consulta_so_por_empresa_e_nao_uma_por_item()
    {
        var lotes = Enumerable.Range(0, 50).Select(i => Lote(diasParaVencer: 1 + i % 5)).ToArray();
        _estoque.GetProximoVencimentoAsync(_empresaId, Arg.Any<int>(), 1, 100, _lojaId, true).Returns((lotes, lotes.Length));

        var resultado = await CriarGerador().ExecutarAsync(_empresaId);

        resultado.Criados.Should().Be(50);
        await _sino.Received(1).GetAlertasAutomaticosAsync(
            _empresaId, Arg.Any<IReadOnlyCollection<TipoAlertaEstoque>>(), Agora.UtcDateTime.AddDays(-7), Arg.Any<CancellationToken>());
        await _estoque.Received(1).GetProximoVencimentoAsync(_empresaId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), _lojaId, true);
    }

    [Fact]
    public async Task Gera_validade_proxima_e_produto_vencido_so_de_lote_com_saldo()
    {
        var aVencer = Lote(diasParaVencer: 2);
        var vencido = Lote(diasParaVencer: -3);
        _estoque.GetProximoVencimentoAsync(_empresaId, Arg.Any<int>(), 1, 100, _lojaId, true)
            .Returns((new[] { vencido, aVencer }, 2));

        await CriarGerador().ExecutarAsync(_empresaId);

        _gravadas.Should().Contain(n => n.TipoAlerta == TipoAlertaEstoque.ValidadeProxima && n.ReferenciaId == aVencer.Id && n.Severidade == SeveridadeNotificacao.Alta);
        _gravadas.Should().Contain(n => n.TipoAlerta == TipoAlertaEstoque.ProdutoVencido && n.ReferenciaId == vencido.Id && n.Severidade == SeveridadeNotificacao.Critica);
        // O filtro de saldo é do repositório: o gerador pede só lote com saldo (lote zerado não gera alerta).
        await _estoque.Received().GetProximoVencimentoAsync(_empresaId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), _lojaId, true);
        await _estoque.Received().GetItensParadosAsync(_empresaId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), _lojaId, true);
    }

    [Fact]
    public async Task Marca_como_vencido_o_lote_que_passou_da_validade_parado()
    {
        var vencidoParado = Lote(diasParaVencer: -1, status: StatusItemEstoque.Ok);
        var ainda = Lote(diasParaVencer: 5, status: StatusItemEstoque.Ok);
        _estoque.GetVencidosSemStatusAsync(_empresaId, HorarioBrasil.CivilComoInstanteUtc(HorarioBrasil.DataOperacional(Agora.UtcDateTime)), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { vencidoParado, ainda });

        var resultado = await CriarGerador().ExecutarAsync(_empresaId);

        vencidoParado.Status.Should().Be(StatusItemEstoque.Vencido);
        vencidoParado.AlteradoEm.Should().Be(Agora.UtcDateTime);
        ainda.Status.Should().NotBe(StatusItemEstoque.Vencido, "quem não venceu não é marcado");
        resultado.LotesMarcadosVencidos.Should().Be(1);
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Gera_alertas_de_pedido_atrasado_e_pedido_recebido()
    {
        var pedidoAtrasadoId = Guid.NewGuid();
        var pedidoRecebidoId = Guid.NewGuid();
        _pedidos.GetPedidosAtrasadosAsync(_empresaId, Arg.Any<DateTime>()).Returns(new[]
        {
            new PedidoFornecedor { Id = pedidoAtrasadoId, EmpresaId = _empresaId, PrevisaoEntrega = Agora.UtcDateTime.AddDays(-2), Status = StatusPedidoFornecedor.EmTransito }
        });
        _pedidos.GetPedidosRecebidosNoPeriodoAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(new[]
        {
            new PedidoFornecedor { Id = pedidoRecebidoId, EmpresaId = _empresaId, DataRecebimento = Agora.UtcDateTime, Status = StatusPedidoFornecedor.Recebido }
        });

        await CriarGerador().ExecutarAsync(_empresaId);

        _gravadas.Should().Contain(n => n.TipoAlerta == TipoAlertaEstoque.PedidoAtrasado && n.ReferenciaId == pedidoAtrasadoId);
        _gravadas.Should().Contain(n => n.TipoAlerta == TipoAlertaEstoque.PedidoRecebido && n.ReferenciaId == pedidoRecebidoId);
    }

    [Fact]
    public async Task Sem_empresa_nao_roda()
    {
        var act = () => CriarGerador().ExecutarAsync(Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }
}
