using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Integrations;
using EasyStock.Infra.Integrations.Entregas;
using EasyStock.TestHelpers.Http;

namespace EasyStock.Infra.Integrations.UnitTests.Entregas;

public sealed class ContratosEntregasTests
{
    private static readonly DateTime Agora = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("uber_direct")]
    [InlineData("lalamove")]
    public async Task Cotacao_contratacao_simuladas_sao_idempotentes_e_sem_rastreio_falso(string provedor)
    {
        using var sut = new SimuladorFreteProvider(provedor, new Relogio(Agora));
        var pedido = Solicitacao();
        var cotacao = await sut.CotarAsync(pedido, default);
        Assert.Null(cotacao.Indisponibilidade);
        Assert.True(cotacao.Simulado);
        Assert.True(cotacao.ValorCentavos > 0);
        var resultados = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => sut.ContratarAsync(pedido, cotacao.Referencia, default)));
        Assert.Single(resultados.Select(r => r.Referencia).Distinct());
        Assert.All(resultados, r => { Assert.True(r.Simulado); Assert.Null(r.RastreioUrl); });
    }

    [Theory]
    [InlineData("uber_direct")]
    [InlineData("lalamove")]
    public async Task Cotacao_nao_pode_ser_contratada_por_outra_empresa(string provedor)
    {
        using var sut = new SimuladorFreteProvider(provedor, new Relogio(Agora));
        var pedido = Solicitacao();
        var cotacao = await sut.CotarAsync(pedido, default);
        var ex = await Assert.ThrowsAsync<FreteProviderException>(() => sut.ContratarAsync(pedido with { EmpresaId = Guid.NewGuid() }, cotacao.Referencia, default));
        Assert.Contains("nova cotação", ex.Message);
    }

    [Theory]
    [InlineData("uber_direct", 16)]
    [InlineData("lalamove", 6)]
    public async Task Cotacao_vencida_exige_nova_cotacao(string provedor, int minutos)
    {
        var relogio = new Relogio(Agora);
        using var sut = new SimuladorFreteProvider(provedor, relogio);
        var pedido = Solicitacao();
        var cotacao = await sut.CotarAsync(pedido, default);
        relogio.Avancar(minutos);
        var ex = await Assert.ThrowsAsync<FreteProviderException>(() => sut.ContratarAsync(pedido, cotacao.Referencia, default));
        Assert.Contains("vencida", ex.Message);
    }

    [Fact]
    public async Task Reinicio_do_simulador_exige_recotar_em_vez_de_recriar_silenciosamente()
    {
        using var original = new SimuladorFreteProvider("lalamove", new Relogio(Agora));
        using var reiniciado = new SimuladorFreteProvider("lalamove", new Relogio(Agora));
        var pedido = Solicitacao();
        var cotacao = await original.CotarAsync(pedido, default);
        var ex = await Assert.ThrowsAsync<FreteProviderException>(() => reiniciado.ContratarAsync(pedido, cotacao.Referencia, default));
        Assert.Contains("reiniciada", ex.Message);
    }

    [Fact]
    public async Task Endereco_alterado_invalida_cotacao_e_chave_nao_aceita_outro_pedido()
    {
        using var sut = new SimuladorFreteProvider("lalamove", new Relogio(Agora));
        var pedido = Solicitacao();
        var cotacao = await sut.CotarAsync(pedido, default);
        var alterado = pedido with { Origem = pedido.Origem with { Endereco = "Outro endereço" } };
        await Assert.ThrowsAsync<FreteProviderException>(() => sut.ContratarAsync(alterado, cotacao.Referencia, default));
        await sut.ContratarAsync(pedido, cotacao.Referencia, default);
        await Assert.ThrowsAsync<FreteProviderException>(() => sut.ContratarAsync(alterado, cotacao.Referencia, default));
    }

    [Fact]
    public async Task Uber_recusa_multiplos_destinos_e_lalamove_aceita_agrupamento()
    {
        using var uber = new SimuladorFreteProvider("uber_direct", new Relogio(Agora));
        using var lala = new SimuladorFreteProvider("lalamove", new Relogio(Agora));
        var pedido = Solicitacao(3);
        Assert.NotNull((await uber.CotarAsync(pedido, default)).Indisponibilidade);
        var cotacao = await lala.CotarAsync(pedido, default);
        Assert.Null(cotacao.Indisponibilidade);
        Assert.Equal(2400, cotacao.ValorCentavos);
        Assert.True((await lala.ContratarAsync(pedido, cotacao.Referencia, default)).Simulado);
    }

    [Fact]
    public async Task Lalamove_recusa_excesso_de_paradas_e_janelas_incompativeis()
    {
        using var sut = new SimuladorFreteProvider("lalamove", new Relogio(Agora));
        Assert.NotNull((await sut.CotarAsync(Solicitacao(16), default)).Indisponibilidade);
        var pedido = Solicitacao(2);
        var pedidos = pedido.Pedidos.ToArray();
        pedidos[1] = pedidos[1] with { JanelaInicioUtc = Agora.AddHours(5), JanelaFimUtc = Agora.AddHours(6) };
        Assert.NotNull((await sut.CotarAsync(pedido with { Pedidos = pedidos }, default)).Indisponibilidade);
    }

    [Fact]
    public async Task Endereco_vazio_coordenadas_invalidas_e_janela_nao_utc_sao_rejeitados()
    {
        using var sut = new SimuladorFreteProvider("lalamove", new Relogio(Agora));
        var pedido = Solicitacao();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CotarAsync(pedido with { Origem = pedido.Origem with { Endereco = "" } }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CotarAsync(pedido with { Origem = pedido.Origem with { Latitude = double.NaN } }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CotarAsync(pedido with { Pedidos = [pedido.Pedidos[0] with { JanelaInicioUtc = DateTime.SpecifyKind(Agora, DateTimeKind.Unspecified) }] }, default));
    }

    [Fact]
    public async Task Falha_http_real_nao_usa_simulacao()
    {
        var handler = new HttpFalso();
        handler.QuandoQualquer().Responde(HttpStatusCode.ServiceUnavailable, "{}");
        var sut = new UberDirectHttpClient(handler.CriarClient(), "customer");
        await Assert.ThrowsAsync<HttpRequestException>(() => sut.CotarAsync(UberRequest()));
    }

    [Fact]
    public async Task Uber_usa_endpoint_e_nomes_do_contrato_publico()
    {
        var handler = new HttpFalso();
        handler.Quando(HttpMethod.Post, "/v1/customers/cliente/delivery_quotes").Responde(HttpStatusCode.OK,
            """{"id":"dqt_1","fee":1690,"currency":"brl","expires":"2026-10-13T12:15:00Z","dropoff_eta":"2026-10-13T13:00:00Z"}""");
        var resposta = await new UberDirectHttpClient(handler.CriarClient(), "cliente").CotarAsync(UberRequest());
        Assert.Equal(1690, resposta.Fee);
        using var json = JsonDocument.Parse(handler.UltimaChamada!.Corpo!);
        Assert.Equal("Origem", json.RootElement.GetProperty("pickup_address").GetString());
        Assert.True(json.RootElement.TryGetProperty("dropoff_deadline_dt", out _));
        Assert.False(json.RootElement.TryGetProperty("PickupAddress", out _));
    }

    [Fact]
    public async Task Lalamove_assina_corpo_exato_preserva_id_e_valor_decimal()
    {
        var handler = new HttpFalso();
        handler.Quando(HttpMethod.Post, "/v3/quotations").Responde(HttpStatusCode.Created,
            """{"data":{"quotationId":"1234567890123456789","expiresAt":"2026-10-13T12:05:00Z","stops":[],"priceBreakdown":{"total":"16.90","currency":"BRL"},"isRouteOptimized":false}}""");
        var client = new LalamoveHttpClient(handler.CriarClient(), "key", "secret", new Relogio(Agora));
        var stops = new[] { new LalamoveParada(new("-23.5", "-46.6"), "Origem"), new LalamoveParada(new("-23.6", "-46.7"), "Destino") };
        var resposta = await client.CotarAsync(new(Agora.AddHours(1), "MOTORCYCLE", "pt_BR", stops, false));
        Assert.Equal("1234567890123456789", resposta.QuotationId);
        Assert.Equal("16.90", resposta.PriceBreakdown.Total);
        var chamada = handler.UltimaChamada!;
        var timestamp = new DateTimeOffset(Agora).ToUnixTimeMilliseconds().ToString();
        var expected = EasyStock.Infra.Integrations.Logistica.Lalamove.AssinadorLalamove.Assinar("secret", long.Parse(timestamp, System.Globalization.CultureInfo.InvariantCulture), "POST", "/v3/quotations", chamada.Corpo!);
        Assert.Equal($"hmac key:{timestamp}:{expected}", chamada.Cabecalho("Authorization"));
        Assert.Equal("BR", chamada.Cabecalho("Market"));
        using var json = JsonDocument.Parse(chamada.Corpo!);
        Assert.Equal(2, json.RootElement.GetProperty("data").GetProperty("stops").GetArrayLength());
    }

    [Fact]
    public void Webhook_uber_valida_bytes_originais_e_rejeita_adulteracao()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"kind\":\"event.delivery_status\",\"name\":\"a\\u0026b\"}");
        var assinatura = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("signing-key"), bytes));
        Assert.True(AssinaturasEntrega.VerificarWebhookUber("signing-key", bytes, assinatura));
        Assert.False(AssinaturasEntrega.VerificarWebhookUber("outra-key", bytes, assinatura));
        Assert.False(AssinaturasEntrega.VerificarWebhookUber("signing-key", Encoding.UTF8.GetBytes("{}"), assinatura));
        Assert.False(AssinaturasEntrega.VerificarWebhookUber("signing-key", bytes, new string('z', 64)));
    }

    [Fact]
    public async Task Handler_simulado_recusa_host_real()
    {
        using var client = new HttpClient(new SimuladorEntregaHttpHandler(new Relogio(Agora)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("https://api.uber.com/v1/customers/test/deliveries/test"));
    }

    [Fact]
    public async Task Clientes_consultam_e_cancelam_contrato_sem_rede()
    {
        using var handler = new SimuladorEntregaHttpHandler(new Relogio(Agora));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://entregas-simuladas.invalid/") };
        var uber = new UberDirectHttpClient(http, "sim_cliente");
        var quote = await uber.CotarAsync(UberRequest());
        var entrega = await uber.CriarAsync(new(quote.Id, "Origem", "Loja", "+5511999990000", "Destino", "Cliente", "+5511999990001",
            [new UberItem("Pedido", 1)], "chave", Agora.AddMinutes(30), Agora.AddHours(2)));
        Assert.False(entrega.LiveMode);
        Assert.Equal("pending", (await uber.ConsultarAsync(entrega.Id)).Status);
        Assert.Equal("canceled", (await uber.CancelarAsync(entrega.Id)).Status);
        Assert.Equal("canceled", (await uber.ConsultarAsync(entrega.Id)).Status);

        var lala = new LalamoveHttpClient(http, "sim-key", "sim-secret", new Relogio(Agora));
        var stops = new[] { new LalamoveParada(new("-23.5", "-46.6"), "Origem"), new LalamoveParada(new("-23.6", "-46.7"), "Destino") };
        var lalaQuote = await lala.CotarAsync(new(Agora.AddHours(1), "MOTORCYCLE", "pt_BR", stops, false));
        var lalaEntrega = await lala.CriarAsync(new(lalaQuote.QuotationId, new(lalaQuote.Stops[0].StopId!, "Loja", "+5511999990000"),
            [new(lalaQuote.Stops[1].StopId!, "Cliente", "+5511999990001")]));
        Assert.Equal("ASSIGNING_DRIVER", (await lala.ConsultarAsync(lalaEntrega.OrderId)).Status);
        await lala.CancelarAsync(lalaEntrega.OrderId);
        Assert.Equal("CANCELED", (await lala.ConsultarAsync(lalaEntrega.OrderId)).Status);
    }

    [Fact]
    public void Nao_inventa_contrato_99()
    {
        Assert.Throws<ArgumentException>(() => new SimuladorFreteProvider("99entrega"));
    }

    private static UberCotacaoRequest UberRequest() => new("Origem", "Destino", -23.5, -46.6, -23.6, -46.7, Agora.AddMinutes(30), Agora.AddHours(2));
    private static FreteSolicitacao Solicitacao(int quantidade = 1) => new(Guid.NewGuid(),
        new("Loja", "Origem sintética", "+5511999990000", -23.5, -46.6),
        Enumerable.Range(0, quantidade).Select(i => new FretePedido(Guid.NewGuid(),
            new("Cliente " + i, "Destino sintético " + i, "+5511999990001", -23.51 - i * .001, -46.61), Agora.AddHours(1), Agora.AddHours(3))).ToArray(), "chave-1");

    private sealed class Relogio(DateTime agora) : TimeProvider
    {
        private DateTimeOffset _agora = agora;
        public override DateTimeOffset GetUtcNow() => _agora;
        public void Avancar(int minutos) => _agora = _agora.AddMinutes(minutos);
    }
}
