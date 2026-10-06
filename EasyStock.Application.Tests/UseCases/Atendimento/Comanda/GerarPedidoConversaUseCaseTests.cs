using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.Tests.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Comanda;

/// <summary>
/// F03 do console: a operadora fecha a comanda da conversa; o pedido nasce pelo caminho da conversa (S10),
/// a cobrança pela S11 e o resumo com o link sai ao cliente pelo canal da conversa.
/// </summary>
public class GerarPedidoConversaUseCaseTests
{
    private const string WaId = "5511999998888";

    private sealed class Cenario
    {
        public CheckoutCoreServiceTests.Cenario Checkout { get; } = new();
        public Cliente Cliente { get; }
        public Conversa Conversa { get; }
        public IConversaRepository ConversaRepo { get; } = Substitute.For<IConversaRepository>();
        public IPedidoRepository Pedidos { get; } = Substitute.For<IPedidoRepository>();
        public ICanalMensageria Canal { get; } = Substitute.For<ICanalMensageria>();
        public IMercadoPagoClient Mp { get; } = Substitute.For<IMercadoPagoClient>();
        public List<Mensagem> Mensagens { get; } = new();
        public List<CobrancaPedido> Cobrancas { get; } = new();
        public IUnitOfWork Uow { get; } = Substitute.For<IUnitOfWork>();
        public GerarPedidoConversaUseCase UseCase { get; }
        public Guid EmpresaId => Checkout.Storefront.EmpresaId;

        public Cenario(bool comEndereco = true)
        {
            Cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = "Maria" };
            if (comEndereco)
                Cliente.Enderecos.Add(new ClienteEndereco { Id = Guid.NewGuid(), ClienteId = Cliente.Id, Cep = "01310-100", Padrao = true });
            var clienteRepo = Substitute.For<IClienteRepository>();
            clienteRepo.GetByIdWithDetailsAsync(EmpresaId, Cliente.Id).Returns(Cliente);

            var agora = DateTime.UtcNow;
            Conversa = Conversa.Abrir(EmpresaId, WaId, agora.AddMinutes(-2), "Maria", Cliente.Id);
            Conversa.RegistrarEntrada(agora.AddMinutes(-1));
            ConversaRepo.ObterPorIdAsync(EmpresaId, Conversa.Id, Arg.Any<CancellationToken>()).Returns(Conversa);
            ConversaRepo.When(r => r.AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>()))
                .Do(ci => Mensagens.Add(ci.Arg<Mensagem>()));

            Canal.Canal.Returns(CanalConversa.WhatsApp);
            Canal.EnviarTextoAsync(WaId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("wamid.resumo");

            var cobrancaRepo = Substitute.For<ICobrancaPedidoRepository>();
            cobrancaRepo.ListarDoPedidoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(_ => Cobrancas.ToList());
            cobrancaRepo.When(r => r.AddAsync(Arg.Any<CobrancaPedido>(), Arg.Any<CancellationToken>()))
                .Do(ci => Cobrancas.Add(ci.Arg<CobrancaPedido>()));

            Mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
                .Returns(new PreferenceCriadaResult("pref-1", "https://mp.test/pref-1"));

            // Troca de forma (na entrega) trava o pedido recém-criado numa transação.
            Uow.SetupExecuteInTransactionSemRetry<(CobrancaPedidoResult, Guid?, string?, PedidoMudouStatusOperacao?)>();
            Checkout.PedidoRepo.GetForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(ci => Checkout.PedidosAdicionados.FirstOrDefault(p => p.Id == ci.Arg<Guid>()));

            var aviso = new AvisoCobrancaConversa(ConversaRepo, new ResolvedorCanal([Canal]), Uow,
                NullLogger<AvisoCobrancaConversa>.Instance);
            var gerar = new GerarCobrancaPedidoUseCase(Pedidos, Checkout.StorefrontRepo, cobrancaRepo, Mp, Checkout.Servico(), Uow,
                TimeProvider.System, NullLogger<GerarCobrancaPedidoUseCase>.Instance);
            var trocar = new TrocarFormaPagamentoPedidoUseCase(Checkout.PedidoRepo, cobrancaRepo, gerar, aviso,
                Substitute.For<IPublicadorEventoIntegracao>(), Mp, Uow, TimeProvider.System,
                NullLogger<TrocarFormaPagamentoPedidoUseCase>.Instance, new EasyStock.Application.Services.Pedidos.CalculadoraInicioPrevistoPedido(Substitute.For<EasyStock.Application.Ports.Output.Persistence.IPrazoPreparoPedidoQueries>()), Substitute.For<IOperacaoEventPublisher>());

            // O núcleo lê o pedido em andamento do banco com a conversa travada (#1238).
            ConversaRepo.TravarParaPedidoAsync(EmpresaId, Conversa.Id, Arg.Any<CancellationToken>())
                .Returns(_ => Conversa.PedidoEmAndamentoId);

            UseCase = new GerarPedidoConversaUseCase(
                ConversaRepo, clienteRepo,
                Checkout.CriarPedidoAtendimento(ConversaRepo, clienteRepo, Uow, Pedidos),
                gerar, trocar, aviso, TimeProvider.System,
                NullLogger<GerarPedidoConversaUseCase>.Instance);
        }

        public GerarPedidoConversaInput Input(Guid? empresaId = null, string? forma = null) => new(
            EmpresaId: empresaId ?? EmpresaId,
            ConversaId: Conversa.Id,
            Itens: new List<ItemPedidoCheckout> { new(Checkout.CardapioItemId, 2, "sem granulado") },
            JanelaId: Checkout.JanelaId,
            DataEntrega: Checkout.DataEntrega,
            Forma: forma);
    }

    [Fact]
    public async Task CriaPedidoCobraEEnviaOLinkAoCliente()
    {
        var c = new Cenario();

        var resultado = await c.UseCase.ExecuteAsync(c.Input());

        c.Conversa.PedidoEmAndamentoId.Should().Be(resultado.PedidoId);
        resultado.Total.Should().Be(25m);
        resultado.Forma.Should().Be(TrocarFormaPagamentoPedidoUseCase.FormaOnline);
        resultado.Cobranca!.LinkPagamento.Should().Be("https://mp.test/pref-1");
        resultado.EnviadoAoCliente.Should().BeTrue();
        c.Cobrancas.Should().ContainSingle().Which.ConversaId.Should().Be(c.Conversa.Id);

        await c.Canal.Received(1).EnviarTextoAsync(WaId,
            Arg.Is<string>(t => t.Contains("2x Brigadeiro") && t.Contains("25,00") && t.Contains("https://mp.test/pref-1")),
            Arg.Any<CancellationToken>());
        var saida = c.Mensagens.Should().ContainSingle().Subject;
        saida.Direcao.Should().Be(DirecaoMensagem.Saida);
        saida.ExternoId.Should().Be("wamid.resumo");
    }

    [Fact]
    public async Task NaEntrega_EnviaResumoSemLink()
    {
        var c = new Cenario();

        var resultado = await c.UseCase.ExecuteAsync(c.Input(forma: "na_entrega"));

        resultado.Forma.Should().Be(TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega);
        resultado.Cobranca!.LinkPagamento.Should().BeNull();
        await c.Mp.DidNotReceiveWithAnyArgs().CriarPreferenceAsync(default!, default);
        await c.Canal.Received(1).EnviarTextoAsync(WaId, Arg.Is<string>(t => t.Contains("na entrega")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConversaDeOutraEmpresa_NaoEncontrada()
    {
        var c = new Cenario();

        var act = () => c.UseCase.ExecuteAsync(c.Input(empresaId: Guid.NewGuid()));

        await act.Should().ThrowAsync<ConversaNaoEncontradaException>();
        await c.Checkout.VagaRepo.DidNotReceiveWithAnyArgs().OcuparAsync(default, default, default, default);
    }

    [Fact]
    public async Task PedidoEmAndamentoAberto_RecusaSegundoPedido()
    {
        var c = new Cenario();
        var anterior = new Pedido { Id = Guid.NewGuid(), EmpresaId = c.EmpresaId, Status = StatusPedidoMapper.AguardandoPagamento };
        c.Conversa.DefinirPedidoEmAndamento(anterior.Id);
        c.Pedidos.GetByIdWithDetailsAsync(c.EmpresaId, anterior.Id).Returns(anterior);

        var act = () => c.UseCase.ExecuteAsync(c.Input());

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*pedido em andamento*");
        await c.Checkout.VagaRepo.DidNotReceiveWithAnyArgs().OcuparAsync(default, default, default, default);
    }

    [Fact]
    public async Task PedidoAnteriorEntregue_PermiteNovo()
    {
        var c = new Cenario();
        var anterior = new Pedido { Id = Guid.NewGuid(), EmpresaId = c.EmpresaId, Status = StatusPedidoMapper.Entregue };
        c.Conversa.DefinirPedidoEmAndamento(anterior.Id);
        c.Pedidos.GetByIdWithDetailsAsync(c.EmpresaId, anterior.Id).Returns(anterior);

        var resultado = await c.UseCase.ExecuteAsync(c.Input());

        resultado.PedidoId.Should().NotBe(anterior.Id);
    }

    [Fact]
    public async Task ClienteSemEndereco_RecusaComOrientacao()
    {
        var c = new Cenario(comEndereco: false);

        var act = () => c.UseCase.ExecuteAsync(c.Input());

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*endereço*");
    }

    [Fact]
    public async Task MercadoPagoFora_DesfazPedidoEVagaSemAvisarOCliente()
    {
        // #1301: antes o pedido ficava sem cobrança e a vaga presa, prometendo um link que nenhum job reemitia.
        var c = new Cenario();
        c.Mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("fora"));

        var act = () => c.UseCase.ExecuteAsync(c.Input());

        await act.Should().ThrowAsync<EasyStock.Domain.Exceptions.Storefront.MercadoPagoIndisponivelException>(
            "a operadora recebe 503 e tenta de novo");
        var pedido = c.Checkout.PedidosAdicionados.Should().ContainSingle().Subject;
        pedido.Status.Should().Be(StatusPedidoMapper.Cancelado);
        await c.Checkout.VagaRepo.Received(1).LiberarPorPedidoAsync(
            pedido.Id, Arg.Is<string>(m => m.Contains("mercado_pago_indisponivel")), Arg.Any<CancellationToken>());
        await c.Canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public async Task FormaInvalida_Recusa()
    {
        var c = new Cenario();

        var act = () => c.UseCase.ExecuteAsync(c.Input(forma: "fiado"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }
}
