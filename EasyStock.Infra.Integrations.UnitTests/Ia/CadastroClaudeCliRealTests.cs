using System.Diagnostics;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Integrations.Ia;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit.Abstractions;

namespace EasyStock.Infra.Integrations.UnitTests.Ia;

/// <summary>Uma inferência opt-in no adaptador real, apenas com dados sintéticos e sem portas de escrita/envio.</summary>
public class CadastroClaudeCliRealTests(ITestOutputHelper output)
{
    [CadastroCliRealFact]
    public async Task Sugere_tres_campos_sinteticos_com_evidencia_sem_escrita()
    {
        var caminho = Environment.GetEnvironmentVariable("EASYSTOK_TEST_CLAUDE_PATH");
        var cli = new ClaudeCliMontagemFichaClient(Options.Create(new MontagemFichaCliOptions
        {
            Enabled = true, ExecutablePath = caminho, TimeoutSegundos = 60,
        }), NullLogger<ClaudeCliMontagemFichaClient>.Instance);
        cli.Disponivel.Should().BeTrue("o opt-in exige caminho absoluto para o CLI já autenticado");
        var captura = new Captura(cli);
        var empresa = Guid.NewGuid();
        var conversa = Conversa.Abrir(empresa, "5511999998888", DateTime.UtcNow, "Contato sintético");
        var mensagem = Mensagem.Entrada(empresa, conversa.Id, DateTime.UtcNow, TipoConteudoMensagem.Texto,
            "Meu nome é Ana Silva. Meu telefone é (11) 99999-8888. Meu email é ana@example.com.", "wamid.sintetico");
        var repo = Substitute.For<IConversaRepository>();
        repo.ObterComMensagensAsync(empresa, conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ConversaComMensagens(conversa, [mensagem]));
        var useCase = new SugerirCadastroConversaUseCase(captura, repo, NullLogger<SugerirCadastroConversaUseCase>.Instance);
        var tempo = Stopwatch.StartNew();

        var resultado = await useCase.ExecuteAsync(new(empresa, conversa.Id));

        captura.Chamadas.Should().Be(1);
        resultado.Campos.Select(c => c.Campo).Should().BeEquivalentTo("nome", "telefone", "email");
        resultado.Campos.Single(c => c.Campo == "nome").Valor.Should().Be("Ana Silva");
        resultado.Campos.Single(c => c.Campo == "telefone").Valor.Should().Be("11999998888");
        resultado.Campos.Single(c => c.Campo == "email").Valor.Should().Be("ana@example.com");
        resultado.Campos.Should().OnlyContain(c => c.MensagemId == mensagem.Id && mensagem.Texto!.Contains(c.Evidencia));
        conversa.ClienteId.Should().BeNull();
        conversa.VinculoRevisao.Should().Be(0);
        repo.ReceivedCalls().Should().ContainSingle("o cadastro só lê contexto; não resolve identidade nem persiste");
        output.WriteLine($"CLI real: {captura.Chamadas} chamada, {tempo.ElapsedMilliseconds} ms, {captura.Resposta!.TokensEntrada}+{captura.Resposta.TokensSaida} tokens; 3 campos validados; zero portas de envio/pagamento.");
    }

    private sealed class Captura(IMontagemFichaLlmClient client) : IMontagemFichaLlmClient
    {
        public bool Disponivel => client.Disponivel;
        public int Chamadas { get; private set; }
        public RespostaLlm? Resposta { get; private set; }
        public async Task<RespostaLlm> EnviarAsync(RequisicaoLlm requisicao, CancellationToken ct = default)
        {
            Chamadas++;
            return Resposta = await client.EnviarAsync(requisicao, ct);
        }
    }
}

public sealed class CadastroCliRealFactAttribute : FactAttribute
{
    public CadastroCliRealFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("EASYSTOK_TEST_CADASTRO_CLI_REAL") != "1")
            Skip = "Inferência real desligada. Exige opt-in EASYSTOK_TEST_CADASTRO_CLI_REAL=1 e EASYSTOK_TEST_CLAUDE_PATH absoluto.";
    }
}
