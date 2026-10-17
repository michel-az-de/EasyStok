using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.Tests.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Comanda;

/// <summary>
/// #1884 ("Quanto fica?"): a atendente vê frete, total, sinal e restante antes de gerar a cobrança. A
/// simulação só lê: não cria pedido, não ocupa vaga e não cobra.
/// </summary>
public class SimularPedidoConversaUseCaseTests
{
    private const string WaId = "5511999998888";

    private sealed class Cenario
    {
        public CheckoutCoreServiceTests.Cenario Checkout { get; } = new();
        public Cliente Cliente { get; }
        public Conversa Conversa { get; }
        public IConversaRepository ConversaRepo { get; } = Substitute.For<IConversaRepository>();
        public SimularPedidoConversaUseCase UseCase { get; }
        public Guid EmpresaId => Checkout.Storefront.EmpresaId;

        public Cenario(string? cep = "01310-100", bool comEndereco = true)
        {
            Cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = "Maria" };
            if (comEndereco)
                Cliente.Enderecos.Add(new ClienteEndereco { Id = Guid.NewGuid(), ClienteId = Cliente.Id, Cep = cep, Numero = "10", Padrao = true });
            var clienteRepo = Substitute.For<IClienteRepository>();
            clienteRepo.GetByIdWithDetailsAsync(EmpresaId, Cliente.Id).Returns(Cliente);

            Conversa = Conversa.Abrir(EmpresaId, WaId, DateTime.UtcNow.AddMinutes(-2), "Maria", Cliente.Id);
            ConversaRepo.ObterPorIdAsync(EmpresaId, Conversa.Id, Arg.Any<CancellationToken>()).Returns(Conversa);

            UseCase = new SimularPedidoConversaUseCase(ConversaRepo, clienteRepo, Checkout.ConfiguracaoAtendimentoRepo, Checkout.Servico(), Checkout.Relogio);
        }

        /// <summary>Dois itens de R$ 10,00 na janela das 9h do dia 02/06 (terça-feira).</summary>
        public SimularPedidoConversaInput Input(int quantidade = 2) => new(
            EmpresaId, Conversa.Id,
            [new ItemPedidoCheckout(Checkout.CardapioItemId, quantidade)],
            Checkout.JanelaId, Checkout.DataEntrega);

        /// <summary>Nenhuma escrita: nem pedido, nem item, nem vaga.</summary>
        public async Task ConferirSemEfeitoColateralAsync()
        {
            Checkout.PedidosAdicionados.Should().BeEmpty();
            Checkout.ItensAdicionados.Should().BeEmpty();
            await Checkout.VagaRepo.DidNotReceiveWithAnyArgs().OcuparAsync(default, default, default, default);
            await Checkout.PedidoRepo.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        }
    }

    [Fact]
    public async Task EncomendaDeOutroDia_SinalEMetadeDoTotalComFrete()
    {
        // Véspera, 12:00 em Brasília: 2 x R$ 10,00 + frete da zona R$ 5,00 = R$ 25,00; sinal R$ 12,50.
        var c = new Cenario();

        var r = await c.UseCase.ExecuteAsync(c.Input());

        r.Itens.Should().Be(20m);
        r.Frete.Should().Be(5m);
        r.FreteACombinar.Should().BeFalse();
        r.Total.Should().Be(25m);
        r.Encomenda.Should().BeTrue();
        r.Sinal.Should().Be(12.50m);
        r.Restante.Should().Be(12.50m);
        await c.ConferirSemEfeitoColateralAsync();
    }

    [Fact]
    public async Task SinalArredondaParaCimaComoOServidorCobra()
    {
        // 3 x R$ 10,00 + R$ 5,00 = R$ 35,00; metade R$ 17,50. Com 1 item: R$ 15,00 e sinal R$ 7,50.
        var c = new Cenario();

        (await c.UseCase.ExecuteAsync(c.Input(quantidade: 3))).Sinal.Should().Be(17.50m);
        var par = await c.UseCase.ExecuteAsync(c.Input(quantidade: 1));
        par.Total.Should().Be(15m);
        par.Sinal.Should().Be(7.50m);
        par.Restante.Should().Be(7.50m);
        await c.ConferirSemEfeitoColateralAsync();
    }

    [Fact]
    public async Task MesmoDia_NaoTemSinal()
    {
        // 06:00 em Brasília do próprio dia 02/06 (a janela é às 9h e o prazo é de 100 min): total à vista, sem sinal.
        var c = new Cenario();
        c.Checkout.Relogio.Advance(new DateTimeOffset(2026, 6, 2, 9, 0, 0, TimeSpan.Zero) - c.Checkout.Relogio.GetUtcNow());
        var useCase = new SimularPedidoConversaUseCase(c.ConversaRepo, ClientesDe(c), c.Checkout.ConfiguracaoAtendimentoRepo, c.Checkout.Servico(), c.Checkout.Relogio);

        var r = await useCase.ExecuteAsync(c.Input());

        r.Total.Should().Be(25m);
        r.Encomenda.Should().BeFalse();
        r.Sinal.Should().BeNull();
        r.Restante.Should().BeNull();
        await c.ConferirSemEfeitoColateralAsync();
    }

    [Fact]
    public async Task CepForaDaArea_FreteACombinar_SemTotalNemSinal()
    {
        // Fora de qualquer zona o frete é combinado com a equipe: os itens aparecem, o resto não é inventado.
        var c = new Cenario(cep: "99999-999");

        var r = await c.UseCase.ExecuteAsync(c.Input());

        r.Itens.Should().Be(20m);
        r.FreteACombinar.Should().BeTrue();
        r.Frete.Should().BeNull();
        r.Total.Should().BeNull();
        r.Sinal.Should().BeNull();
        r.Restante.Should().BeNull();
        await c.ConferirSemEfeitoColateralAsync();
    }

    [Fact]
    public async Task SemEndereco_PedeOCadastroComAMesmaMensagemDoPedido()
    {
        var c = new Cenario(comEndereco: false);

        var act = () => c.UseCase.ExecuteAsync(c.Input());

        (await act.Should().ThrowAsync<RegraDeDominioVioladaException>()).WithMessage("*endereço de entrega*");
        await c.ConferirSemEfeitoColateralAsync();
    }

    [Fact]
    public async Task ItemIndisponivel_RecusaComOMotivoDoPedido()
    {
        var c = new Cenario();
        c.Checkout.CardapioItem.MarcarEsgotado();

        var act = () => c.UseCase.ExecuteAsync(c.Input());

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
        await c.ConferirSemEfeitoColateralAsync();
    }

    [Fact]
    public async Task JanelaQueNaoAtendeODia_Recusa()
    {
        var c = new Cenario();
        var input = c.Input() with { DataEntrega = c.Checkout.DataEntrega.AddDays(1) };

        var act = () => c.UseCase.ExecuteAsync(input);

        (await act.Should().ThrowAsync<RegraDeDominioVioladaException>()).WithMessage("*não atende o dia*");
        await c.ConferirSemEfeitoColateralAsync();
    }

    private static IClienteRepository ClientesDe(Cenario c)
    {
        var repo = Substitute.For<IClienteRepository>();
        repo.GetByIdWithDetailsAsync(c.EmpresaId, c.Cliente.Id).Returns(c.Cliente);
        return repo;
    }
}
