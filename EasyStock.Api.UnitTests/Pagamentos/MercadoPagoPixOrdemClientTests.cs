using System.Net;
using System.Text.Json;
using EasyStock.Infra.Integrations.Pagamentos.MercadoPago;
using EasyStock.TestHelpers.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.UnitTests.Pagamentos;

public class MercadoPagoPixOrdemClientTests
{
    private const string OrdemId = "ORDTST000000000000000000000001";
    private static readonly Guid PedidoId = Guid.NewGuid();

    private static (MercadoPagoClient Client, HttpFalso Http) Criar(string? e2e, object[] resultados,
        string? ordemRetornada = null)
    {
        var http = new HttpFalso();
        http.Quando(HttpMethod.Get, $"/v1/orders/{OrdemId}").Responde(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { id = ordemRetornada ?? OrdemId, external_reference = PedidoId,
                transactions = new { payments = new[] { new { payment_method = new { id = "pix", e2e_id = e2e } } } } }));
        http.Quando(HttpMethod.Get, "/v1/payments/search").Responde(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { results = resultados }));
        return (new MercadoPagoClient(http.CriarClient(), Options.Create(new MercadoPagoOptions { AccessToken = "fake" }),
            NullLogger<MercadoPagoClient>.Instance), http);
    }

    private static object Pix(long id, string? e2e, string status = "approved", Guid? pedido = null) => new {
        id, status, external_reference = pedido ?? PedidoId, transaction_amount = 25m,
        payment_method_id = "pix", payment_type_id = "bank_transfer",
        transaction_details = new { transaction_id = e2e }
    };

    [Fact]
    public async Task SelecionaPixExatoMesmoComSinalAntigoAprovado()
    {
        var (client, http) = Criar("E2E-B", [Pix(1, "E2E-A"), Pix(2, "E2E-B", "pending")]);
        var pagamentos = await client.BuscarPagamentosPixDaOrdemAsync(OrdemId);
        pagamentos.Should().ContainSingle().Which.Id.Should().Be("2");
        pagamentos[0].TransactionId.Should().Be("E2E-B");
        pagamentos[0].Status.Should().Be("pending");
        http.Chamadas.Should().HaveCount(2);
        http.UltimaChamada!.Uri.Query.Should().Contain($"external_reference={PedidoId}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("E2E-A")]
    [InlineData("e2e-b")]
    public async Task NaoUsaOutroPagamentoNemComparaE2ENulo(string? e2e)
    {
        var (client, _) = Criar("E2E-B", [Pix(1, e2e)]);
        (await client.BuscarPagamentosPixDaOrdemAsync(OrdemId)).Should().BeEmpty();
    }

    [Fact]
    public async Task OrdemSemE2ENaoPesquisaSinaisHistoricos()
    {
        var (client, http) = Criar(null, [Pix(1, null)]);
        (await client.BuscarPagamentosPixDaOrdemAsync(OrdemId)).Should().BeEmpty();
        http.Chamadas.Should().ContainSingle();
    }

    [Fact]
    public async Task E2EDuplicadoOuDeOutroPedidoNaoPodeSerUsado()
    {
        var (client, _) = Criar("E2E-B", [Pix(1, "E2E-B"), Pix(2, "E2E-B")]);
        (await client.BuscarPagamentosPixDaOrdemAsync(OrdemId)).Should().BeEmpty();
        (client, _) = Criar("E2E-B", [Pix(1, "E2E-B", pedido: Guid.NewGuid())]);
        (await client.BuscarPagamentosPixDaOrdemAsync(OrdemId)).Should().BeEmpty();
    }

    [Fact]
    public async Task RejeitaOrdemDiferenteNaReconsulta()
    {
        var (client, http) = Criar("E2E-B", [], "ORDOUTRA");
        var acao = () => client.BuscarPagamentosPixDaOrdemAsync(OrdemId);
        await acao.Should().ThrowAsync<InvalidOperationException>();
        http.Chamadas.Should().ContainSingle();
    }

    [Theory]
    [InlineData("../users/me")]
    [InlineData("ORD123?x=1")]
    [InlineData("")]
    public async Task FormatoInvalidoNaoConsulta(string id)
    {
        var (client, http) = Criar("E2E-B", []);
        var acao = () => client.BuscarPagamentosPixDaOrdemAsync(id);
        await acao.Should().ThrowAsync<ArgumentException>();
        http.Chamadas.Should().BeEmpty();
    }
}
