using EasyStock.Application.Events.Storefront.Handlers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.CancelarPedido;
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

    public Guid EmpresaId { get; } = Guid.NewGuid();
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
    public List<CriarPreferenceCommand> Preferencias { get; } = new();

    public CobrancaPedidoFixture(string status = StatusPedidoMapper.AguardandoPagamento)
    {
        Pedido = Pedido.Criar(EmpresaId, origem: "whatsapp");
        Pedido.Status = status;
        Pedido.Itens.Add(Item("Brigadeiro", 2, 10m));
        Pedido.Itens.Add(Item("Frete SP Centro", 1, 5m));
        Pedido.RecalcularTotal();

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

        Uow.SetupExecuteInTransactionSemRetry<(ConfirmarPagamentoPedidoResult, PedidoPagoOperacao?)>();
        Uow.SetupExecuteInTransactionSemRetry<CobrancaPedidoResult>();
        Uow.SetupExecuteInTransactionSemRetry<(CobrancaPedidoResult, Guid?)>();
        Uow.SetupExecuteInTransactionSemRetry<DesfazerPagamentoManualResult>();
        Uow.SetupExecuteInTransactionSemRetry<(ResultadoExpiracaoCobranca, Guid?, string?)>();
        Uow.SetupExecuteInTransactionSemRetry<PedidoResult?>();
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
        new(PedidoRepo, StorefrontRepo, CobrancaRepo, MpClient, Uow, Relogio,
            NullLogger<GerarCobrancaPedidoUseCase>.Instance);

    public ConfirmarPagamentoPedidoUseCase Confirmar() =>
        new(CobrancaRepo, PedidoStorefrontRepo,
            new RegistrarPagamentoPedidoUseCase(PedidoRepo, Uow, NullLogger<RegistrarPagamentoPedidoUseCase>.Instance),
            Publicador, OperacaoEventos, Tenant, Uow, Relogio, NullLogger<ConfirmarPagamentoPedidoUseCase>.Instance);

    public TrocarFormaPagamentoPedidoUseCase Trocar() =>
        new(PedidoStorefrontRepo, CobrancaRepo, Gerar(), Aviso(), Publicador, Uow, Relogio);

    public DesfazerPagamentoManualUseCase Desfazer() =>
        new(PedidoStorefrontRepo, PedidoRepo, CobrancaRepo, Publicador, Uow, Relogio);

    public ProcessarCobrancaVencidaUseCase ProcessarVencida()
    {
        var estoque = new PedidoEstoqueIntegrationService(
            Substitute.For<IItemEstoqueRepository>(),
            Substitute.For<IMovimentacaoEstoqueRepository>(),
            Options.Create(new PedidoEstoqueOptions()),
            NullLogger<PedidoEstoqueIntegrationService>.Instance);
        var cancelar = new CancelarPedidoUseCase(PedidoRepo, estoque, Substitute.For<IContaReceberRepository>(), Uow,
            NullLogger<CancelarPedidoUseCase>.Instance);
        var liberarVaga = new LiberarVagaOnPedidoCanceladoHandler(VagaRepo, NullLogger<LiberarVagaOnPedidoCanceladoHandler>.Instance);
        return new ProcessarCobrancaVencidaUseCase(PedidoStorefrontRepo, CobrancaRepo, Gerar(), cancelar, liberarVaga,
            Aviso(), Tenant, Uow, Relogio, NullLogger<ProcessarCobrancaVencidaUseCase>.Instance);
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
