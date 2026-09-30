using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>Assistente da dona (S47): contrato HTTP. O LLM é substituto; nenhuma chamada de rede.</summary>
public class AtendimentoAssistenteControllerTests
{
    private readonly IAgenteLlmClient _llm = Substitute.For<IAgenteLlmClient>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private AtendimentoAssistenteController Criar()
    {
        _currentUser.EmpresaId.Returns(Guid.NewGuid());
        return new AtendimentoAssistenteController(
            new AssistenteDonaUseCase(_llm, Substitute.For<IConversaRepository>(), NullLogger<AssistenteDonaUseCase>.Instance),
            _currentUser);
    }

    [Fact]
    public async Task SemChave_Devolve503ComMensagemClara()
    {
        _llm.Disponivel.Returns(false);

        var result = await Criar().Perguntar(new PerguntarAssistenteBody("Posso cobrar taxa de entrega?", null));

        var objeto = result.Should().BeOfType<ObjectResult>().Subject;
        objeto.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        objeto.Value.Should().BeOfType<ApiErrorResponse>()
            .Which.Error.Message.Should().Contain("Anthropic:ApiKey");
    }

    [Fact]
    public async Task PerguntaVazia_Devolve400()
    {
        _llm.Disponivel.Returns(true);

        var result = await Criar().Perguntar(new PerguntarAssistenteBody("  ", null));

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
