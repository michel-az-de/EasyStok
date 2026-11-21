using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>#2833: reagir e apagar para todos pelo console. O canal que não oferece a ação vira 409 com código próprio.</summary>
public partial class AtendimentoConversasControllerTests
{
    private ICanalComAcoesNaMensagem CanalComAcoes => (ICanalComAcoesNaMensagem)_canalComAcoes;

    private readonly ICanalMensageria _canalComAcoes = CriarCanalComAcoes();

    private static ICanalMensageria CriarCanalComAcoes()
    {
        var canal = Substitute.For<ICanalMensageria, ICanalComAcoesNaMensagem>();
        canal.Canal.Returns(CanalConversa.WhatsApp);
        return canal;
    }

    private AcoesNaMensagemUseCase AcoesUseCase() =>
        new(_repositorio, new ResolvedorCanal([_canalComAcoes]), _unitOfWork, TimeProvider.System);

    private (Conversa Conversa, Mensagem DaLoja) ConversaComMensagemDaLoja()
    {
        var conversa = ConversaComClienteAgora();
        var daLoja = Mensagem.Saida(_empresaId, conversa.Id, AutorMensagem.Dona, DateTime.UtcNow.AddMinutes(-2), TipoConteudoMensagem.Texto, "Mando o Pix?");
        daLoja.ConfirmarEnvio("wpp:s:true_5511999998888@c.us_A");
        _repositorio.Mensagens.Add(daLoja);
        return (conversa, daLoja);
    }

    [Fact]
    public async Task Reagir_devolve_a_mensagem_com_a_reacao()
    {
        var (conversa, daLoja) = ConversaComMensagemDaLoja();

        var result = await _controller.Reagir(conversa.Id, daLoja.Id, new ReagirMensagemBody("❤️"), AcoesUseCase(), default);

        Dados<MensagemAtendimentoResult>(result).Reacao.Should().Be("❤️");
        await CanalComAcoes.Received(1).ReagirAsync(WaId, "wpp:s:true_5511999998888@c.us_A", "❤️", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apagar_devolve_a_mensagem_marcada_como_apagada()
    {
        var (conversa, daLoja) = ConversaComMensagemDaLoja();

        var result = await _controller.ApagarMensagem(conversa.Id, daLoja.Id, AcoesUseCase(), default);

        Dados<MensagemAtendimentoResult>(result).ApagadaEm.Should().NotBeNull();
        daLoja.ApagadaPorUsuarioId.Should().Be(_usuarioId);
    }

    [Fact]
    public async Task Canal_que_nao_apaga_vira_409_com_codigo_proprio()
    {
        var (conversa, daLoja) = ConversaComMensagemDaLoja();
        CanalComAcoes.ApagarParaTodosAsync(default!, default!, default)
            .ThrowsAsyncForAnyArgs(new AcaoNaoSuportadaWhatsAppException("A Meta não apaga."));

        var result = await _controller.ApagarMensagem(conversa.Id, daLoja.Id, AcoesUseCase(), default);

        var conflito = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflito.Value.Should().BeOfType<ApiErrorResponse>().Which.Error.Code.Should().Be(AcaoNaoSuportadaWhatsAppException.Codigo);
        daLoja.ApagadaEm.Should().BeNull();
    }

    [Fact]
    public async Task Apagar_mensagem_do_cliente_vira_409_sem_chamar_o_canal()
    {
        var conversa = ConversaComClienteAgora();
        var doCliente = _repositorio.Mensagens.Single(m => m.ConversaId == conversa.Id);

        var result = await _controller.ApagarMensagem(conversa.Id, doCliente.Id, AcoesUseCase(), default);

        result.Should().BeOfType<ConflictObjectResult>();
        await CanalComAcoes.DidNotReceiveWithAnyArgs().ApagarParaTodosAsync(default!, default!, default);
    }

    [Fact]
    public async Task Sem_permissao_de_atender_e_403()
    {
        var (conversa, daLoja) = ConversaComMensagemDaLoja();
        _currentUser.TemPermissao(EasyStock.Domain.Enums.Permissao.AtenderConversas).Returns(false);

        var result = await _controller.Reagir(conversa.Id, daLoja.Id, new ReagirMensagemBody("👍"), AcoesUseCase(), default);

        result.Should().BeOfType<ForbidResult>();
    }
}
