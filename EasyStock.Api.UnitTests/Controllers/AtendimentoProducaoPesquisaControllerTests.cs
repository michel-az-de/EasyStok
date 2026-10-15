using System.Reflection;
using System.Text.Json;
using EasyStock.Api.Authorization;
using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class AtendimentoProducaoPesquisaControllerTests
{
    private readonly IPesquisaProducaoWeb _pesquisa = Substitute.For<IPesquisaProducaoWeb>();
    private readonly IAgenteLlmClient _llm = Substitute.For<IAgenteLlmClient>();
    private readonly ICurrentUserAccessor _usuario = Substitute.For<ICurrentUserAccessor>();

    public AtendimentoProducaoPesquisaControllerTests()
    {
        _usuario.TemPermissao(Permissao.GerenciarEstoque).Returns(true);
        _llm.Disponivel.Returns(true);
        _pesquisa.BuscarReceitasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<FonteProducaoWeb>>(
            [new("Livro de receitas/Bolo", "https://pt.wikibooks.org/wiki/Livro_de_receitas/Bolo", "Farinha", DateTimeOffset.UtcNow)]));
    }

    [Fact]
    public void Pesquisa_e_sugestao_exigem_gerente_modulo_producao_e_limiter_ai()
    {
        var tipo = typeof(AtendimentoProducaoPesquisaController);
        tipo.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Select(a => a.Policy).Should().Equal("Gerente");
        tipo.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be("ai");
        foreach (var acao in new[] { nameof(AtendimentoProducaoPesquisaController.Pesquisar), nameof(AtendimentoProducaoPesquisaController.Sugerir) })
            ModulosConvention.ModulosDe(tipo, acao).Should().Equal(Modulo.Producao);
    }

    [Fact]
    public async Task Sem_permissao_de_estoque_bloqueia_ambas_as_rotas_antes_do_io()
    {
        _usuario.TemPermissao(Permissao.GerenciarEstoque).Returns(false);
        var controller = Criar();
        (await controller.Pesquisar("Ovo", CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await controller.Sugerir(new("Bolo"), CancellationToken.None)).Should().BeOfType<ForbidResult>();
        await _pesquisa.DidNotReceiveWithAnyArgs().BuscarInsumosAsync(default!, default);
        await _pesquisa.DidNotReceiveWithAnyArgs().BuscarReceitasAsync(default!, default);
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Termo_invalido_retorna_400_sem_chamar_fontes(bool sugestao)
    {
        var controller = Criar();
        var resultado = sugestao ? await controller.Sugerir(new("ab"), CancellationToken.None) : await controller.Pesquisar("ab", CancellationToken.None);
        resultado.Should().BeOfType<BadRequestObjectResult>();
        await _pesquisa.DidNotReceiveWithAnyArgs().BuscarInsumosAsync(default!, default);
        await _pesquisa.DidNotReceiveWithAnyArgs().BuscarReceitasAsync(default!, default);
    }

    [Theory]
    [InlineData(false, "http")]
    [InlineData(false, "json")]
    [InlineData(false, "timeout")]
    [InlineData(true, "http")]
    [InlineData(true, "json")]
    [InlineData(true, "timeout")]
    public async Task Fonte_indisponivel_retorna_503_sem_expor_erro_interno(bool sugestao, string falha)
    {
        Exception erro = falha switch
        {
            "json" => new JsonException("detalhe-interno"),
            "timeout" => new OperationCanceledException("detalhe-interno"),
            _ => new HttpRequestException("detalhe-interno")
        };
        _pesquisa.BuscarInsumosAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<FonteProducaoWeb>>(erro));
        _pesquisa.BuscarReceitasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<FonteProducaoWeb>>(erro));
        var controller = Criar();
        var resultado = sugestao ? await controller.Sugerir(new("Bolo"), CancellationToken.None) : await controller.Pesquisar("Ovo", CancellationToken.None);
        var objeto = resultado.Should().BeOfType<ObjectResult>().Subject;
        objeto.StatusCode.Should().Be(503);
        JsonSerializer.Serialize(objeto.Value).Should().NotContain("detalhe-interno");
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    [Fact]
    public async Task Provedor_ia_com_erro_http_retorna_503_sem_fallback()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<RespostaLlm>(new HttpRequestException("segredo-interno")));
        var resultado = (await Criar().Sugerir(new("Bolo"), CancellationToken.None)).Should().BeOfType<ObjectResult>().Subject;
        resultado.StatusCode.Should().Be(503);
        JsonSerializer.Serialize(resultado.Value).Should().NotContain("segredo-interno");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelamento_do_chamador_nao_e_convertido_em_503(bool sugestao)
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _pesquisa.BuscarInsumosAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(info => Task.FromCanceled<IReadOnlyList<FonteProducaoWeb>>(info.Arg<CancellationToken>()));
        _pesquisa.BuscarReceitasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(info => Task.FromCanceled<IReadOnlyList<FonteProducaoWeb>>(info.Arg<CancellationToken>()));
        var controller = Criar();
        var acao = () => sugestao ? controller.Sugerir(new("Bolo"), cts.Token) : controller.Pesquisar("Ovo", cts.Token);
        await acao.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Pesquisa_retorna_fontes_http_no_envelope_da_api()
    {
        IReadOnlyList<FonteProducaoWeb> fontes = [new("Ovo", "https://pt.wikipedia.org/wiki/Ovo", "", DateTimeOffset.UtcNow)];
        _pesquisa.BuscarInsumosAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(fontes));
        var resultado = (await Criar().Pesquisar("Ovo", CancellationToken.None)).Should().BeOfType<OkObjectResult>().Subject;
        resultado.Value.Should().BeOfType<ApiResponse<IReadOnlyList<FonteProducaoWeb>>>().Which.Data.Should().Equal(fontes);
    }

    private AtendimentoProducaoPesquisaController Criar() => new(_pesquisa, new(_pesquisa, _llm), _usuario);
}
