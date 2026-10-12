using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.Services.Notifications;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

/// <summary>
/// Aviso externo da impressão travada (N11). O <c>ImpressaoPendenteAlertaJob</c> repete o SSE a cada rodada
/// enquanto o canhoto estiver pendente; o e-mail e o WhatsApp saem uma vez só. Liga o tenant da impressão
/// (gravar o evento de outra empresa sem ele é recusado pela RLS), pré-checa o <c>CorrelationId</c>
/// determinístico <c>prazo:impressao_travada:{id:N}</c> e enfileira na unidade de trabalho do escopo.
/// </summary>
public sealed class NotificarImpressaoTravadaUseCase(
    ITenantContextAccessor tenantContext,
    IEventoNotificacaoRepository eventos,
    INotificadorService notificador,
    IUnitOfWork unitOfWork,
    IOptions<PrazosOptions> prazos,
    TimeProvider relogio)
{
    /// <summary>True quando enfileirou; false com o interruptor desligado ou quando esta impressão já foi avisada.</summary>
    public async Task<bool> ExecuteAsync(ImpressaoAtrasada impressao, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(impressao);
        UseCaseGuards.EnsureEmpresaId(impressao.EmpresaId);
        var opcoes = prazos.Value;
        if (!opcoes.Habilitado) return false;

        tenantContext.SetCurrentTenant(impressao.EmpresaId);
        var correlationId = PrazoEstouradoEvento.CorrelationId(TipoPrazo.ImpressaoTravada, impressao.ImpressaoId);
        if (await eventos.ExisteCorrelacaoAsync(impressao.EmpresaId, correlationId, ct)) return false;

        var parada = relogio.GetUtcNow().UtcDateTime - impressao.CriadaEm;
        await PrazoEstouradoEvento.EnfileirarAsync(notificador, opcoes, TipoPrazo.ImpressaoTravada,
            impressao.EmpresaId, impressao.ImpressaoId, PrazoEstouradoEvento.Referencia(impressao.ImpressaoId),
            PrazoEstouradoEvento.Duracao(opcoes.ImpressaoPendente), PrazoEstouradoEvento.Duracao(parada), usuarioId: null, ct);
        await unitOfWork.CommitAsync();
        return true;
    }
}
