using EasyStock.Api.UnitTests.Helpers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Async.Pagamentos.Webhooks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Pagamentos;

/// <summary>Relógio fixo dos testes do webhook do Mercado Pago.</summary>
internal sealed class RelogioFixoMp(DateTime utc) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
}

/// <summary>
/// S32: pedido de R$ 25,00 em <c>AguardandoPagamento</c> com uma cobrança online pendente, repositórios em
/// memória e os use cases reais da S11 por trás do <see cref="MercadoPagoWebhookProcessor"/>. O
/// <see cref="IMercadoPagoClient"/> é falso: <see cref="Pagamentos"/> é o que <c>GET v1/payments/{id}</c> devolve.
/// </summary>
internal sealed class MercadoPagoWebhookFixture
{
    public const string PagamentoId = "20359978";
    public static readonly DateTime Agora = new(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);

    public Guid EmpresaId { get; } = Guid.NewGuid();
    public Pedido Pedido { get; }
    public CobrancaPedido Cobranca { get; }
    public List<CobrancaPedido> Cobrancas { get; } = new();
    public List<PedidoEvento> Eventos { get; } = new();
    public Dictionary<string, PagamentoMercadoPago> Pagamentos { get; } = new();

    public IMercadoPagoClient MpClient { get; } = Substitute.For<IMercadoPagoClient>();
    public IEstornoPedidoGateway Estorno { get; } = Substitute.For<IEstornoPedidoGateway>();
    public IPedidoRepository PedidoRepo { get; } = Substitute.For<IPedidoRepository>();
    public IPedidoStorefrontRepository PedidoStorefrontRepo { get; } = Substitute.For<IPedidoStorefrontRepository>();
    public ICobrancaPedidoRepository CobrancaRepo { get; } = Substitute.For<ICobrancaPedidoRepository>();
    public IPublicadorEventoIntegracao Publicador { get; } = Substitute.For<IPublicadorEventoIntegracao>();
    public IUnitOfWork Uow { get; } = Substitute.For<IUnitOfWork>();

    public MercadoPagoWebhookFixture()
    {
        Pedido = Pedido.Criar(EmpresaId, origem: "site");
        Pedido.Status = StatusPedidoMapper.AguardandoPagamento;
        var item = new PedidoItem { Id = Guid.NewGuid(), PedidoId = Pedido.Id, Nome = "Brigadeiro", Quantidade = 2, PrecoUnitario = 12.5m };
        item.RecalcularSubtotal();
        Pedido.Itens.Add(item);
        Pedido.RecalcularTotal();

        Cobranca = CobrancaPedido.CriarOnline(EmpresaId, Pedido.Id, Pedido.Total.Valor, "pref-1",
            "https://mp.test/pref-1", Agora.AddMinutes(30), 1, Agora.AddMinutes(-1));
        Cobrancas.Add(Cobranca);

        PedidoRepo.GetByIdWithDetailsAsync(EmpresaId, Pedido.Id).Returns(Pedido);
        PedidoRepo.When(r => r.AddPagamentoAsync(Arg.Any<PedidoPagamento>()))
            .Do(ci => Pedido.Pagamentos.Add(ci.Arg<PedidoPagamento>()));
        PedidoStorefrontRepo.GetForUpdateAsync(Pedido.Id, Arg.Any<CancellationToken>()).Returns(Pedido);
        PedidoStorefrontRepo.When(r => r.AddEventoAsync(Arg.Any<PedidoEvento>(), Arg.Any<CancellationToken>()))
            .Do(ci => Eventos.Add(ci.Arg<PedidoEvento>()));
        CobrancaRepo.ListarDoPedidoAsync(EmpresaId, Pedido.Id, Arg.Any<CancellationToken>()).Returns(_ => Cobrancas.ToList());
        CobrancaRepo.ObterEmpresaIdDoPedidoAsync(Pedido.Id, Arg.Any<CancellationToken>()).Returns(EmpresaId);

        MpClient.ConsultarPagamentoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Pagamentos.GetValueOrDefault(ci.Arg<string>()));

        Uow.SetupExecuteInTransactionSemRetry<(ConfirmarPagamentoPedidoResult, PedidoPagoOperacao?, ImpressaoPendenteOperacao?, Guid?)>();
        Uow.SetupExecuteInTransactionSemRetry<(SituacaoAtualizacaoCobranca, Guid?, string?)>();
        Uow.SetupExecuteInTransactionSemRetry<PedidoResult?>();
    }

    /// <summary>Pagamento na fonte com o <c>external_reference</c> do pedido.</summary>
    public void PagamentoNaFonte(string status, decimal valor, string id = PagamentoId, string? externalReference = null) =>
        Pagamentos[id] = new PagamentoMercadoPago(id, status, null, externalReference ?? Pedido.Id.ToString(), valor,
            status == PagamentoMercadoPago.Approved ? Agora : null, "visa", "credit_card");

    /// <summary>Notificação no formato do exemplo oficial de Webhooks (tópico <c>payment</c>).</summary>
    public static string Notificacao(string dataId = PagamentoId, string tipo = "payment", string action = "payment.updated") =>
        $$"""{"action":"{{action}}","api_version":"v1","data":{"id":"{{dataId}}"},"date_created":"2026-09-29T15:00:00Z","id":123456789,"live_mode":false,"type":"{{tipo}}","user_id":724484980}""";

    public MercadoPagoWebhookProcessor Processor()
    {
        var tenant = Substitute.For<ITenantContextAccessor>();
        var relogio = new RelogioFixoMp(Agora);
        var aviso = new AvisoCobrancaConversa(Substitute.For<IConversaRepository>(),
            new ResolvedorCanal(Array.Empty<ICanalMensageria>()), Uow, NullLogger<AvisoCobrancaConversa>.Instance);
        var confirmar = new ConfirmarPagamentoPedidoUseCase(CobrancaRepo, PedidoStorefrontRepo,
            new RegistrarPagamentoPedidoUseCase(PedidoRepo, Uow, NullLogger<RegistrarPagamentoPedidoUseCase>.Instance,
                new EasyStock.Application.Services.Pedidos.CalculadoraInicioPrevistoPedido(Substitute.For<EasyStock.Application.Ports.Output.Persistence.IPrazoPreparoPedidoQueries>())),
            Publicador, Substitute.For<IOperacaoEventPublisher>(), Substitute.For<IImpressaoPendenteRepository>(), tenant, Uow, relogio,
            NullLogger<ConfirmarPagamentoPedidoUseCase>.Instance,
            new CalculadoraInicioPrevistoPedido(Substitute.For<IPrazoPreparoPedidoQueries>()), Estorno, aviso, PedidoRepo);
        var atualizar = new AtualizarCobrancaPorPagamentoUseCase(CobrancaRepo, PedidoStorefrontRepo, aviso, tenant, Uow,
            relogio, NullLogger<AtualizarCobrancaPorPagamentoUseCase>.Instance);
        return new MercadoPagoWebhookProcessor(MpClient, confirmar, atualizar, NullLogger<MercadoPagoWebhookProcessor>.Instance);
    }
}
