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
/// A listagem das empresas é a única leitura de dado de loja entre tenants e pede o <see cref="IRowLevelSecurityBypass"/>
/// por conta própria (o orquestrador já roda cada coletor sob bypass; o estado e o aviso, que não são dado de loja, também
/// rodam sob ele). A avaliação de cada empresa abre um escopo próprio com o tenant ligado; erro numa empresa, ou a ponte
/// fora do ar, não derruba as outras nem muda o estado (ponte que não responde não é "muda"). O estado vive em
/// <c>endpoint_health_state</c> com o nome <c>wppconnect/{empresa}</c>. Nada de telefone ou texto sai daqui: o aviso só
/// leva enums (ADR-0057).
/// </para>
/// <para>
/// O aviso é um por rodada, não um por loja: a chave de dedupe do publicador é por componente, e lojas costumam ficar
/// mudas juntas. Abre se alguma loja abriu ou segue muda; normaliza só quando nenhuma loja tem incidente aberto. Loja que
/// sai do escopo (desconectou, desligou o módulo, perdeu a credencial) tem o estado apagado, para o incidente não ficar
/// aberto para sempre.
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

    private const string PrefixoDoEstado = "wppconnect/";

    public static string NomeDoEstado(Guid empresaId) => $"{PrefixoDoEstado}{empresaId:N}";

    public async Task ColetarAsync(CancellationToken ct = default)
    {
        if (!configuration.GetValue<bool>(ChaveHabilitado)) return;

        List<Guid> empresas;
        using (bypassRls.Begin())
            empresas = await db.Set<CredencialIntegracao>().AsNoTracking()
                .Where(c => c.ProviderKey == CredencialWppConnect.ProviderKey && c.Ativo)
                .Select(c => c.EmpresaId).Distinct().ToListAsync(ct);

        var agora = relogio.GetUtcNow().UtcDateTime;
        var estados = await db.EndpointHealthStates.Where(s => s.EndpointName.StartsWith(PrefixoDoEstado)).ToListAsync(ct);
        var mantidos = new HashSet<string>(StringComparer.Ordinal);
        DateTime? problemaDesde = null, normalizadoDesde = null;

        foreach (var empresaId in empresas)
        {
            ct.ThrowIfCancellationRequested();
            var sinal = await AvaliarAsync(empresaId, ct);
            if (sinal is null) { mantidos.Add(NomeDoEstado(empresaId)); continue; } // não avaliou: o estado fica como está
            if (sinal == SinalPonteWppConnect.NaoAvaliada) continue;                  // fora do escopo: fecha abaixo

            var nome = NomeDoEstado(empresaId);
            mantidos.Add(nome);
            var estado = estados.FirstOrDefault(s => s.EndpointName == nome);
            if (estado is null)
            {
                estado = new EndpointHealthState { EndpointName = nome };
                db.EndpointHealthStates.Add(estado);
                estados.Add(estado);
            }

            var muda = sinal == SinalPonteWppConnect.Muda;
            var avaliacao = AvaliadorIncidente.Avaliar(estado, saudavel: !muda, agora, Limiares);
            if (muda) estado.LastFailureMessage = "PONTE_MUDA";

            switch (avaliacao.Decisao)
            {
                case DecisaoIncidente.Abrir:
                case DecisaoIncidente.Reavisar:
                    logger.LogWarning(
                        "Ponte WPPConnect muda: a empresa {EmpresaId} está conectada e tem mensagem recebida que não chegou ao webhook.",
                        empresaId);
                    problemaDesde = MaisAntigo(problemaDesde, avaliacao.DesdeUtc ?? agora);
                    break;
                case DecisaoIncidente.Resolver:
                    logger.LogInformation("Ponte WPPConnect voltou a entregar para a empresa {EmpresaId}.", empresaId);
                    normalizadoDesde = MaisAntigo(normalizadoDesde, avaliacao.DesdeUtc ?? agora);
                    break;
            }
        }

        foreach (var orfao in estados.Where(s => !mantidos.Contains(s.EndpointName)).ToList())
        {
            if (orfao.LastAlertedAt is { } desde) normalizadoDesde = MaisAntigo(normalizadoDesde, desde);
            db.EndpointHealthStates.Remove(orfao);
            estados.Remove(orfao);
        }

        // O estado é gravado antes do aviso: o publicador commita a mesma unidade de trabalho e, na dedupe, deixa o evento
        // recusado pendente; nada mais é gravado aqui depois dele.
        await db.SaveChangesAsync(ct);

        if (problemaDesde is { } aberto)
            await publicador.PublicarAsync(ComponenteIncidente.Integracao, EstadoIncidente.ComProblema,
                SeveridadeIncidente.Alta, aberto, ct);
        else if (normalizadoDesde is { } fechado && estados.TrueForAll(s => s.LastAlertedAt is null))
            await publicador.PublicarAsync(ComponenteIncidente.Integracao, EstadoIncidente.Normalizado,
                SeveridadeIncidente.Media, fechado, ct);
    }

    private static DateTime MaisAntigo(DateTime? atual, DateTime novo) => atual is { } a && a < novo ? a : novo;

    /// <summary><c>null</c>: não deu para avaliar (erro, ponte fora do ar). <c>NaoAvaliada</c>: não há o que vigiar nesta empresa.</summary>
    private async Task<SinalPonteWppConnect?> AvaliarAsync(Guid empresaId, CancellationToken ct)
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
            return null;
        }
    }
}
