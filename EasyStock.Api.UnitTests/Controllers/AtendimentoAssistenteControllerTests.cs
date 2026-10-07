using System.Text.Json;
using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
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
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly Guid _empresaId = Guid.NewGuid();

    private AtendimentoAssistenteController Criar()
    {
        _currentUser.EmpresaId.Returns(_empresaId);
        return new AtendimentoAssistenteController(
            new AssistenteDonaUseCase(_llm, _conversas, NullLogger<AssistenteDonaUseCase>.Instance),
            _currentUser);
    }

    [Fact]
    public async Task ComConversa_DevolveAcoesPropostasSemEnviar()
    {
        // #1445: "manda o cardápio" volta como ação proposta; o envio é do clique no console.
        _llm.Disponivel.Returns(true);
        var conversa = Conversa.Abrir(_empresaId, "5511999998888", DateTime.UtcNow, "Maria");
        _conversas.ObterComMensagensAsync(_empresaId, conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ConversaComMensagens(conversa, []));
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(new RespostaLlm(
            RespostaLlm.StopToolUse,
            [new BlocoUsoFerramentaLlm("toolu_1", "propor_envio_cardapio", JsonDocument.Parse("{}").RootElement.Clone())],
            100, 10));

        var result = await Criar().Perguntar(new PerguntarAssistenteBody("manda o cardápio", conversa.Id));

        var dados = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should()
            .BeOfType<ApiResponse<RespostaAssistenteDonaResult>>().Subject.Data;
        dados.Acoes.Should().ContainSingle().Which.Tipo.Should().Be(AcaoPropostaAssistente.EnviarCardapio);
        await _conversas.DidNotReceive().AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>());
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
