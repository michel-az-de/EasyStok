using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Grava cada saída do turno logo depois do envio (#1791), junto com as entradas que o turno levou ao LLM
/// marcadas como processadas: o status e o eco da Meta encontram a mensagem pelo wamid, e um commit final que
/// falhe não faz o próximo job responder de novo. Falha aqui não derruba o envio: o que ficou rastreado vai no
/// commit seguinte.
/// </summary>
internal sealed class GravacaoSaidaTurno(
    IConversaRepository conversaRepository, IUnitOfWork unitOfWork, ILogger logger, Guid empresaId, IReadOnlyList<Mensagem> historico, DateTime agora)
{
    private bool _entradasMarcadas;

    public Guid EmpresaId => empresaId;
    public DateTime Agora => agora;

    /// <summary>S67: a última resposta que o turno gravou, para o registro do turno.</summary>
    public Guid? UltimaSaidaId { get; set; }

    public async Task GravarAsync(CancellationToken ct)
    {
        try
        {
            await MarcarProcessadasAsync(ct);
            await unitOfWork.CommitAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Agente: falha ao gravar a saída já enviada; segue para o commit do fim do turno.");
        }
    }

    /// <summary>
    /// Marca no rastreamento, uma vez por turno, as entradas que foram ao LLM. O histórico vem sem tracking:
    /// a entidade rastreada é buscada pelo <c>wamid</c>.
    /// </summary>
    public async Task MarcarProcessadasAsync(CancellationToken ct)
    {
        if (_entradasMarcadas) return;
        foreach (var entrada in historico.Where(m => m.Autor == AutorMensagem.Cliente && m.ProcessadaEm is null))
        {
            if (entrada.ExternoId is not { } externoId) continue;
            var rastreada = await conversaRepository.ObterMensagemPorExternoIdAsync(empresaId, externoId, ct);
            rastreada?.MarcarProcessada(agora);
        }
        _entradasMarcadas = true;
    }
}
