using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Notifications.Collectors;

/// <summary>
/// Ponte WPPConnect muda (#3117): conectada e sem entregar mensagem ao webhook. A cada rodada do coletor (5 min) pergunta
/// ao <see cref="VigiaPonteWppConnect"/> de cada empresa com a ponte ativa e abre o incidente <c>Integracao</c> na primeira
/// rodada muda (carência de 5 min do vigia mais uma rodada: até 10 min). Resolve depois de 2 rodadas boas seguidas.
/// <para>
/// Nasce DESLIGADO (<c>Atendimento:WppConnect:VigiaPonteMuda:Habilitado</c>): o formato do <c>list-chats</c> da ponte
/// precisa ser conferido em produção antes de ligar (runbook em <c>docs/dev/incidentes/</c>). Desligado, não consulta a ponte.
/// </para>
/// <para>
/// A listagem das empresas é a única leitura entre tenants e roda sob <see cref="IRowLevelSecurityBypass"/>. Depois cada
/// empresa abre um escopo próprio com o tenant ligado; erro numa empresa, ou a ponte fora do ar, não derruba as outras nem
/// muda o estado (ponte que não responde não é "muda"). O estado vive em <c>endpoint_health_state</c> com o nome
/// <c>wppconnect/{empresa}</c>. Nada de telefone ou texto sai daqui: o aviso só leva enums (ADR-0057).
/// </para>
/// </summary>
public sealed class ColetorPonteWppConnectMuda(
    IServiceScopeFactory scopeFactory,
    IRowLevelSecurityBypass bypassRls,
    EasyStockDbContext db,
    IPublicadorIncidenteSistema publicador,
    IConfiguration configuration,
    TimeProvider relogio,
    ILogger<ColetorPonteWppConnectMuda> logger) : IColetorEventoNotificacao
{
    public const string ChaveHabilitado = "Atendimento:WppConnect:VigiaPonteMuda:Habilitado";

    private static readonly LimiaresIncidente Limiares = new(FalhasParaAbrir: 1, BoasParaResolver: 2);

    public static string NomeDoEstado(Guid empresaId) => $"wppconnect/{empresaId:N}";

    public async Task ColetarAsync(CancellationToken ct = default)
    {
        if (!configuration.GetValue<bool>(ChaveHabilitado)) return;

        List<Guid> empresas;
        using (bypassRls.Begin())
            empresas = await db.Set<CredencialIntegracao>().AsNoTracking()
                .Where(c => c.ProviderKey == CredencialWppConnect.ProviderKey && c.Ativo)
                .Select(c => c.EmpresaId).Distinct().ToListAsync(ct);

        foreach (var empresaId in empresas)
        {
            ct.ThrowIfCancellationRequested();
            var sinal = await AvaliarAsync(empresaId, ct);
            if (sinal != SinalPonteWppConnect.NaoAvaliada)
                await RegistrarAsync(empresaId, sinal == SinalPonteWppConnect.Muda, ct);
        }
    }

    private async Task<SinalPonteWppConnect> AvaliarAsync(Guid empresaId, CancellationToken ct)
    {
        try
        {
            using var escopo = scopeFactory.CreateScope();
            var sp = escopo.ServiceProvider;
            sp.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(empresaId);

            // Módulo desligado: o webhook recusa de propósito e não há entrega a medir.
            var flags = await sp.GetRequiredService<ITenantFeatureFlagRepository>().ListarAtivasAsync(empresaId, ct);
            if (!flags.Contains(FeatureCatalogo.ModuloAtendimento, StringComparer.OrdinalIgnoreCase))
                return SinalPonteWppConnect.NaoAvaliada;

            var sessao = await sp.GetRequiredService<IIntegrationCredentialResolver>().ObterAsync<CredencialWppConnect>(
                empresaId, CredencialWppConnect.ProviderKey, AmbienteIntegracao.Production, ct);
            if (sessao is null || !sp.GetRequiredService<IWppConnectSessaoClient>().Habilitado)
                return SinalPonteWppConnect.NaoAvaliada;

            return await sp.GetRequiredService<VigiaPonteWppConnect>().AvaliarAsync(sessao, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Só o tipo: a mensagem da exceção pode trazer endereço ou resposta da ponte.
            logger.LogWarning("Vigia da ponte WPPConnect não avaliou a empresa {EmpresaId}: {TipoErro}.", empresaId, ex.GetType().Name);
            return SinalPonteWppConnect.NaoAvaliada;
        }
    }

    private async Task RegistrarAsync(Guid empresaId, bool muda, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var nome = NomeDoEstado(empresaId);
        var estado = await db.EndpointHealthStates.FirstOrDefaultAsync(s => s.EndpointName == nome, ct);
        if (estado is null)
        {
            estado = new EndpointHealthState { EndpointName = nome };
            db.EndpointHealthStates.Add(estado);
        }

        var avaliacao = AvaliadorIncidente.Avaliar(estado, saudavel: !muda, agora, Limiares);
        if (muda) estado.LastFailureMessage = "PONTE_MUDA";

        switch (avaliacao.Decisao)
        {
            case DecisaoIncidente.Abrir:
            case DecisaoIncidente.Reavisar:
                logger.LogWarning(
                    "Ponte WPPConnect muda: a empresa {EmpresaId} está conectada e tem mensagem recebida que não chegou ao webhook.",
                    empresaId);
                await publicador.PublicarAsync(ComponenteIncidente.Integracao, EstadoIncidente.ComProblema,
                    SeveridadeIncidente.Alta, avaliacao.DesdeUtc ?? agora, ct);
                break;
            case DecisaoIncidente.Resolver:
                logger.LogInformation("Ponte WPPConnect voltou a entregar para a empresa {EmpresaId}.", empresaId);
                await publicador.PublicarAsync(ComponenteIncidente.Integracao, EstadoIncidente.Normalizado,
                    SeveridadeIncidente.Media, avaliacao.DesdeUtc ?? agora, ct);
                break;
        }

        // O publicador commita a mesma unidade de trabalho; sem publicação ou sem decisão, o estado ainda precisa ser gravado.
        await db.SaveChangesAsync(ct);
    }
}
