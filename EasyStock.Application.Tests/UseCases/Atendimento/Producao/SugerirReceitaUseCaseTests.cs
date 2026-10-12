using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.UseCases.Atendimento.Producao;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Producao;

public class SugerirReceitaUseCaseTests
{
    private const string ReceitaValida = """
        {"rendimento":10,"unidadeRendimento":"Un","linhas":[{"nome":"Farinha","quantidade":500,"unidade":"G"}],"observacoes":"Revisar antes de aplicar."}
        """;
    private static readonly FonteProducaoWeb Fonte = new("Livro de receitas/Bolo", "https://pt.wikibooks.org/wiki/Livro_de_receitas/Bolo", "500 g de farinha. Rende 10 unidades.", DateTimeOffset.Parse("2026-10-11T12:00:00Z"));
    private readonly IPesquisaProducaoWeb _pesquisa = Substitute.For<IPesquisaProducaoWeb>();
    private readonly IAgenteLlmClient _llm = Substitute.For<IAgenteLlmClient>();

    public SugerirReceitaUseCaseTests()
    {
        _llm.Disponivel.Returns(true);
        _pesquisa.BuscarReceitasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<FonteProducaoWeb>>([Fonte]));
        Responder(ReceitaValida);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("bolo\nruim")]
    public async Task Termo_invalido_nao_consulta_fontes_nem_ia(string? termo)
    {
        var acao = () => Criar().ExecuteAsync(termo!, CancellationToken.None);
        await acao.Should().ThrowAsync<UseCaseValidationException>();
        await _pesquisa.DidNotReceiveWithAnyArgs().BuscarReceitasAsync(default!, default);
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    [Fact]
    public async Task Termo_acima_do_limite_nao_inicia_pesquisa()
    {
        var acao = () => Criar().ExecuteAsync(new string('b', 101), CancellationToken.None);
        await acao.Should().ThrowAsync<UseCaseValidationException>();
        await _pesquisa.DidNotReceiveWithAnyArgs().BuscarReceitasAsync(default!, default);
    }

    [Fact]
    public async Task Ia_indisponivel_nao_inventa_receita_nem_consulta_fontes()
    {
        _llm.Disponivel.Returns(false);
        var acao = () => Criar().ExecuteAsync("Bolo", CancellationToken.None);
        await acao.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*não está disponível*");
        await _pesquisa.DidNotReceiveWithAnyArgs().BuscarReceitasAsync(default!, default);
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    [Fact]
    public async Task Fontes_vazias_nao_geram_receita_sem_evidencia()
    {
        _pesquisa.BuscarReceitasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<FonteProducaoWeb>>([]));
        var acao = () => Criar().ExecuteAsync("Bolo", CancellationToken.None);
        await acao.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*fontes públicas*");
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    [Fact]
    public async Task Receita_preserva_fontes_http_e_envia_evidencia_ao_provedor_existente()
    {
        RequisicaoLlm? enviada = null;
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(info =>
        {
            enviada = info.Arg<RequisicaoLlm>();
            return Task.FromResult(Resposta(ReceitaValida));
        });

        var resultado = await Criar().ExecuteAsync("  Bolo  ", CancellationToken.None);

        resultado.Fontes.Should().Equal(Fonte);
        resultado.Rendimento.Should().Be(10);
        resultado.Linhas.Should().Equal(new LinhaReceitaSugerida("Farinha", 500, "G"));
        enviada.Should().NotBeNull();
        enviada!.Ferramentas.Select(f => f.Nome).Should().Equal("sugerir_receita");
        using var dados = JsonDocument.Parse(enviada.Mensagens.Single().Conteudo.OfType<BlocoTextoLlm>().Single().Texto);
        dados.RootElement.GetProperty("prato").GetString().Should().Be("Bolo");
        var fonte = dados.RootElement.GetProperty("fontes")[0];
        fonte.GetProperty("Url").GetString().Should().Be(Fonte.Url);
        fonte.GetProperty("Texto").GetString().Should().Be(Fonte.Texto);
        fonte.GetProperty("ConsultadaEm").GetDateTimeOffset().Should().Be(Fonte.ConsultadaEm);
    }

    [Fact]
    public async Task Campo_sem_medida_permanece_null_para_revisao()
    {
        Responder("""{"rendimento":null,"unidadeRendimento":null,"linhas":[{"nome":"Farinha","quantidade":null,"unidade":null}],"observacoes":"A fonte usa xícaras."}""");
        var resultado = await Criar().ExecuteAsync("Bolo", CancellationToken.None);
        resultado.Rendimento.Should().BeNull();
        resultado.UnidadeRendimento.Should().BeNull();
        resultado.Linhas.Should().Equal(new LinhaReceitaSugerida("Farinha", null, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("max_tokens")]
    [InlineData("end_turn")]
    public async Task Resposta_truncada_ou_sem_tool_use_nao_vira_sugestao(string? motivo)
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Resposta(ReceitaValida) with { StopReason = motivo }));
        var acao = () => Criar().ExecuteAsync("Bolo", CancellationToken.None);
        await acao.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Ferramentas_ausentes_ou_multiplas_sao_rejeitadas(int quantidade)
    {
        var blocos = Enumerable.Range(0, quantidade).Select(_ => (BlocoLlm)Uso(ReceitaValida)).ToArray();
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(new RespostaLlm(RespostaLlm.StopToolUse, blocos, 10, 20)));
        var acao = () => Criar().ExecuteAsync("Bolo", CancellationToken.None);
        await acao.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task Ferramenta_de_escrita_nao_e_aceita_como_receita()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(new RespostaLlm(RespostaLlm.StopToolUse, [Uso(ReceitaValida) with { Nome = "registrar_producao" }], 10, 20)));
        var acao = () => Criar().ExecuteAsync("Bolo", CancellationToken.None);
        await acao.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"rendimento":10,"unidadeRendimento":"Un","linhas":[],"observacoes":""}""")]
    [InlineData("""{"rendimento":10,"unidadeRendimento":"Un","linhas":{},"observacoes":""}""")]
    [InlineData("""{"rendimento":10,"unidadeRendimento":"Un","linhas":[{"nome":"Ovo","quantidade":1}],"observacoes":""}""")]
    [InlineData("""{"rendimento":10,"unidadeRendimento":"Un","linhas":[{"nome":17,"quantidade":1,"unidade":"Un"}],"observacoes":""}""")]
    public async Task Propriedades_ausentes_ou_com_tipo_invalido_sao_rejeitadas(string json)
    {
        Responder(json);
        var acao = () => Criar().ExecuteAsync("Bolo", CancellationToken.None);
        await acao.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1000001")]
    [InlineData("1e999")]
    [InlineData("\"500\"")]
    public async Task Quantidade_ou_rendimento_invalidos_nao_passam_como_decimal(string valor)
    {
        foreach (var propriedade in new[] { "quantidade", "rendimento" })
        {
            Responder(ReceitaValida.Replace(propriedade == "quantidade" ? "\"quantidade\":500" : "\"rendimento\":10", $"\"{propriedade}\":{valor}", StringComparison.Ordinal));
            var acao = () => Criar().ExecuteAsync("Bolo", CancellationToken.None);
            await acao.Should().ThrowAsync<UseCaseValidationException>();
        }
    }

    [Theory]
    [InlineData("kg")]
    [InlineData("xicara")]
    [InlineData("")]
    public async Task Unidade_nao_suportada_nao_e_convertida_pelo_modelo(string unidade)
    {
        foreach (var valorOriginal in new[] { "\"G\"", "\"Un\"" })
        {
            Responder(ReceitaValida.Replace(valorOriginal, JsonSerializer.Serialize(unidade), StringComparison.Ordinal));
            var acao = () => Criar().ExecuteAsync("Bolo", CancellationToken.None);
            await acao.Should().ThrowAsync<UseCaseValidationException>();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelamento_na_pesquisa_ou_na_ia_nao_vira_fallback(bool naIa)
    {
        using var cts = new CancellationTokenSource();
        var iniciada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (naIa)
            _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(async info =>
            {
                iniciada.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, info.Arg<CancellationToken>());
                return Resposta(ReceitaValida);
            });
        else
            _pesquisa.BuscarReceitasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async info =>
            {
                iniciada.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, info.Arg<CancellationToken>());
                return (IReadOnlyList<FonteProducaoWeb>)[Fonte];
            });
        var tarefa = Criar().ExecuteAsync("Bolo", cts.Token);
        await iniciada.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        var acao = () => tarefa.WaitAsync(TimeSpan.FromSeconds(5));
        await acao.Should().ThrowAsync<OperationCanceledException>();
        if (!naIa) await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    private SugerirReceitaUseCase Criar() => new(_pesquisa, _llm);
    private void Responder(string json) => _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Resposta(json)));
    private static RespostaLlm Resposta(string json) => new(RespostaLlm.StopToolUse, [Uso(json)], 10, 20);
    private static BlocoUsoFerramentaLlm Uso(string json)
    {
        using var documento = JsonDocument.Parse(json);
        return new("tool_1", "sugerir_receita", documento.RootElement.Clone());
    }
}
