using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Application.UseCases.Operacao.Atraso;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>Relógio fixo para os testes da cobrança.</summary>
internal sealed class RelogioFixo(DateTime utc) : TimeProvider
{
    public DateTime Agora { get; set; } = utc;
    public override DateTimeOffset GetUtcNow() => new(Agora, TimeSpan.Zero);
}

/// <summary>
/// Fakes compartilhados pelos testes da S11: um pedido com dois itens e frete, a lista de cobranças
/// do pedido em memória e os use cases montados sobre eles.
/// </summary>
internal sealed class CobrancaPedidoFixture
{
    public static readonly DateTime Agora = new(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);

    public Guid EmpresaId { get; }
    public Pedido Pedido { get; }
    public List<CobrancaPedido> Cobrancas { get; } = new();
    public List<PedidoEvento> Eventos { get; } = new();
    public RelogioFixo Relogio { get; } = new(Agora);

    public IPedidoRepository PedidoRepo { get; } = Substitute.For<IPedidoRepository>();
    public IPedidoStorefrontRepository PedidoStorefrontRepo { get; } = Substitute.For<IPedidoStorefrontRepository>();
    public ICobrancaPedidoRepository CobrancaRepo { get; } = Substitute.For<ICobrancaPedidoRepository>();
    public IStorefrontRepository StorefrontRepo { get; } = Substitute.For<IStorefrontRepository>();
    public IMercadoPagoClient MpClient { get; } = Substitute.For<IMercadoPagoClient>();
    public IPublicadorEventoIntegracao Publicador { get; } = Substitute.For<IPublicadorEventoIntegracao>();
    public IOperacaoEventPublisher OperacaoEventos { get; } = Substitute.For<IOperacaoEventPublisher>();
    public ITenantContextAccessor Tenant { get; } = Substitute.For<ITenantContextAccessor>();
    public IConversaRepository ConversaRepo { get; } = Substitute.For<IConversaRepository>();
    public IVagaOcupadaRepository VagaRepo { get; } = Substitute.For<IVagaOcupadaRepository>();
    public IUnitOfWork Uow { get; } = Substitute.For<IUnitOfWork>();
    public INotificadorService Notificador { get; } = Substitute.For<INotificadorService>();
    public EasyStock.Application.Services.Notifications.PrazosOptions Prazos { get; } = new();
    public IImpressaoPendenteRepository ImpressaoRepo { get; } = Substitute.For<IImpressaoPendenteRepository>();
    public IPrazoPreparoPedidoQueries PrazoQueries { get; } = Substitute.For<IPrazoPreparoPedidoQueries>();
    public List<CriarPreferenceCommand> Preferencias { get; } = new();

    /// <summary>Estorno do pagamento que chega depois do cancelamento; aceita por padrão.</summary>
    public IEstornoPedidoGateway Estorno { get; } = Substitute.For<IEstornoPedidoGateway>();

    /// <summary>O que <c>payments/search?external_reference=</c> devolve (S32).</summary>
    public List<PagamentoMercadoPago> PagamentosNoMercadoPago { get; } = new();

    /// <param name="pedido">Pedido já criado por outro use case (caminho completo); sem ele, um pedido de R$ 25.</param>
    public CobrancaPedidoFixture(string status = StatusPedidoMapper.AguardandoPagamento, Pedido? pedido = null)
    {
        if (pedido is not null)
        {
            EmpresaId = pedido.EmpresaId;
            Pedido = pedido;
        }
        else
        {
            EmpresaId = Guid.NewGuid();
            Pedido = Pedido.Criar(EmpresaId, origem: "whatsapp");
            Pedido.Status = status;
            Pedido.Itens.Add(Item("Brigadeiro", 2, 10m));
            Pedido.Itens.Add(Item("Frete SP Centro", 1, 5m));
            Pedido.RecalcularTotal();
        }

        PedidoRepo.GetByIdWithDetailsAsync(EmpresaId, Pedido.Id).Returns(Pedido);
        PedidoRepo.When(r => r.AddPagamentoAsync(Arg.Any<PedidoPagamento>()))
            .Do(ci => Pedido.Pagamentos.Add(ci.Arg<PedidoPagamento>()));
        PedidoRepo.When(r => r.AddEventoAsync(Arg.Any<PedidoEvento>()))
            .Do(ci => Eventos.Add(ci.Arg<PedidoEvento>()));
        PedidoStorefrontRepo.GetForUpdateAsync(Pedido.Id, Arg.Any<CancellationToken>()).Returns(Pedido);
        PedidoStorefrontRepo.When(r => r.AddEventoAsync(Arg.Any<PedidoEvento>(), Arg.Any<CancellationToken>()))
            .Do(ci => Eventos.Add(ci.Arg<PedidoEvento>()));

        CobrancaRepo.ListarDoPedidoAsync(EmpresaId, Pedido.Id, Arg.Any<CancellationToken>())
            .Returns(_ => Cobrancas.ToList());
        CobrancaRepo.When(r => r.AddAsync(Arg.Any<CobrancaPedido>(), Arg.Any<CancellationToken>()))
            .Do(ci => Cobrancas.Add(ci.Arg<CobrancaPedido>()));
        CobrancaRepo.ObterEmpresaIdDoPedidoAsync(Pedido.Id, Arg.Any<CancellationToken>()).Returns(EmpresaId);

        var storefront = StorefrontEntity.Criar(EmpresaId, "casa-da-baba", "Casa da Babá", 0m);
        StorefrontRepo.GetByEmpresaAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(storefront);

        var sequencia = 0;
        MpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                Preferencias.Add(ci.Arg<CriarPreferenceCommand>());
                sequencia++;
                return new PreferenceCriadaResult($"pref-{sequencia}", $"https://mp.test/pref-{sequencia}");
            });

        MpClient.BuscarPagamentosPorReferenciaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => PagamentosNoMercadoPago.ToList());

        Estorno.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(EstornoPedidoResult.Ok("estorno-1"));

        Uow.SetupExecuteInTransactionSemRetry<(ConfirmarPagamentoPedidoResult, PedidoPagoOperacao?, ImpressaoPendenteOperacao?, Guid?)>();
        Uow.SetupExecuteInTransactionSemRetry<(SituacaoAtualizacaoCobranca, Guid?, string?)>();
        Uow.SetupExecuteInTransactionSemRetry<CobrancaPedidoResult>();
        Uow.SetupExecuteInTransactionSemRetry<(CobrancaPedidoResult, Guid?, string?, PedidoMudouStatusOperacao?, ImpressaoPendenteOperacao?)>();
        Uow.SetupExecuteInTransactionSemRetry<DesfazerPagamentoManualResult>();
        Uow.SetupExecuteInTransactionSemRetry<(ResultadoExpiracaoCobranca, Guid?, string?)>();
        Uow.SetupExecuteInTransactionSemRetry<PedidoResult?>();
        Uow.SetupExecuteInTransactionSemRetry<PedidoAtrasadoOperacao?>();
    }

    private PedidoItem Item(string nome, decimal qtd, decimal preco)
    {
        var item = new PedidoItem
        {
            Id = Guid.NewGuid(),
            PedidoId = Pedido.Id,
            Nome = nome,
            Quantidade = qtd,
            PrecoUnitario = preco,
        };
        item.RecalcularSubtotal();
        return item;
    }

    public CobrancaPedido AdicionarOnline(int tentativa = 1, DateTime? expiraEm = null, Guid? conversaId = null, string referencia = "pref-antiga")
    {
        var c = CobrancaPedido.CriarOnline(EmpresaId, Pedido.Id, Pedido.Total.Valor, referencia,
            $"https://mp.test/{referencia}", expiraEm ?? Agora.AddMinutes(30), tentativa, Agora.AddMinutes(-1), conversaId);
        Cobrancas.Add(c);
        return c;
    }

    public CobrancaPedido AdicionarNaEntrega()
    {
        var c = CobrancaPedido.CriarNaEntrega(EmpresaId, Pedido.Id, Pedido.Total.Valor, Agora.AddMinutes(-1));
        Cobrancas.Add(c);
        return c;
    }

    public AvisoCobrancaConversa Aviso() =>
        new(ConversaRepo, new ResolvedorCanal(Array.Empty<ICanalMensageria>()), Uow,
            NullLogger<AvisoCobrancaConversa>.Instance);

    public GerarCobrancaPedidoUseCase Gerar() =>
        new(PedidoRepo, StorefrontRepo, CobrancaRepo, MpClient, CheckoutCore(), Uow, Relogio,
            NullLogger<GerarCobrancaPedidoUseCase>.Instance);

    /// <summary>Núcleo do checkout sobre a vaga e o pedido da fixture: só a reserva desfeita (#1301) passa por ele aqui.</summary>
    public CheckoutCoreService CheckoutCore() =>
        new(StorefrontRepo, Substitute.For<ICardapioItemRepository>(), Substitute.For<IJanelaEntregaRepository>(),
            Substitute.For<IBloqueioEntregaRepository>(),
            new CalcularFreteUseCase(StorefrontRepo, Substitute.For<IFreteZonaRepository>(), Substitute.For<ICepLookupClient>(),
                Substitute.For<IGeocodingClient>(), Substitute.For<IRotaClient>(), NullLogger<CalcularFreteUseCase>.Instance),
            VagaRepo, PedidoStorefrontRepo, Substitute.For<IExpedienteLojaRepository>(),
            NullLogger<CheckoutCoreService>.Instance, Relogio);

    private QuitacaoPedido Quitacao() => QuitacaoPedidoTeste.Criar(PedidoRepo, CobrancaRepo, ConversaRepo, ImpressaoRepo, Publicador, OperacaoEventos);

    public ConfirmarPagamentoPedidoUseCase Confirmar() =>
        new(CobrancaRepo, PedidoStorefrontRepo,
            new RegistrarPagamentoPedidoUseCase(PedidoRepo, Uow, NullLogger<RegistrarPagamentoPedidoUseCase>.Instance,
                new CalculadoraInicioPrevistoPedido(PrazoQueries), Quitacao()),
            Publicador, OperacaoEventos, Quitacao(), Tenant, Uow, Relogio, NullLogger<ConfirmarPagamentoPedidoUseCase>.Instance,
            new CalculadoraInicioPrevistoPedido(PrazoQueries), Estorno, Aviso(), PedidoRepo);

    public NotificarAtrasoPedidoUseCase NotificarAtraso() =>
        new(PedidoStorefrontRepo, OperacaoEventos, Tenant, Uow, Notificador, Options.Create(Prazos), Relogio,
            NullLogger<NotificarAtrasoPedidoUseCase>.Instance);

    public TrocarFormaPagamentoPedidoUseCase Trocar() =>
        new(PedidoStorefrontRepo, CobrancaRepo, Gerar(), Aviso(), Publicador, MpClient, Uow, Relogio,
            NullLogger<TrocarFormaPagamentoPedidoUseCase>.Instance, new CalculadoraInicioPrevistoPedido(PrazoQueries), OperacaoEventos, Quitacao());

    public AtualizarCobrancaPorPagamentoUseCase AtualizarPorPagamento() =>
        new(CobrancaRepo, PedidoStorefrontRepo, Aviso(), Tenant, Uow, Relogio,
            NullLogger<AtualizarCobrancaPorPagamentoUseCase>.Instance);

    public DesfazerPagamentoManualUseCase Desfazer() =>
        new(PedidoStorefrontRepo, PedidoRepo, CobrancaRepo, Publicador, Uow, Relogio, Quitacao());

    public ProcessarCobrancaVencidaUseCase ProcessarVencida()
    {
        var estoque = new PedidoEstoqueIntegrationService(
            Substitute.For<IItemEstoqueRepository>(),
            Substitute.For<IMovimentacaoEstoqueRepository>(),
            Substitute.For<EasyStock.Application.Ports.Output.Integration.IPublicadorEventoIntegracao>(),
            Options.Create(new PedidoEstoqueOptions()),
            NullLogger<PedidoEstoqueIntegrationService>.Instance);
        Uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<EasyStock.Application.UseCases.Pedidos.PedidoResult?>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<EasyStock.Application.UseCases.Pedidos.PedidoResult?>>>()(ci.Arg<CancellationToken>()));
        var cancelar = new CancelarPedidoUseCase(PedidoRepo, estoque,
            new EasyStock.Application.Services.Pedidos.EfeitosCancelamentoPedido(Substitute.For<IContaReceberRepository>(), VagaRepo, NullLogger<EasyStock.Application.Services.Pedidos.EfeitosCancelamentoPedido>.Instance), Uow,
            NullLogger<CancelarPedidoUseCase>.Instance, CobrancaRepo, Substitute.For<EasyStock.Application.Ports.Output.Integration.IPublicadorEventoIntegracao>(), Substitute.For<EasyStock.Application.Ports.Output.Atendimento.IOperacaoEventPublisher>());
        return new ProcessarCobrancaVencidaUseCase(PedidoStorefrontRepo, CobrancaRepo, Gerar(), cancelar,
            Aviso(), MpClient, Confirmar(), Tenant, Uow, Relogio, NullLogger<ProcessarCobrancaVencidaUseCase>.Instance, OperacaoEventos);
    }

    public void AdicionarPagamento(string? referencia, decimal valor = 25m)
    {
        Pedido.Pagamentos.Add(new PedidoPagamento
        {
            Id = Guid.NewGuid(),
            PedidoId = Pedido.Id,
            Metodo = "pix",
            Valor = valor,
            Referencia = referencia,
            PagoEm = Agora.AddMinutes(-5),
        });
    }

    public static Dinheiro Reais(decimal v) => Dinheiro.FromDecimal(v);
}
