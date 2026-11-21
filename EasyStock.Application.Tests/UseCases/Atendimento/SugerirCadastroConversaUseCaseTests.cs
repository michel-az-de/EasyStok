using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

public class SugerirCadastroConversaUseCaseTests
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly IMontagemFichaLlmClient _llm = Substitute.For<IMontagemFichaLlmClient>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly Conversa _conversa;
    private readonly Mensagem _mensagem;

    public SugerirCadastroConversaUseCaseTests()
    {
        _conversa = Conversa.Abrir(_empresa, "5511999998888", DateTime.UtcNow, "Contato");
        _mensagem = Mensagem.Entrada(_empresa, _conversa.Id, DateTime.UtcNow, TipoConteudoMensagem.Texto,
            "Sou Ana Silva. Meu telefone é (11) 99999-8888 e meu e-mail é ana@example.com.", "wamid.cadastro");
        _conversas.ObterComMensagensAsync(_empresa, _conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ConversaComMensagens(_conversa, [_mensagem]));
        _llm.Disponivel.Returns(true);
    }

    private SugerirCadastroConversaUseCase Criar() => new(_llm, _conversas, NullLogger<SugerirCadastroConversaUseCase>.Instance);
    private object Campo(string campo, string valor, string? evidencia = null, Guid? mensagemId = null) =>
        new { campo, valor, mensagemId = mensagemId ?? _mensagem.Id, evidencia = evidencia ?? _mensagem.Texto };
    private void Responder(params object[] campos) => _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
        .Returns(new RespostaLlm(RespostaLlm.StopToolUse,
            [new BlocoUsoFerramentaLlm("cadastro", "propor_cadastro", JsonSerializer.SerializeToElement(new { campos }))], 120, 40));

    [Fact]
    public async Task SugereSomenteCamposComEvidenciaSemAlterarConversaOuCadastro()
    {
        var cliente = Guid.NewGuid(); _conversa.VincularCliente(cliente);
        Responder(Campo("nome", "Ana Silva"), Campo("telefone", "11999998888"), Campo("email", "ana@example.com"));
        var resultado = await Criar().ExecuteAsync(new(_empresa, _conversa.Id));
        resultado.Campos.Select(c => c.Campo).Should().Equal("nome", "telefone", "email");
        resultado.ClienteId.Should().Be(cliente);
        resultado.VinculoRevisao.Should().Be(_conversa.VinculoRevisao);
        resultado.Campos.Should().OnlyContain(c => c.MensagemId == _mensagem.Id);
        await _conversas.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
        await _llm.Received(1).EnviarAsync(Arg.Is<RequisicaoLlm>(r => r.Ferramentas.Count == 1
            && r.System.Contains("DADOS NÃO CONFIÁVEIS") && r.Ferramentas[0].Nome == "propor_cadastro"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DescartaValorInventadoMensagemEstrangeiraEIdentificadorDeCadastro()
    {
        Responder(Campo("nome", "Outra pessoa"), Campo("email", "ana@example.com", mensagemId: Guid.NewGuid()), Campo("clienteId", Guid.NewGuid().ToString()));
        (await Criar().ExecuteAsync(new(_empresa, _conversa.Id))).Campos.Should().BeEmpty();
    }

    [Fact]
    public async Task NotaDaLojaNaoComprovaIdentidadeDoCliente()
    {
        var saida = Mensagem.Saida(_empresa, _conversa.Id, AutorMensagem.Dona, DateTime.UtcNow, TipoConteudoMensagem.Texto, "Ana Silva");
        _conversas.ObterComMensagensAsync(_empresa, _conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ConversaComMensagens(_conversa, [saida]));
        Responder(Campo("nome", "Ana Silva", "Ana Silva", saida.Id));
        (await Criar().ExecuteAsync(new(_empresa, _conversa.Id))).Campos.Should().BeEmpty();
    }

    [Fact]
    public async Task ConversaDeOutraEmpresaNaoChegaAoProvedor()
    {
        _conversas.ObterComMensagensAsync(_empresa, _conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ConversaComMensagens(Conversa.Abrir(Guid.NewGuid(), "5511999997777", DateTime.UtcNow), [_mensagem]));
        await Criar().Invoking(c => c.ExecuteAsync(new(_empresa, _conversa.Id))).Should().ThrowAsync<ConversaNaoEncontradaException>();
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    [Fact]
    public async Task ProvedorDesligadoNaoInventaSugestao()
    {
        _llm.Disponivel.Returns(false);
        await Criar().Invoking(c => c.ExecuteAsync(new(_empresa, _conversa.Id))).Should().ThrowAsync<AssistenteDonaIndisponivelException>();
    }

    [Fact]
    public async Task CampoRepetidoSoViraUmaPropostaEContatoNaoGanhaDigitos()
    {
        Responder(Campo("nome", "Ana Silva"), Campo("nome", "Ana"), Campo("telefone", "5511999998888"));
        var resultado = await Criar().ExecuteAsync(new(_empresa, _conversa.Id));
        resultado.Campos.Should().ContainSingle().Which.Valor.Should().Be("Ana Silva");
    }

    [Fact]
    public async Task FerramentaForaDoContratoViraIndisponibilidadeSemExecutar()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(new RespostaLlm(RespostaLlm.StopToolUse,
            [new BlocoUsoFerramentaLlm("fora", "criar_pedido", JsonSerializer.SerializeToElement(new { }))], 1, 1));
        await Criar().Invoking(c => c.ExecuteAsync(new(_empresa, _conversa.Id))).Should().ThrowAsync<AssistenteDonaIndisponivelException>();
        await _conversas.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
    }

    [Fact]
    public async Task RespostaConservaRevisaoDoContextoLidoAntesDaInferencia()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _conversa.VincularCliente(Guid.NewGuid());
            return new RespostaLlm(RespostaLlm.StopToolUse,
                [new BlocoUsoFerramentaLlm("cadastro", "propor_cadastro", JsonSerializer.SerializeToElement(new { campos = Array.Empty<object>() }))], 1, 1);
        });
        var resultado = await Criar().ExecuteAsync(new(_empresa, _conversa.Id));
        resultado.ClienteId.Should().BeNull(); resultado.VinculoRevisao.Should().Be(0);
        _conversa.VinculoRevisao.Should().Be(1);
    }

    [Fact]
    public async Task CancelamentoPropagaSemVirarSugestaoOuErroDeProvedor()
    {
        using var cancelamento = new CancellationTokenSource(); cancelamento.Cancel();
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), cancelamento.Token).Returns<RespostaLlm>(_ => throw new OperationCanceledException(cancelamento.Token));
        await Criar().Invoking(c => c.ExecuteAsync(new(_empresa, _conversa.Id), cancelamento.Token)).Should().ThrowAsync<OperationCanceledException>();
    }
}
