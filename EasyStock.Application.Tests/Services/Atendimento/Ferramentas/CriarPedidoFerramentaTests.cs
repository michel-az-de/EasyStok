using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.Tests.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>criar_pedido</c> (S06 ligada na S11): núcleo do checkout (S10) + cobrança (S11) e devolve resumo,
/// total e link para o agente responder.
/// </summary>
public class CriarPedidoFerramentaTests
{
    private static readonly DateTime Agora = new(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc);

    private sealed class Cenario
    {
        public CheckoutCoreServiceTests.Cenario Checkout { get; } = new();
        public Cliente Cliente { get; }
        public Conversa Conversa { get; }
        public List<CobrancaPedido> Cobrancas { get; } = new();
        public CriarPedidoFerramenta Ferramenta { get; }

        public Cenario()
        {
            var empresaId = Checkout.Storefront.EmpresaId;
            Cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Maria" };
            Cliente.Enderecos.Add(new ClienteEndereco { Id = Guid.NewGuid(), ClienteId = Cliente.Id, Cep = "01310-100", Padrao = true });
            var clienteRepo = Substitute.For<IClienteRepository>();
            clienteRepo.GetByIdWithDetailsAsync(empresaId, Cliente.Id).Returns(Cliente);

            Conversa = Conversa.Abrir(empresaId, "5511999998888", Agora, "Maria", Cliente.Id);
            var conversaRepo = Substitute.For<IConversaRepository>();
            conversaRepo.ObterPorIdAsync(empresaId, Conversa.Id, Arg.Any<CancellationToken>()).Returns(Conversa);

            var cobrancaRepo = Substitute.For<ICobrancaPedidoRepository>();
            cobrancaRepo.ListarDoPedidoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(_ => Cobrancas.ToList());
            cobrancaRepo.When(r => r.AddAsync(Arg.Any<CobrancaPedido>(), Arg.Any<CancellationToken>()))
                .Do(ci => Cobrancas.Add(ci.Arg<CobrancaPedido>()));

            var mp = Substitute.For<IMercadoPagoClient>();
            mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
                .Returns(new PreferenceCriadaResult("pref-1", "https://mp.test/pref-1"));

            var uow = Substitute.For<IUnitOfWork>();
            var gerar = new GerarCobrancaPedidoUseCase(Substitute.For<IPedidoRepository>(), Checkout.StorefrontRepo,
                cobrancaRepo, mp, uow, TimeProvider.System, NullLogger<GerarCobrancaPedidoUseCase>.Instance);
            Ferramenta = new CriarPedidoFerramenta(
                new CriarPedidoAtendimentoUseCase(Checkout.Servico(), conversaRepo, clienteRepo, Checkout.ConfiguracaoAtendimentoRepo, uow, Checkout.Atribuicao()),
                gerar, clienteRepo, Checkout.StorefrontRepo, Checkout.JanelaRepo);
        }

        public Task<string> ExecutarAsync(object entrada) =>
            Ferramenta.ExecutarAsync(
                new ContextoTurnoAgente(Checkout.Storefront.EmpresaId, Conversa, Agora),
                JsonSerializer.SerializeToElement(entrada));
    }

    [Fact]
    public async Task CriaPedidoEDevolveResumoTotalELink()
    {
        var c = new Cenario();

        var resultado = await c.ExecutarAsync(new
        {
            itens = new[] { new { cardapio_item_id = c.Checkout.CardapioItemId, quantidade = 2, observacao = "sem granulado" } },
            data_entrega = c.Checkout.DataEntrega.ToString("yyyy-MM-dd"),
            janela_id = c.Checkout.JanelaId,
        });

        using var json = JsonDocument.Parse(resultado);
        json.RootElement.TryGetProperty("erro", out _).Should().BeFalse(resultado);
        json.RootElement.GetProperty("linkPagamento").GetString().Should().Be("https://mp.test/pref-1");
        json.RootElement.GetProperty("total").GetString().Should().NotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("itens")[0].GetProperty("observacao").GetString().Should().Be("sem granulado");
        c.Conversa.PedidoEmAndamentoId.Should().NotBeNull();
        c.Cobrancas.Should().ContainSingle().Which.ConversaId.Should().Be(c.Conversa.Id);
    }

    [Fact]
    public async Task SemClienteIdentificado_DevolveErro()
    {
        var c = new Cenario();
        var anonima = Conversa.Abrir(c.Checkout.Storefront.EmpresaId, "5511911112222", Agora);

        var resultado = await c.Ferramenta.ExecutarAsync(
            new ContextoTurnoAgente(c.Checkout.Storefront.EmpresaId, anonima, Agora),
            JsonSerializer.SerializeToElement(new { itens = Array.Empty<object>(), data_entrega = "2026-06-02" }));

        resultado.Should().Contain("cliente_nao_identificado");
    }

    [Fact]
    public async Task ClienteBloqueado_RecusaComCodigoSemCriarPedido()
    {
        var c = new Cenario();
        c.Cliente.Bloquear("golpe", Agora);

        var resultado = await c.ExecutarAsync(new
        {
            itens = new[] { new { cardapio_item_id = c.Checkout.CardapioItemId, quantidade = 1 } },
            data_entrega = c.Checkout.DataEntrega.ToString("yyyy-MM-dd"),
            janela_id = c.Checkout.JanelaId,
        });

        using var json = JsonDocument.Parse(resultado);
        json.RootElement.GetProperty("erro").GetString().Should().Be("cliente_bloqueado");
        resultado.Should().NotContain("golpe", "o motivo do bloqueio é interno");
        c.Conversa.PedidoEmAndamentoId.Should().BeNull();
        c.Cobrancas.Should().BeEmpty();
    }
}
