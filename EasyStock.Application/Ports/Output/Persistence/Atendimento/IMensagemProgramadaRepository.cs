using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Mensagens programadas ao cliente (S39). Consultas do console com <c>EmpresaId</c> no WHERE (ADR-0010).</summary>
public interface IMensagemProgramadaRepository
{
    Task AddAsync(MensagemProgramada mensagem, CancellationToken ct = default);

    /// <summary>Rastreado.</summary>
    Task<MensagemProgramada?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task<MensagemProgramada?> ObterComLockAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<MensagemProgramada>> ListarAsync(
        Guid empresaId, Guid? clienteId, SituacaoMensagemProgramada? situacao, int limite, CancellationToken ct = default);

    /// <summary>
    /// Disparador (cross-tenant, com bypass de RLS): agendadas vencidas, travadas com
    /// <c>FOR UPDATE SKIP LOCKED</c>. Tem que rodar dentro de transação; quem chama muda a situação
    /// para <see cref="SituacaoMensagemProgramada.Enviando"/> antes do commit, e é isso que impede
    /// dois disparadores de mandarem a mesma mensagem.
    /// </summary>
    Task<IReadOnlyList<MensagemProgramada>> ListarVencidasComLockAsync(DateTime agoraUtc, int limite, CancellationToken ct = default);
}
