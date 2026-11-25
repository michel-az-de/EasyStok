using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#3175: os códigos de motivo são uma lista fechada e todos passam na validação do domínio.</summary>
public class MotivoDoTurnoTests
{
    private static void ValidoNoDominio(string motivo) =>
        TurnoAgente.Registrar(Guid.NewGuid(), Guid.NewGuid(), null, null, null, 0, 0, 0, null, [], 0,
            DesfechoTurnoAgente.Escalou, 0, DateTime.UtcNow, motivo).Motivo.Should().Be(motivo);

    [Fact]
    public void Toda_CategoriaFixaDaGuardaTemSlugProprioQuePassaNoDominio()
    {
        var categorias = LogsDoAssistente.CategoriasDaGuarda
            .Append(LogsDoAssistente.CategoriaFerramentaFalhou).Distinct().ToList();
        categorias.Should().NotBeEmpty();

        var slugs = categorias.Select(MotivoDoTurno.DaGuarda).ToList();

        slugs.Should().OnlyHaveUniqueItems("cada categoria tem o seu código");
        slugs.Should().NotContain(MotivoDoTurno.GuardaOutro, "categoria nova na lista do log precisa de slug aqui");
        slugs.Should().NotContain(MotivoDoTurno.GuardaValor);
        slugs.Should().OnlyContain(s => s.StartsWith("guarda:"));
        slugs.ForEach(ValidoNoDominio);
    }

    [Fact]
    public void ValorEOutroTemCodigoProprio()
    {
        MotivoDoTurno.DaGuarda(LogsDoAssistente.CategoriaValor).Should().Be("guarda:valor");
        MotivoDoTurno.DaGuarda(LogsDoAssistente.CategoriaOutra).Should().Be("guarda:outro");
        MotivoDoTurno.DaGuarda("cliente Maria pediu 59").Should().Be("guarda:outro");
    }

    [Fact]
    public void FalhaDaGeracaoNaoLevaOTextoDoMotivo()
    {
        MotivoDoTurno.DaFalhaDaGeracao("o agente não conseguiu responder (falha ao chamar o LLM)", true).Should().Be("falha_llm");
        MotivoDoTurno.DaFalhaDaGeracao("o agente não produziu resposta (stop_reason=max_tokens)", false).Should().Be("guarda:sem_resposta");
        MotivoDoTurno.DaFalhaDaGeracao("qualquer coisa com R$ 59", false).Should().Be("guarda:outro");
    }

    [Theory]
    [InlineData(GeracaoAgenteAtendimento.MotivoRecusa, "recusa_modelo")]
    [InlineData(GeracaoAgenteAtendimento.MotivoRecusa + " (refusal)", "recusa_modelo")]
    [InlineData(GeracaoAgenteAtendimento.MotivoTetoDeSaida + " (max_tokens)", "teto_saida")]
    public void RecusaETetoDeSaidaTemCodigoProprio(string motivoDaGeracao, string esperado) =>
        MotivoDoTurno.DaFalhaDaGeracao(motivoDaGeracao, false).Should().Be(esperado);

    // Código de portão que o domínio recusasse derrubaria o turno depois de escalar e antes do commit.
    [Fact]
    public void TodoPortaoDoCatalogoViraMotivoQueODominioAceita()
    {
        foreach (var portao in PortoesDoAssistente.Catalogo)
            FluentActions.Invoking(() => TurnoAgente.Registrar(Guid.NewGuid(), Guid.NewGuid(), null, null, null, 0, 0, 0, null, [], 0,
                DesfechoTurnoAgente.Escalou, 0, DateTime.UtcNow, MotivoDoTurno.DoPortao(portao.Codigo))).Should().NotThrow(portao.Codigo);
    }

    [Theory]
    [InlineData("limite")]
    [InlineData("bloqueado")]
    [InlineData("cota")]
    public void PortaoViraCodigoValido(string portao)
    {
        MotivoDoTurno.DoPortao(portao).Should().Be("portao:" + portao);
        ValidoNoDominio(MotivoDoTurno.DoPortao(portao));
    }

    [Fact]
    public void CodigosFixosPassamNoDominio() =>
        new[] { MotivoDoTurno.Iteracoes, MotivoDoTurno.Menu, MotivoDoTurno.FalhaLlm, MotivoDoTurno.TetoSaida, MotivoDoTurno.PrazoTurno,
                MotivoDoTurno.RecusaModelo, MotivoDoTurno.EscaladaModelo, MotivoDoTurno.Interrompido }
            .ToList().ForEach(ValidoNoDominio);
}
