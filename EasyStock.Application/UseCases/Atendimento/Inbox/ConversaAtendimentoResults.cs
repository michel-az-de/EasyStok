using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

/// <summary>Linha da inbox do console (S07).</summary>
/// <param name="MotivoEscalada">Por que o agente passou para humano (#1238); nulo depois que alguém assume.</param>
public sealed record ConversaResumoResult(
    Guid Id,
    CanalConversa Canal,
    string ContatoIdExterno,
    string? ContatoNome,
    Guid? ClienteId,
    SituacaoConversa Situacao,
    int NaoLidas,
    DateTime UltimaMensagemEm,
    string? UltimaMensagemTexto,
    Guid? PedidoEmAndamentoId,
    Guid? AssumidaPorUsuarioId,
    bool DentroDaJanela,
    string? MotivoEscalada = null)
{
    internal static ConversaResumoResult De(Conversa c, string? ultimaMensagemTexto, DateTime agora) => new(
        c.Id, c.Canal, c.ContatoIdExterno, c.ContatoNome, c.ClienteId, c.Situacao, c.NaoLidas,
        c.UltimaMensagemEm, ultimaMensagemTexto, c.PedidoEmAndamentoId, c.AssumidaPorUsuarioId, c.DentroDaJanela(agora),
        c.MotivoEscalada);
}

/// <summary>Mensagem como o console mostra. <c>MidiaChave</c> é interna: o arquivo é servido por endpoint autenticado.</summary>
public sealed record MensagemAtendimentoResult(
    Guid Id,
    DirecaoMensagem Direcao,
    AutorMensagem Autor,
    TipoConteudoMensagem TipoConteudo,
    string? Texto,
    string? BotaoId,
    string? MidiaChave,
    string? MidiaMime,
    StatusMensagem Status,
    string? Erro,
    string? ExternoId,
    DateTime EnviadaEm,
    Guid? EnviadaPorUsuarioId,
    DateTime? AguardaClienteDesde = null,
    DateTime? ReservaSmsEm = null,
    string? ErroMidia = null,
    bool MidiaFalhou = false,
    string? Transcricao = null,
    bool Programada = false)
{
    internal static MensagemAtendimentoResult De(Mensagem m) => new(
        m.Id, m.Direcao, m.Autor, m.TipoConteudo, m.Texto, m.BotaoId, m.MidiaChave, m.MidiaMime,
        m.Status, m.Erro, m.ExternoId, m.EnviadaEm, m.EnviadaPorUsuarioId, m.AguardaClienteDesde, m.ReservaSmsEm,
        m.ErroMidia, m.MidiaFalhou, m.Transcricao, m.Programada);
}

/// <summary>Estado da conversa depois de uma ação do console.</summary>
public sealed record ConversaSituacaoResult(Guid Id, SituacaoConversa Situacao, Guid? AssumidaPorUsuarioId, int NaoLidas)
{
    internal static ConversaSituacaoResult De(Conversa c) => new(c.Id, c.Situacao, c.AssumidaPorUsuarioId, c.NaoLidas);
}

/// <summary>Conversa inexistente ou de outra empresa: 404, sem vazar existência.</summary>
public sealed class ConversaNaoEncontradaException(Guid conversaId)
    : Exception($"Conversa {conversaId} não encontrada.")
{
    public Guid ConversaId { get; } = conversaId;
}

/// <summary>
/// Texto livre fora da janela de atendimento do canal (24 h no WhatsApp): o domínio recusou ou a Meta
/// devolveu 131047. Nada é gravado como enviado; o caminho é um modelo aprovado.
/// </summary>
public sealed class ForaDaJanelaAtendimentoException(string mensagem, Exception? inner = null)
    : Exception(mensagem, inner)
{
    public const string Codigo = "fora_da_janela_24h";
    public const string Sugestao = "template";
}

/// <summary>
/// O canal recusou ou falhou no envio por outro motivo (rede, token, erro da Meta). <see cref="Mensagem"/> é a
/// mensagem gravada como falhou (#1396), com o id que o console usa para reenviar.
/// </summary>
public sealed class FalhaEnvioCanalException(string mensagem, Exception inner, MensagemAtendimentoResult? gravada = null)
    : Exception(mensagem, inner)
{
    public MensagemAtendimentoResult? Mensagem { get; } = gravada;
}
