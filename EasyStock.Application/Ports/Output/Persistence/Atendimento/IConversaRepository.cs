using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Conversa com as ultimas N mensagens em ordem cronologica (a mais nova por ultimo).</summary>
public sealed record ConversaComMensagens(Conversa Conversa, IReadOnlyList<Mensagem> Mensagens);

/// <summary>
/// Persistencia do agregado Conversa/Mensagem (S04, ADR-0050). Toda consulta recebe o
/// <c>empresaId</c> e o poe no WHERE alem do filtro global e do RLS (ADR-0010, defesa em
/// profundidade). O commit e do <see cref="IUnitOfWork"/>; aqui so se enfileira.
/// </summary>
public interface IConversaRepository
{
    Task<Conversa?> ObterPorIdAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    /// <summary>Conversa nao encerrada do contato. O <paramref name="contatoWaId"/> e normalizado para digitos.</summary>
    Task<Conversa?> ObterAbertaPorContatoAsync(Guid empresaId, string contatoWaId, CancellationToken ct = default);

    Task<ConversaComMensagens?> ObterComMensagensAsync(Guid empresaId, Guid id, int ultimasN, CancellationToken ct = default);

    /// <summary>Mais recentes primeiro (por <c>UltimaMensagemEm</c>), paginado.</summary>
    Task<IReadOnlyList<Conversa>> ListarAsync(
        Guid empresaId,
        SituacaoConversa? situacao,
        int pagina,
        int tamanhoPagina,
        CancellationToken ct = default);

    Task<IReadOnlyList<Conversa>> ListarPorClienteAsync(Guid empresaId, Guid clienteId, int max = 5, CancellationToken ct = default);

    /// <summary>Lookup pelo <c>wamid</c> (idempotencia do webhook e callbacks de status).</summary>
    Task<Mensagem?> ObterMensagemPorExternoIdAsync(Guid empresaId, string externoId, CancellationToken ct = default);

    Task AddAsync(Conversa conversa, CancellationToken ct = default);

    Task AddMensagemAsync(Mensagem mensagem, CancellationToken ct = default);
}
