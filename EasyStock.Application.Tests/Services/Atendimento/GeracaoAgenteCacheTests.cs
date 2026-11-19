using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #2862: prompt caching do agente. A parte estável do system (prompt fixo + caderno) não muda entre turnos; a data e o
/// dossiê vão em <see cref="RequisicaoLlm.SystemVolatil"/>, depois do breakpoint de cache.
/// </summary>
public class GeracaoAgenteCacheTests
{
    private static readonly DateTime Agora = new(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresaId = Guid.NewGuid();

    private async Task<SystemTurnoAgente> TurnoAsync(Conversa conversa, DateTime agora)
    {
        var caderno = Substitute.For<ICadernoRepository>();
        caderno.ListarAsync(_empresaId, false, Arg.Any<CancellationToken>()).Returns(new List<TrechoCaderno>());
        return await GeracaoAgenteAtendimento.MontarSystemTurnoAsync(
            _empresaId, ConfiguracaoAtendimento.CriarPadrao(_empresaId), conversa, cliente: null, [], agora,
            caderno, dossieUseCase: null!, CancellationToken.None);
    }

    private static IFerramentaAgente Ferramenta(string nome)
    {
        var ferramenta = Substitute.For<IFerramentaAgente>();
        ferramenta.Nome.Returns(nome);
        ferramenta.Descricao.Returns("descrição de " + nome);
        ferramenta.SchemaJson.Returns("""{"type":"object","properties":{}}""");
        return ferramenta;
    }

    private static List<MensagemLlm> Mensagens() => [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])];

    private static IAgenteLlmClient LlmQueCaptura(List<RequisicaoLlm> capturadas, RespostaLlm resposta)
    {
        var llm = Substitute.For<IAgenteLlmClient>();
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(x =>
        {
            capturadas.Add(x.Arg<RequisicaoLlm>());
            return resposta;
        });
        return llm;
    }

    [Fact]
    public async Task ParteEstavelNaoMudaEntreTurnosComHorarioDiferente()
    {
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", Agora);

        var primeiro = await TurnoAsync(conversa, Agora);
        var depois = await TurnoAsync(conversa, Agora.AddHours(3).AddMinutes(7));

        depois.Estavel.Should().Be(primeiro.Estavel);
        depois.Volatil.Should().NotBe(primeiro.Volatil);
    }

    [Fact]
    public async Task DataEDossieFicamSoNaParteVolatil()
    {
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", Agora);

        var turno = await TurnoAsync(conversa, Agora);

        turno.Estavel.Should().Be(PromptAtendimento.Montar(ConfiguracaoAtendimento.CriarPadrao(_empresaId)));
        turno.Estavel.Should().NotContain("- Agora:").And.NotContain("Dossiê desta conversa");
        turno.Volatil.Should().StartWith("\n\nDossiê desta conversa").And.Contain("- Agora:");
    }

    [Fact]
    public async Task TextoTotalSaiComUmaQuebraDeLinhaDupla()
    {
        // A API junta os blocos do system sem separador: o "\n\n" fica no começo do bloco volátil.
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", Agora);

        var turno = await TurnoAsync(conversa, Agora);

        turno.Texto.Should().Be(turno.Estavel + turno.Volatil);
        turno.Texto.Should().Contain(PromptAtendimento.Montar(ConfiguracaoAtendimento.CriarPadrao(_empresaId)) + "\n\nDossiê desta conversa");
    }

    [Fact]
    public async Task RequisicaoLevaDataEDossieSoNoSystemVolatil_EEstavelIgualEntreTurnos()
    {
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", Agora);
        var capturadas = new List<RequisicaoLlm>();
        var llm = LlmQueCaptura(capturadas, new RespostaLlm("end_turn", [new BlocoTextoLlm("ok")], 1, 1));

        foreach (var agora in new[] { Agora, Agora.AddHours(2) })
        {
            var turno = await TurnoAsync(conversa, agora);
            await GeracaoAgenteAtendimento.GerarAsync(llm, turno.Estavel, Mensagens(), new Dictionary<string, IFerramentaAgente>(),
                new ContextoTurnoAgente(_empresaId, conversa, agora), 1, NullLogger.Instance, CancellationToken.None,
                systemVolatil: turno.Volatil);
        }

        capturadas.Should().HaveCount(2);
        capturadas[0].System.Should().Be(capturadas[1].System);
        capturadas[0].System.Should().NotContain("- Agora:");
        capturadas[0].SystemVolatil.Should().Contain("- Agora:");
        capturadas[1].SystemVolatil.Should().NotBe(capturadas[0].SystemVolatil);
    }

    [Fact]
    public async Task FerramentasSaemEmOrdemAlfabeticaOrdinal()
    {
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", Agora);
        var capturadas = new List<RequisicaoLlm>();
        var llm = LlmQueCaptura(capturadas, new RespostaLlm("end_turn", [new BlocoTextoLlm("ok")], 1, 1));
        var ferramentas = new Dictionary<string, IFerramentaAgente>
        {
            ["zeta"] = Ferramenta("zeta"),
            ["alfa"] = Ferramenta("alfa"),
            ["Beta"] = Ferramenta("Beta"),
        };

        await GeracaoAgenteAtendimento.GerarAsync(llm, "s", Mensagens(), ferramentas,
            new ContextoTurnoAgente(_empresaId, conversa, Agora), 1, NullLogger.Instance, CancellationToken.None);

        // Ordinal: maiúscula antes de minúscula, como o cache pede uma ordem estável e não de cultura.
        capturadas.Single().Ferramentas.Select(f => f.Nome).Should().Equal("Beta", "alfa", "zeta");
    }

    [Fact]
    public async Task ConsumoSomaEscritaDeCacheNaEntradaETotalDaCota()
    {
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", Agora);
        var llm = LlmQueCaptura([], new RespostaLlm("end_turn", [new BlocoTextoLlm("ok")], 40, 8, 900, "claude-sonnet-5", 1200));

        var geracao = await GeracaoAgenteAtendimento.GerarAsync(llm, "s", Mensagens(), new Dictionary<string, IFerramentaAgente>(),
            new ContextoTurnoAgente(_empresaId, conversa, Agora), 1, NullLogger.Instance, CancellationToken.None);

        // A escrita de cache entra na entrada do consumo (sem migração). O total da cota soma entrada, saída, leitura e escrita.
        geracao.Consumo!.TokensEntrada.Should().Be(40 + 1200);
        geracao.Consumo.TokensCacheLido.Should().Be(900);
        geracao.Tokens.Should().Be(40 + 8 + 900 + 1200);
    }
}
