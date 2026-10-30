using System.Text.Json;
using System.Reflection;
using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class MontagemFichaControllerTests
{
    [Fact]
    public void ApenasNovaMontagemExigeAPoliticaExistenteDeIa()
    {
        var tipo = typeof(AtendimentoAssistenteController);
        tipo.GetMethod(nameof(AtendimentoAssistenteController.MontarFicha))!
            .GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be("ai");
        tipo.GetMethod(nameof(AtendimentoAssistenteController.Perguntar))!
            .GetCustomAttribute<EnableRateLimitingAttribute>().Should().BeNull();
    }

    private readonly Guid _empresa = Guid.NewGuid();
    private readonly ICurrentUserAccessor _ator = Substitute.For<ICurrentUserAccessor>();
    private readonly IMontagemFichaLlmClient _llm = Substitute.For<IMontagemFichaLlmClient>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();

    private AtendimentoAssistenteController Controller() => new(
        new AssistenteDonaUseCase(_llm, _conversas, NullLogger<AssistenteDonaUseCase>.Instance), _ator);
    private MontagemFichaUseCase Caso() => new(_llm, _conversas,
        new ObterDossieClienteUseCase(Substitute.For<IClienteRepository>(), Substitute.For<IClienteCrmRepository>(),
            Substitute.For<IHistoricoPedidosClienteQueries>(), Substitute.For<IDomicilioQueries>(), _conversas),
        NullLogger<MontagemFichaUseCase>.Instance);
    private Conversa ConversaPropria()
    {
        _ator.EmpresaId.Returns(_empresa);
        var c = Conversa.Abrir(_empresa, "5511999998888", DateTime.UtcNow);
        _conversas.ObterPorIdAsync(_empresa, c.Id, Arg.Any<CancellationToken>()).Returns(c);
        _conversas.ObterComMensagensAsync(_empresa, c.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new ConversaComMensagens(c, []));
        return c;
    }

    [Fact]
    public async Task ActionUsaEmpresaDoJwtEEntregaAComposicaoTipada()
    {
        var conversa = ConversaPropria();
        _llm.Disponivel.Returns(true);
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(new RespostaLlm(RespostaLlm.StopToolUse,
            [new BlocoUsoFerramentaLlm("tool1", BlocosMontagemFicha.NomeFerramenta,
                JsonDocument.Parse("""{"blocos":[{"tipo":"resumo","texto":"Confira o pedido com a cliente."}]}""").RootElement.Clone())], 30, 10));
        var resultado = await Controller().MontarFicha(new(conversa.Id), Caso());
        var dados = resultado.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResponse<MontagemFichaResult>>().Subject.Data;
        dados.ConversaId.Should().Be(conversa.Id);
        dados.Blocos.Should().ContainSingle().Which.Tipo.Should().Be(TipoBlocoMontagemFicha.Resumo);
        await _conversas.Received(1).ObterComMensagensAsync(_empresa, conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _conversas.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
    }

    [Fact]
    public async Task DesligadoDevolve503SemInventarBlocos()
    {
        var conversa = ConversaPropria();
        _llm.Disponivel.Returns(false);
        var resultado = await Controller().MontarFicha(new(conversa.Id), Caso());
        var resposta = resultado.Should().BeOfType<ObjectResult>().Subject;
        resposta.StatusCode.Should().Be(503);
        resposta.Value.Should().BeOfType<ApiErrorResponse>().Which.Error.Code.Should().Be("FICHA_IA_INDISPONIVEL");
    }

    [Fact]
    public async Task ConversaDeOutraEmpresaRetorna404SemChamarModelo()
    {
        _ator.EmpresaId.Returns(_empresa);
        var resultado = await Controller().MontarFicha(new(Guid.NewGuid()), Caso());
        resultado.Should().BeOfType<NotFoundObjectResult>();
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    [Fact]
    public async Task JwtSemEmpresaRetorna400SemConsultarDados()
    {
        _ator.EmpresaId.Returns(Guid.Empty);
        var resultado = await Controller().MontarFicha(new(Guid.NewGuid()), Caso());
        resultado.Should().BeOfType<BadRequestObjectResult>();
        await _conversas.DidNotReceiveWithAnyArgs().ObterComMensagensAsync(default, default, default, default);
    }
}
