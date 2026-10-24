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
using NSubstitute;

namespace EasyStock.Api.UnitTests.Services;

/// <summary>
/// Alertas de estoque de uma empresa (#2016): a rodada é de UMA empresa (o escopo e o tenant são do chamador), o alerta
/// é um por (tipo, item) por dia de Brasília e o lote vencido parado ganha o status.
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

    // O sino em memória: AddAsync grava, GetAlertasDoPeriodoAsync lê. Duas rodadas seguidas enxergam a mesma coisa.
    private readonly INotificacaoRepository _sino = Substitute.For<INotificacaoRepository>();
    private readonly List<Notificacao> _gravadas = [];
    private (DateTime Inicio, DateTime Fim)? _janelaConsultada;

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

        _sino.GetAlertasDoPeriodoAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _janelaConsultada = (call.ArgAt<DateTime>(1), call.ArgAt<DateTime>(2));
                return _gravadas
                    .Where(n => n.CriadaEm >= call.ArgAt<DateTime>(1) && n.CriadaEm < call.ArgAt<DateTime>(2))
                    .Select(n => (n.TipoAlerta, n.ReferenciaId))
                    .ToHashSet();
            });
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
        Substitute.For<ILogger<GeradorNotificacoesAutomaticas>>());

    private void ProdutoCritico(Guid produtoId) =>
        // Fonte única: produto com vigente 1, mínimo 5, crítico 2 -> estado CRÍTICO.
        _analytics.GetSnapshotReposicaoAsync(_empresaId, _lojaId,
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProdutoReposicaoSnapshot>
            {
                new(produtoId, null, "Cafe", 1m, 5, 2, null, null, null, null, 0m, 0, 7, 1, null, null)
            });

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
    public async Task Nao_repete_o_alerta_na_rodada_seguinte_do_mesmo_dia()
    {
        ProdutoCritico(Guid.NewGuid());
        var gerador = CriarGerador();

        var primeira = await gerador.ExecutarAsync(_empresaId);
        _relogio.Advance(TimeSpan.FromHours(1));
        var segunda = await gerador.ExecutarAsync(_empresaId);

        primeira.Criados.Should().Be(2);
        segunda.Criados.Should().Be(0);
        _gravadas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Repete_no_dia_seguinte_enquanto_a_situacao_continuar()
    {
        ProdutoCritico(Guid.NewGuid());
        var gerador = CriarGerador();

        await gerador.ExecutarAsync(_empresaId);
        _relogio.Advance(TimeSpan.FromDays(1));
        var segunda = await gerador.ExecutarAsync(_empresaId);

        segunda.Criados.Should().Be(2);
    }

    [Fact]
    public async Task O_dia_do_alerta_e_o_dia_de_Brasilia_e_nao_o_dia_UTC()
    {
        // 22:00 em Brasília (01:00Z do dia 25): ainda é dia 24 para a loja; um corte por UTC repetiria o alerta aqui.
        ProdutoCritico(Guid.NewGuid());
        var gerador = CriarGerador();
        await gerador.ExecutarAsync(_empresaId);

        _relogio.SetUtcNow(new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero));
        var depoisDaMeiaNoiteUtc = await gerador.ExecutarAsync(_empresaId);

        depoisDaMeiaNoiteUtc.Criados.Should().Be(0, "para a loja ainda é o mesmo dia");
        _janelaConsultada.Should().Be((new DateTime(2026, 10, 24, 3, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 25, 3, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task Alerta_ja_criado_hoje_nao_e_recriado_mas_os_demais_sim()
    {
        var produtoId = Guid.NewGuid();
        ProdutoCritico(produtoId);
        _gravadas.Add(Notificacao.Criar(_empresaId, TipoAlertaEstoque.EstoqueCritico, "Estoque Crítico", "ja avisado", SeveridadeNotificacao.Alta, produtoId));
        _gravadas[0].CriadaEm = Agora.UtcDateTime.AddHours(-2);

        var resultado = await CriarGerador().ExecutarAsync(_empresaId);

        resultado.Criados.Should().Be(1);
        _gravadas.Where(n => n.TipoAlerta == TipoAlertaEstoque.EstoqueCritico).Should().HaveCount(1);
        _gravadas.Should().Contain(n => n.TipoAlerta == TipoAlertaEstoque.ReposicaoSugerida);
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
