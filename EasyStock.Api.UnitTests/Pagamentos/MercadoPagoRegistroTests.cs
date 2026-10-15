using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Infra.Integrations.Pagamentos.MercadoPago;
using EasyStock.Infra.Integrations.Simulacao;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Pagamentos;

/// <summary>
/// I3.1 (#1667): o registro do Mercado Pago falha fechado. Em Production não sobe com simulador, token de
/// teste ou endereço alheio; sem token, o cliente diz "não configurado" em vez de bater no provedor.
/// </summary>
public class MercadoPagoRegistroTests
{
    private static ServiceProvider Registrar(string ambiente, params (string Chave, string? Valor)[] config)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config.ToDictionary(c => c.Chave, c => c.Valor)).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMercadoPagoClient(configuration, ambiente);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("")] // sem ambiente conhecido vale Production
    public void Production_com_UseStub_true_nao_sobe(string ambiente)
    {
        var act = () => Registrar(ambiente, ("MercadoPago:UseStub", "true"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*MercadoPago:UseStub*");
    }

    [Theory]
    [InlineData("TEST-1234567890-abcdef")]
    [InlineData("test-1234567890-abcdef")]
    public void Production_com_token_de_teste_nao_sobe(string token)
    {
        var act = () => Registrar("Production", ("MercadoPago:AccessToken", token));

        act.Should().Throw<InvalidOperationException>().WithMessage("*TEST-*")
            .Which.Message.Should().NotContain("1234567890", "o token não vai para a mensagem");
    }

    [Theory]
    [InlineData("http://localhost:9000/")]
    [InlineData("https://api.mercadopago.com.evil.test/")]
    [InlineData("http://api.mercadopago.com/")]
    public void Production_com_base_url_alheia_nao_sobe(string baseUrl)
    {
        var act = () => Registrar("Production", ("MercadoPago:AccessToken", "APP_USR-abc"), ("MercadoPago:BaseUrl", baseUrl));

        act.Should().Throw<InvalidOperationException>().WithMessage("*BaseUrl*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://api.mercadopago.com/")]
    public void Production_com_credencial_de_producao_sobe_com_o_cliente_real(string? baseUrl)
    {
        using var sp = Registrar("Production", ("MercadoPago:AccessToken", "APP_USR-abc"), ("MercadoPago:BaseUrl", baseUrl));

        sp.GetRequiredService<IMercadoPagoClient>().Should().BeOfType<MercadoPagoClient>();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Sem_token_e_sem_stub_sobe_e_diz_que_nao_esta_configurado(string ambiente)
    {
        using var sp = Registrar(ambiente);
        var client = sp.GetRequiredService<IMercadoPagoClient>();

        client.Should().NotBeAssignableTo<ISimulador>();
        var act = () => client.CriarPreferenceAsync(new CriarPreferenceCommand(Guid.NewGuid(), Guid.NewGuid(), "Loja", 10m, []));
        await act.Should().ThrowAsync<MercadoPagoNaoConfiguradoException>();
        var busca = () => client.BuscarPagamentosPorReferenciaAsync("ref");
        await busca.Should().ThrowAsync<MercadoPagoNaoConfiguradoException>("sem credencial não é o mesmo que sem pagamento");
    }

    [Fact]
    public void Development_com_stub_resolve_um_simulador()
    {
        using var sp = Registrar("Development", ("MercadoPago:UseStub", "true"));

        sp.GetRequiredService<IMercadoPagoClient>().Should().BeAssignableTo<ISimulador>();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("")]
    [InlineData(null)]
    public void Fabrica_de_simulador_lanca_em_Production(string? ambiente)
    {
        var act = () => FabricaDeSimulador.Criar(ambiente, () => new StubMercadoPagoClient(NullLogger<StubMercadoPagoClient>.Instance));

        act.Should().Throw<InvalidOperationException>().WithMessage("*StubMercadoPagoClient*");
    }

    [Fact]
    public async Task Stub_devolve_desfecho_Simulado()
    {
        var stub = new StubMercadoPagoClient(NullLogger<StubMercadoPagoClient>.Instance);

        var estorno = await stub.EstornarAsync("123", 10m, "chave-1");
        var desfecho = await new MercadoPagoEstornoPedidoGateway(stub, NullLogger<MercadoPagoEstornoPedidoGateway>.Instance)
            .EstornarAsync("123", 10m, "chave-1");

        estorno.Status.Should().Be(EstornoMercadoPagoResult.Simulado).And.NotBe(PagamentoMercadoPago.Approved);
        desfecho.Desfecho.Should().Be(DesfechoEstornoPedido.Simulado);
    }
}