using System.Net;
using System.Text;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;
using EasyStock.Infra.Integrations.DependencyInjection;
using EasyStock.Infra.Integrations.Ia;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Integrations.UnitTests.Ia;

/// <summary>
/// #3365 (D2 da revisão #3271): o custo do turno nasce no registro da chamada, com a tabela <c>Anthropic:Precos</c>. O guard
/// de arquitetura vê o construtor, mas não a fiação: com o decorador ausente ou a tabela não ligada à configuração o custo
/// do turno ficaria nulo em silêncio. Aqui a composição de produção (Application + AgenteLlm) devolve a resposta com custo.
/// </summary>
public class AgenteLlmCustoComposicaoTests
{
    // (120 + 1000 de escrita) × 3 + 30 × 15 + 500 × 0,30, por milhão = 0,00396.
    private const string RespostaDoModelo = """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-sonnet-5-20261101",
         "content":[{"type":"text","text":"Oi!"}],"stop_reason":"end_turn",
         "usage":{"input_tokens":120,"output_tokens":30,"cache_read_input_tokens":500,"cache_creation_input_tokens":1000}}
        """;

    private static ServiceProvider Provedor(Dictionary<string, string?> extras, IRegistroChamadaIa? registro)
    {
        var dados = new Dictionary<string, string?>
        {
            ["MontagemFicha:Cli:Enabled"] = "false", ["Anthropic:Enabled"] = "true", ["Anthropic:ApiKey"] = "chave-de-teste",
        };
        foreach (var (chave, valor) in extras) dados[chave] = valor;
        var config = new ConfigurationBuilder().AddInMemoryCollection(dados).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddEasyStockAtendimentoUseCases(); // liga Anthropic:Precos em PrecosModelosAgenteOptions, como Api e Worker
        services.AddEasyStockAgenteLlm(config);
        services.AddHttpClient<AnthropicMessagesClient>().ConfigurePrimaryHttpMessageHandler(() => new RespostaFixaHandler(RespostaDoModelo));
        if (registro is not null) services.AddSingleton(registro);
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> Precos() => new()
    {
        ["Anthropic:Precos:claude-sonnet-5:Entrada"] = "3", ["Anthropic:Precos:claude-sonnet-5:Saida"] = "15",
        ["Anthropic:Precos:claude-sonnet-5:CacheLido"] = "0.3",
    };

    private static RequisicaoLlm Requisicao() =>
        new("s", [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], [])
        { EmpresaId = Guid.NewGuid(), Finalidade = FinalidadeIa.Atendimento };

    [Fact]
    public async Task ComRegistroEPrecosNaConfiguracaoARespostaSaiComOMesmoCustoDaLinha()
    {
        var gravadas = new List<ChamadaIa>();
        var registro = Substitute.For<IRegistroChamadaIa>();
        registro.RegistrarAsync(Arg.Do<ChamadaIa>(gravadas.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await using var provider = Provedor(Precos(), registro);

        var resposta = await provider.GetRequiredService<IAgenteLlmClient>().EnviarAsync(Requisicao());

        resposta.CustoUsd.Should().Be(0.00396m);
        gravadas.Should().ContainSingle().Which.CustoUsd.Should().Be(0.00396m);
    }

    [Fact]
    public async Task SemTabelaDePrecosNaConfiguracaoARespostaSaiSemCustoNuncaZero()
    {
        var registro = Substitute.For<IRegistroChamadaIa>();
        await using var provider = Provedor([], registro);

        var resposta = await provider.GetRequiredService<IAgenteLlmClient>().EnviarAsync(Requisicao());

        resposta.CustoUsd.Should().BeNull();
    }

    private sealed class RespostaFixaHandler(string corpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") });
    }
}
