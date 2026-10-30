using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Infra.Integrations.DependencyInjection;
using EasyStock.Infra.Integrations.Ia;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.UnitTests.Ia;

public class ClaudeCliMontagemFichaClientTests
{
    private const string Valida = """{"subtype":"success","is_error":false,"permission_denials":[],"structured_output":{"blocos":[{"tipo":"nota","texto":"Prefere retirada no sábado."}]},"usage":{"input_tokens":2,"cache_creation_input_tokens":30,"cache_read_input_tokens":100,"output_tokens":10}}""";
    private static MontagemFichaCliOptions Opcoes() => new() { Enabled = true, ExecutablePath = Environment.ProcessPath! };
    private static RequisicaoLlm Requisicao() => new(MontagemFichaUseCase.SystemPrompt,
        [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("Cliente Ana: prefere retirada no sábado. Pedido real: duas lasanhas.")])],
        [BlocosMontagemFicha.Ferramenta]);
    private static ClaudeCliMontagemFichaClient Criar(ExecutorMontagemCli executar, MontagemFichaCliOptions? opcoes = null) =>
        new(Options.Create(opcoes ?? Opcoes()), NullLogger<ClaudeCliMontagemFichaClient>.Instance, executar);

    [Fact]
    public async Task ContextoNaoSomeNoStdinERecursosPerigososNaoSaoOferecidos()
    {
        string? cwd = null;
        var client = Criar((inicio, entrada, opcoes, ct) =>
        {
            cwd = inicio.WorkingDirectory;
            inicio.UseShellExecute.Should().BeFalse();
            inicio.RedirectStandardInput.Should().BeTrue();
            Path.GetFullPath(cwd).Should().StartWith(Path.GetFullPath(Path.GetTempPath()));
            var args = inicio.ArgumentList.ToArray();
            foreach (var flag in new[] { "--safe-mode", "--restricted", "--disable-slash-commands", "--strict-mcp-config", "--no-session-persistence" })
                args.Should().Contain(flag);
            args[Array.IndexOf(args, "--tools") + 1].Should().BeEmpty();
            args[Array.IndexOf(args, "--setting-sources") + 1].Should().BeEmpty();
            args[Array.IndexOf(args, "--mcp-config") + 1].Should().Be("{\"mcpServers\":{}}");
            args[Array.IndexOf(args, "--json-schema") + 1].Should().Be(BlocosMontagemFicha.Ferramenta.SchemaJson);
            string.Join(" ", args).Should().NotContain("Cliente Ana");
            inicio.Environment.Keys.Should().NotContain(n => n.StartsWith("ANTHROPIC_", StringComparison.OrdinalIgnoreCase));
            using var doc = JsonDocument.Parse(entrada);
            doc.RootElement[0].GetProperty("papel").GetString().Should().Be("user");
            doc.RootElement[0].GetProperty("textos")[0].GetString().Should().Contain("Cliente Ana").And.Contain("retirada no sábado").And.Contain("duas lasanhas");
            return Task.FromResult(new ResultadoMontagemCli(0, Valida));
        });
        var resposta = await client.EnviarAsync(Requisicao());
        resposta.TokensEntrada.Should().Be(132);
        resposta.TokensSaida.Should().Be(10);
        BlocosMontagemFicha.Traduzir(resposta).Should().ContainSingle().Which.Texto.Should().Be("Prefere retirada no sábado.");
        Directory.Exists(cwd).Should().BeFalse("a chamada isolada vazia é removida ao terminar");
    }

    [Fact]
    public async Task DesligadoOuCaminhoRelativoNaoIniciaProcesso()
    {
        foreach (var opcoes in new[] { new MontagemFichaCliOptions(), new MontagemFichaCliOptions { Enabled = true, ExecutablePath = "claude.exe" },
            new MontagemFichaCliOptions { Enabled = true, ExecutablePath = Environment.ProcessPath, TimeoutSegundos = 1000 } })
        {
            var client = Criar((_, _, _, _) => throw new Exception("não deve executar"), opcoes);
            client.Disponivel.Should().BeFalse();
            await FluentActions.Invoking(() => client.EnviarAsync(Requisicao())).Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task ConcorrenciaMaximaUmSemFilaELiberaDepoisDoSucesso()
    {
        var pendente = new TaskCompletionSource<ResultadoMontagemCli>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chamadas = 0;
        var client = Criar((_, _, _, _) => { chamadas++; return pendente.Task; });
        var primeira = client.EnviarAsync(Requisicao());
        await FluentActions.Invoking(() => client.EnviarAsync(Requisicao())).Should().ThrowAsync<InvalidOperationException>();
        chamadas.Should().Be(1);
        pendente.SetResult(new(0, Valida)); await primeira;
        await client.EnviarAsync(Requisicao());
        chamadas.Should().Be(2);
    }

    [Fact]
    public async Task CancelamentoOuFalhaNaoPrendeAVagaENaoViraRespostaFalsa()
    {
        using var cancelar = new CancellationTokenSource();
        var chamadas = 0;
        var client = Criar((_, _, _, ct) => { chamadas++; return chamadas == 1
            ? Task.FromCanceled<ResultadoMontagemCli>(new CancellationToken(true)) : Task.FromResult(new ResultadoMontagemCli(0, Valida)); });
        await FluentActions.Invoking(() => client.EnviarAsync(Requisicao(), cancelar.Token)).Should().ThrowAsync<OperationCanceledException>();
        await client.EnviarAsync(Requisicao());
        chamadas.Should().Be(2);
        var falha = Criar((_, _, _, _) => Task.FromResult(new ResultadoMontagemCli(1, Valida)));
        await FluentActions.Invoking(() => falha.EnviarAsync(Requisicao())).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("{\"subtype\":\"error_during_execution\",\"is_error\":true}")]
    [InlineData("{\"subtype\":\"success\",\"is_error\":false,\"result\":\"texto livre não é JSON tipado\"}")]
    [InlineData("{\"subtype\":\"success\",\"is_error\":false,\"structured_output\":{\"blocos\":[{\"tipo\":\"html\",\"texto\":\"texto\"}]}}")]
    [InlineData("{\"subtype\":\"success\",\"is_error\":false,\"permission_denials\":[{}],\"structured_output\":{\"blocos\":[]}}")]
    [InlineData("{\"subtype\":\"success\",\"is_error\":false,\"structured_output\":{\"blocos\":[{\"tipo\":\"comanda\",\"texto\":\"texto\",\"total\":1}]}}")]
    public void NaoTransformaErroTextoLivreOuTipoDesconhecidoEmComposicao(string json) =>
        FluentActions.Invoking(() => ClaudeCliMontagemFichaClient.Traduzir(json)).Should().Throw<Exception>();

    [Fact]
    public async Task LeitoresLimitamBytesEAcionamEncerramentoNoExcesso()
    {
        var encerramentos = 0;
        await using var excesso = new MemoryStream(new byte[2049]);
        await FluentActions.Invoking(() => ProcessoClaudeFicha.LerLimitadoAsync(excesso, 2048, () => encerramentos++, CancellationToken.None))
            .Should().ThrowAsync<InvalidDataException>();
        encerramentos.Should().Be(1);
        await using var normal = new MemoryStream(Encoding.UTF8.GetBytes("Preferência: sábado."));
        (await ProcessoClaudeFicha.LerLimitadoAsync(normal, 2048, () => encerramentos++, CancellationToken.None)).Should().Be("Preferência: sábado.");
        encerramentos.Should().Be(1);
    }

    [Fact]
    public void CapturaETelemetriaHerdadasSaoDesativadasSoNoDicionarioDoFilho()
    {
        var inicio = new ProcessStartInfo();
        inicio.Environment["CLAUDE_CODE_ENABLE_TELEMETRY"] = "1";
        inicio.Environment["OTEL_LOG_USER_PROMPTS"] = "1";
        inicio.Environment["OTEL_LOG_RAW_API_BODIES"] = "file:sentinela-nao-criar";
        inicio.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://captura.invalid";
        inicio.Environment["ANTHROPIC_BASE_URL"] = "https://provedor-nao-autorizado.invalid";
        ClaudeCliMontagemFichaClient.LimparAmbiente(inicio);
        inicio.Environment.Keys.Should().NotContain(k => k.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase));
        inicio.Environment.Keys.Should().NotContain(k => k.StartsWith("ANTHROPIC_", StringComparison.OrdinalIgnoreCase));
        inicio.Environment["CLAUDE_CODE_ENABLE_TELEMETRY"].Should().Be("0");
        inicio.Environment["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"].Should().Be("1");
        inicio.Environment["DISABLE_TELEMETRY"].Should().Be("1");
        inicio.Environment["DO_NOT_TRACK"].Should().Be("1");
        // ProcessStartInfo.Environment é uma cópia; nenhuma SetEnvironmentVariable é executada.
        Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT").Should().NotBe("https://captura.invalid");
    }

    [Fact]
    public async Task RecusaContextoNaoTextualOuGrandeAntesDeExecutar()
    {
        var client = Criar((_, _, _, _) => throw new Exception("não deve executar"));
        var opaco = Requisicao() with { Mensagens = [new MensagemLlm("user", [new BlocoUsoFerramentaLlm("id", "ler", JsonDocument.Parse("{}").RootElement.Clone())])] };
        await FluentActions.Invoking(() => client.EnviarAsync(opaco)).Should().ThrowAsync<InvalidOperationException>();
        var enorme = Requisicao() with { Mensagens = [new MensagemLlm("user", [new BlocoTextoLlm(new string('x', 131073))])] };
        await FluentActions.Invoking(() => client.EnviarAsync(enorme)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public void OptInMudaSomenteMontagemNuncaOAgenteGlobal()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["MontagemFicha:Cli:Enabled"] = "true", ["MontagemFicha:Cli:ExecutablePath"] = Environment.ProcessPath,
            ["Anthropic:Enabled"] = "false" }).Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddEasyStockAgenteLlm(config);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMontagemFichaLlmClient>().Should().BeOfType<ClaudeCliMontagemFichaClient>();
        provider.GetRequiredService<IAgenteLlmClient>().Should().BeOfType<AnthropicMessagesClient>().Which.Disponivel.Should().BeFalse();
    }
}
