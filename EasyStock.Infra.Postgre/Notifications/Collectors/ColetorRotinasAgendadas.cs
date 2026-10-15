using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Notifications.Collectors;

/// <summary>
/// Rotinas agendadas por horário diário local (N12). A cada rodada do coletor (5 min, no loop do motor) lista as rotinas
/// <b>da empresa</b> ativas com <c>ParametrosJson.agenda.horario</c> e, para cada uma devida e ainda sem evento do dia, monta o
/// payload com o <see cref="IConstrutorPayloadAgendado"/> do tipo e enfileira o evento com
/// <c>CorrelationId = agenda:{rotinaId}:{yyyyMMdd}</c>. O motor é genérico: cada tipo agendado novo só acrescenta um construtor.
/// <para>
/// "Última execução persistida" é o próprio evento do dia: pré-checagem por <c>(EmpresaId, CorrelationId)</c> antes da parte
/// cara (o payload) e índice único como trava final contra dois hosts (23505 é lido como "outro host já fez"). Reiniciar o
/// processo não perde o dia, e o catch-up vale só dentro do mesmo dia local (<see cref="AgendaDiariaLocal"/>).
/// </para>
/// <para>
/// A listagem é a única leitura entre tenants: roda sob <see cref="IRowLevelSecurityBypass"/> numa leitura curta. Depois disso
/// cada rotina abre um escopo de DI próprio com o tenant da empresa ligado antes da primeira conexão, e um erro numa empresa
/// não derruba as outras. Rotina global com <c>agenda</c> é ignorada (o resumo é opt-in por empresa).
/// </para>
/// </summary>
public sealed class ColetorRotinasAgendadas(
    IServiceScopeFactory scopeFactory,
    IRowLevelSecurityBypass bypassRls,
    IRotinaRepository rotinaRepo,
    TimeProvider relogio,
    ILogger<ColetorRotinasAgendadas> logger) : IColetorEventoNotificacao
{
    public async Task ColetarAsync(CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;

        IReadOnlyList<RotinaNotificacao> ativas;
        using (bypassRls.Begin())
            ativas = await rotinaRepo.ListarAtivasAsync(ct: ct);

        var dia = AgendaDiariaLocal.DiaLocal(agora);
        foreach (var rotina in ativas)
        {
            ct.ThrowIfCancellationRequested();
            if (!AgendaDiariaLocal.TemAgenda(rotina.ParametrosJson)) continue;

            if (rotina.EmpresaId is not { } empresaId)
            {
                logger.LogWarning(
                    "Rotina global {Codigo} tem agenda e foi ignorada: o agendamento vale só para rotina da empresa.", rotina.Codigo);
                continue;
            }

            if (AgendaDiariaLocal.HorarioDosParametros(rotina.ParametrosJson) is not { } horario)
            {
                logger.LogWarning("Rotina {Codigo} da empresa {EmpresaId} tem agenda.horario inválido e foi ignorada.", rotina.Codigo, empresaId);
                continue;
            }

            if (!AgendaDiariaLocal.Devida(horario, agora)) continue;

            await GerarDoDiaAsync(rotina, empresaId, dia, ct);
        }
    }

    private async Task GerarDoDiaAsync(RotinaNotificacao rotina, Guid empresaId, DateOnly dia, CancellationToken ct)
    {
        var chave = AgendaDiariaLocal.Chave(rotina.Id, dia);
        try
        {
            using var escopo = scopeFactory.CreateScope();
            var sp = escopo.ServiceProvider;
            sp.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(empresaId);

            if (await sp.GetRequiredService<IEventoNotificacaoRepository>().ExisteCorrelacaoAsync(empresaId, chave, ct))
                return;

            var construtor = sp.GetServices<IConstrutorPayloadAgendado>().FirstOrDefault(c => c.Tipo == rotina.TipoEvento);
            if (construtor is null)
            {
                logger.LogWarning(
                    "Rotina {Codigo} agendada para o tipo {Tipo}, que não tem construtor de payload agendado.", rotina.Codigo, rotina.TipoEvento);
                return;
            }

            var payload = await construtor.ConstruirAsync(empresaId, dia, ct);
            await sp.GetRequiredService<INotificadorService>()
                .EnfileirarEventoAsync(rotina.TipoEvento, empresaId, payload, rotina.Id, ct, chave);

            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            try
            {
                await unitOfWork.CommitAsync();
                logger.LogInformation("Rotina agendada {Codigo}: evento do dia {Dia} enfileirado ({Chave}).", rotina.Codigo, dia, chave);
            }
            catch (Exception ex) when (unitOfWork.EhViolacaoDeUnicidade(ex))
            {
                logger.LogInformation("Rotina agendada {Codigo}: outro host já gerou o evento do dia ({Chave}).", rotina.Codigo, chave);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao gerar o evento agendado da rotina {Codigo} da empresa {EmpresaId}.", rotina.Codigo, empresaId);
        }
    }
}
