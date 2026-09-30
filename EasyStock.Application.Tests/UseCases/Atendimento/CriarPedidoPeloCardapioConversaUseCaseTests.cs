using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.Tests.Services.Atendimento;
using EasyStock.Application.Tests.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

/// <summary>
/// S48: o cliente marca o pedido no cardápio do site pelo link da conversa; o pedido nasce pelo
/// caminho da conversa (S10), fica vinculado a ela com o resumo e segue para a cobrança (S11).
/// </summary>
public class CriarPedidoPeloCardapioConversaUseCaseTests
{
    private static readonly DateTime Agora = DateTime.UtcNow;

    private sealed class Cenario
    {
        public CheckoutCoreServiceTests.Cenario Checkout { get; } = new();
        public LinkCardapioConversaServiceTests.RepositorioEmMemoria Links { get; } = new();
        public LinkCardapioConversaService LinkService { get; }
        public Cliente Cliente { get; }
        public ClienteEndereco Endereco { get; }
        public Conversa Conversa { get; }
        public IConversaRepository ConversaRepo { get; } = Substitute.For<IConversaRepository>();
        public ITenantContextAccessor Tenant { get; } = Substitute.For<ITenantContextAccessor>();
        public List<Mensagem> Mensagens { get; } = new();
        public List<CobrancaPedido> Cobrancas { get; } = new();
        public IUnitOfWork Uow { get; } = Substitute.For<IUnitOfWork>();
        public CriarPedidoPeloCardapioConversaUseCase UseCase { get; }
        public Guid EmpresaId => Checkout.Storefront.EmpresaId;

        public Cenario()
        {
            Cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = "Maria" };
            Endereco = new ClienteEndereco { Id = Guid.NewGuid(), ClienteId = Cliente.Id, Cep = "01310-100", Padrao = true };
            Cliente.Enderecos.Add(Endereco);
            var clienteRepo = Substitute.For<IClienteRepository>();
            clienteRepo.GetByIdWithDetailsAsync(EmpresaId, Cliente.Id).Returns(Cliente);

            Conversa = Conversa.Abrir(EmpresaId, "5511999998888", Agora, "Maria", Cliente.Id);
            ConversaRepo.ObterPorIdAsync(EmpresaId, Conversa.Id, Arg.Any<CancellationToken>()).Returns(Conversa);
            ConversaRepo.When(r => r.AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>()))
                .Do(ci => Mensagens.Add(ci.Arg<Mensagem>()));

            var cobrancaRepo = Substitute.For<ICobrancaPedidoRepository>();
            cobrancaRepo.ListarDoPedidoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(_ => Cobrancas.ToList());
            cobrancaRepo.When(r => r.AddAsync(Arg.Any<CobrancaPedido>(), Arg.Any<CancellationToken>()))
                .Do(ci => Cobrancas.Add(ci.Arg<CobrancaPedido>()));

            var mp = Substitute.For<IMercadoPagoClient>();
            mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
                .Returns(new PreferenceCriadaResult("pref-1", "https://mp.test/pref-1"));

            var gerar = new GerarCobrancaPedidoUseCase(Substitute.For<IPedidoRepository>(), Checkout.StorefrontRepo,
                cobrancaRepo, mp, Uow, TimeProvider.System, NullLogger<GerarCobrancaPedidoUseCase>.Instance);
            var trocar = new TrocarFormaPagamentoPedidoUseCase(Checkout.PedidoRepo, cobrancaRepo, gerar,
                new AvisoCobrancaConversa(ConversaRepo, new ResolvedorCanal([]), Uow, NullLogger<AvisoCobrancaConversa>.Instance),
                Substitute.For<IPublicadorEventoIntegracao>(), mp, Uow, TimeProvider.System,
                NullLogger<TrocarFormaPagamentoPedidoUseCase>.Instance);

            LinkService = new LinkCardapioConversaService(Links,
                new SaudacaoAtendimento(Checkout.StorefrontRepo, new ConfigurationBuilder().Build()));

            UseCase = new CriarPedidoPeloCardapioConversaUseCase(
                LinkService, Tenant, ConversaRepo, clienteRepo,
                new CriarPedidoAtendimentoUseCase(Checkout.Servico(), ConversaRepo, clienteRepo, Checkout.ConfiguracaoAtendimentoRepo, Uow, Checkout.Atribuicao()),
                gerar, trocar, Uow, TimeProvider.System,
                NullLogger<CriarPedidoPeloCardapioConversaUseCase>.Instance);
        }

        public async Task<string> GerarTokenAsync() =>
            (await LinkService.GerarAsync(EmpresaId, Conversa.Id, Agora)).Token;

        public CriarPedidoPeloCardapioConversaInput Input(string token, int quantidade = 2) => new(
            Token: token,
            Itens: new List<ItemPedidoCheckout> { new(Checkout.CardapioItemId, quantidade, "sem granulado") },
            JanelaId: Checkout.JanelaId,
            DataEntrega: Checkout.DataEntrega);
    }

    [Fact]
    public async Task VinculaAConversa()
    {
        var c = new Cenario();
        var token = await c.GerarTokenAsync();

        var resultado = await c.UseCase.ExecuteAsync(c.Input(token));

        c.Tenant.Received().SetCurrentTenant(c.EmpresaId);
        c.Conversa.PedidoEmAndamentoId.Should().Be(resultado.PedidoId);

        var resumo = c.Mensagens.Should().ContainSingle().Subject;
        resumo.ConversaId.Should().Be(c.Conversa.Id);
        resumo.Autor.Should().Be(AutorMensagem.Sistema);
        resumo.Direcao.Should().Be(DirecaoMensagem.Saida);
        resumo.ExternoId.Should().BeNull("é nota para a operadora e para o agente, não sai para o cliente");
        resumo.Texto.Should().Contain("cardápio").And.Contain("2x Brigadeiro").And.Contain("25,00");

        resultado.Total.Should().Be(25m);
        resultado.Forma.Should().Be(TrocarFormaPagamentoPedidoUseCase.FormaOnline);
        resultado.LinkPagamento.Should().Be("https://mp.test/pref-1");
        c.Cobrancas.Should().ContainSingle().Which.ConversaId.Should().Be(c.Conversa.Id);
        c.Links.Links.Single().UsadoEm.Should().NotBeNull();
    }

    [Fact]
    public async Task TokenJaUsado_RecusaSemCriarPedido()
    {
        var c = new Cenario();
        var token = await c.GerarTokenAsync();
        await c.UseCase.ExecuteAsync(c.Input(token));
        c.Checkout.VagaRepo.ClearReceivedCalls();

        var act = () => c.UseCase.ExecuteAsync(c.Input(token));

        await act.Should().ThrowAsync<LinkCardapioConversaIndisponivelException>();
        await c.Checkout.VagaRepo.DidNotReceiveWithAnyArgs().OcuparAsync(default, default, default, default);
    }

    [Fact]
    public async Task ConversaEncerrada_Recusa()
    {
        var c = new Cenario();
        var token = await c.GerarTokenAsync();
        c.Conversa.Encerrar(Agora);

        var act = () => c.UseCase.ExecuteAsync(c.Input(token));

        await act.Should().ThrowAsync<LinkCardapioConversaIndisponivelException>();
        c.Links.Links.Single().UsadoEm.Should().BeNull();
    }

    [Fact]
    public async Task CarrinhoRecusado_DevolveOLinkParaCorrigir()
    {
        var c = new Cenario();
        var token = await c.GerarTokenAsync();

        var act = () => c.UseCase.ExecuteAsync(c.Input(token) with { JanelaId = Guid.NewGuid() });

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
        c.Links.Links.Single().UsadoEm.Should().BeNull();
        c.Mensagens.Should().BeEmpty();
    }

    [Fact]
    public async Task FormaInvalida_Recusa()
    {
        var c = new Cenario();
        var token = await c.GerarTokenAsync();

        var act = () => c.UseCase.ExecuteAsync(c.Input(token) with { Forma = "boleto" });

        await act.Should().ThrowAsync<UseCaseValidationException>();
        c.Links.Links.Single().UsadoEm.Should().BeNull();
    }
}
