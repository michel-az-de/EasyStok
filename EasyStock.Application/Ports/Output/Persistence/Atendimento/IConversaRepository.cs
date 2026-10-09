using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Conversa com as ultimas N mensagens em ordem cronologica (a mais nova por ultimo).</summary>
public sealed record ConversaComMensagens(Conversa Conversa, IReadOnlyList<Mensagem> Mensagens);

/// <summary>Linha da inbox do console (S07): a conversa e o texto da ultima mensagem, quando houver.</summary>
public sealed record ConversaInboxItem(Conversa Conversa, string? UltimaMensagemTexto, bool? AguardaResposta = null);

/// <summary>Mensagem que não chegou ao cliente, com a conversa dela (S59).</summary>
public sealed record MensagemNaoEntregue(Conversa Conversa, Mensagem Mensagem);

/// <summary>Filtro por responsavel da inbox (S41): <c>UsuarioId</c> nulo = conversas sem ninguem.</summary>
public sealed record FiltroResponsavel(Guid? UsuarioId)
{
    public static readonly FiltroResponsavel Ninguem = new((Guid?)null);
}

/// <summary>
/// Persistencia do agregado Conversa/Mensagem (S04, ADR-0050). Toda consulta recebe o
/// <c>empresaId</c> e o poe no WHERE alem do filtro global e do RLS (ADR-0010, defesa em
/// profundidade). O commit e do <see cref="IUnitOfWork"/>; aqui so se enfileira.
/// </summary>
public interface IConversaRepository
{
    Task<Guid?> ObterIdPorPedidoAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default);

    Task<Conversa?> ObterAbertaPorClienteNoCanalAsync(Guid empresaId, Guid clienteId, CanalConversa canal, CancellationToken ct = default);

    Task TravarContatoAsync(Guid empresaId, string contato, CancellationToken ct = default);

    Task<Conversa?> ObterPorIdAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Trava a linha da conversa (<c>SELECT ... FOR UPDATE</c>) e devolve o <c>PedidoEmAndamentoId</c>
    /// lido do banco nesse instante, não o da entidade rastreada (#1238). Exige transação explícita
    /// aberta: o lock dura até o commit e serializa a geração de pedido da mesma conversa.
    /// </summary>
    Task<Guid?> TravarParaPedidoAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Situacao gravada no banco, sem tracking (nulo se a conversa nao existe). O turno do agente rele
    /// antes de enviar: a dona pode ter assumido enquanto o LLM respondia (#1288).
    /// </summary>
    Task<SituacaoConversa?> ObterSituacaoAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    /// <summary>Conversa nao encerrada do contato no canal. O <paramref name="contatoIdExterno"/> e normalizado conforme o canal.</summary>
    Task<Conversa?> ObterAbertaPorContatoAsync(Guid empresaId, CanalConversa canal, string contatoIdExterno, CancellationToken ct = default);

    Task<ConversaComMensagens?> ObterComMensagensAsync(Guid empresaId, Guid id, int ultimasN, CancellationToken ct = default);

    /// <summary>Mais recentes primeiro (por <c>UltimaMensagemEm</c>), paginado.</summary>
    Task<IReadOnlyList<Conversa>> ListarAsync(
        Guid empresaId,
        SituacaoConversa? situacao,
        int pagina,
        int tamanhoPagina,
        CancellationToken ct = default);

    /// <summary>
    /// Inbox do console (S07): mais recentes primeiro, com o texto da ultima mensagem.
    /// <paramref name="busca"/> filtra por nome ou identificador do contato (sem diferenciar caixa).
    /// <paramref name="responsavel"/> nulo nao filtra (S41).
    /// </summary>
    Task<IReadOnlyList<ConversaInboxItem>> ListarInboxAsync(
        Guid empresaId,
        SituacaoConversa? situacao,
        string? busca,
        FiltroResponsavel? responsavel,
        int pagina,
        int tamanhoPagina,
        CancellationToken ct = default);

    /// <summary>
    /// Historico paginado para tras (S07): as <paramref name="limite"/> mensagens anteriores a
    /// <paramref name="antesDe"/> (ou as ultimas, sem cursor), em ordem cronologica.
    /// </summary>
    Task<IReadOnlyList<Mensagem>> ListarMensagensAsync(
        Guid empresaId,
        Guid conversaId,
        DateTime? antesDe,
        int limite,
        CancellationToken ct = default);

    /// <summary>Mensagens depois de <paramref name="depoisDe"/> (ou as primeiras, sem cursor), em ordem cronologica (S36).</summary>
    Task<IReadOnlyList<Mensagem>> ListarMensagensDepoisAsync(
        Guid empresaId,
        Guid conversaId,
        DateTime? depoisDe,
        int limite,
        CancellationToken ct = default);

    Task<IReadOnlyList<Conversa>> ListarPorClienteAsync(Guid empresaId, Guid clienteId, int max = 5, CancellationToken ct = default);

    /// <summary>Lookup pelo <c>wamid</c> (idempotencia do webhook e callbacks de status).</summary>
    Task<Mensagem?> ObterMensagemPorExternoIdAsync(Guid empresaId, string externoId, CancellationToken ct = default);

    /// <summary>Mensagem da conversa na empresa (mídia do console, #1287); de outra empresa ou conversa, nula.</summary>
    Task<Mensagem?> ObterMensagemAsync(Guid empresaId, Guid conversaId, Guid mensagemId, CancellationToken ct = default);

    /// <summary>Mesma busca de <see cref="ObterMensagemAsync"/>, rastreada para alterar (reenvio, S57).</summary>
    Task<Mensagem?> ObterMensagemParaAlterarAsync(Guid empresaId, Guid conversaId, Guid mensagemId, CancellationToken ct = default);

    /// <summary>
    /// S57: trava (<c>FOR UPDATE SKIP LOCKED</c>) até <paramref name="limite"/> mensagens com reenvio vencido, de
    /// todas as empresas. Exige bypass de RLS e transação explícita.
    /// </summary>
    Task<IReadOnlyList<Mensagem>> ListarReenviosVencidosComLockAsync(DateTime agora, int limite, CancellationToken ct = default);

    /// <summary>
    /// #1397: trava (<c>FOR UPDATE SKIP LOCKED</c>) até <paramref name="limite"/> mensagens com anexo ainda não
    /// baixado e tentativa vencida, de todas as empresas. Exige bypass de RLS e transação explícita.
    /// </summary>
    Task<IReadOnlyList<Mensagem>> ListarMidiasPendentesComLockAsync(DateTime agora, int limite, CancellationToken ct = default);

    /// <summary>
    /// S58: há mensagem esperando o cliente responder ao modelo de retomada desde <paramref name="desde"/>, do mesmo
    /// contato no canal ou do mesmo cliente (o WhatsApp grava o celular com e sem o nono dígito).
    /// </summary>
    Task<bool> ExisteAguardandoClienteAsync(
        Guid empresaId, CanalConversa canal, string contatoIdExterno, Guid? clienteId, DateTime desde, CancellationToken ct = default);

    /// <summary>
    /// S58: trava (<c>FOR UPDATE SKIP LOCKED</c>) mensagens que esperam o cliente e cujo contato (ou cliente) já
    /// escreveu depois da espera, de todas as empresas. Exige bypass de RLS e transação explícita.
    /// </summary>
    Task<IReadOnlyList<Mensagem>> ListarAguardandoComRespostaComLockAsync(int limite, CancellationToken ct = default);

    /// <summary>S59: mensagens de saída que falharam, mais recentes primeiro, com a conversa.</summary>
    Task<IReadOnlyList<MensagemNaoEntregue>> ListarNaoEntreguesAsync(Guid empresaId, int limite, CancellationToken ct = default);

    Task AddAsync(Conversa conversa, CancellationToken ct = default);

    Task AddMensagemAsync(Mensagem mensagem, CancellationToken ct = default);
}
