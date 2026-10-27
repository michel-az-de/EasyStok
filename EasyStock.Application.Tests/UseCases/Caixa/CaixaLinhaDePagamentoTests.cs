using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Caixa;

namespace EasyStock.Application.Tests.UseCases.Caixa;

/// <summary>
/// #2082 B16/C10: a linha "Pagamento de pedido" do <c>GET /api/caixa/dia</c> leva o número do pedido no dia, o cliente
/// e quem registrou. O console (#2078) lê exatamente <c>pedidoNumeroDoDia</c>, <c>clienteNome</c> e
/// <c>registradoPorNome</c>, opcionais; os campos que já existiam não mudam.
/// </summary>
public class CaixaLinhaDePagamentoTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly DateTime Ini = new(2026, 10, 26, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PagamentoDePedido_LevaNumeroDoDiaClienteEQuemRegistrou()
    {
        var repo = Substitute.For<ICaixaRepository>();
        var pedido = Pedido.Criar(EmpresaId);
        pedido.ClienteNome = "Maria";
        pedido.DefinirNumeroDoDia(new DateOnly(2026, 10, 26), 12);
        repo.GetPagamentosPedidosListaNoIntervaloAsync(EmpresaId, Ini, Ini.AddDays(1), null).Returns([
            new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Pedido = pedido, Valor = 59m, Metodo = "pix",
                PagoEm = Ini.AddHours(11), RegistradoPorNome = "Mercado Pago" },
            new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = Guid.NewGuid(), Valor = 10m, Metodo = "dinheiro", PagoEm = Ini.AddHours(12) },
        ]);

        var linhas = await new CaixaSaldoCalculator(repo).GetLinhasExtrasAsync(EmpresaId, Ini, Ini.AddDays(1));

        linhas.Select(l => (l.Valor, l.Descricao, l.Origem, l.PedidoNumeroDoDia, l.ClienteNome, l.RegistradoPorNome)).Should().Equal(
            (59m, "Pagamento de pedido", "Pedido", (int?)12, "Maria", "Mercado Pago"),
            (10m, "Pagamento de pedido", "Pedido", (int?)null, null, null));
    }

    [Fact]
    public void Json_TemOsTresCamposNovosEOsAntigosIntactos()
    {
        var linha = new CaixaLinhaExtraResult(Ini, "pagamento", 59m, "Pagamento de pedido", "pix", "Pedido", 12, "Maria", "Mercado Pago");

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(linha, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var raiz = json.RootElement;
        raiz.GetProperty("pedidoNumeroDoDia").GetInt32().Should().Be(12);
        raiz.GetProperty("clienteNome").GetString().Should().Be("Maria");
        raiz.GetProperty("registradoPorNome").GetString().Should().Be("Mercado Pago");
        raiz.EnumerateObject().Select(c => c.Name).Should().Contain(["hora", "tipo", "valor", "descricao", "metodo", "origem"]);
    }
}
