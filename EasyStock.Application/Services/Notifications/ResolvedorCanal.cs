using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

public sealed class ResolvedorCanal
{
    /// <summary>
    /// Retorna os canais permitidos em ordem de preferência da rotina,
    /// filtrando por kill switches, ativação do canal e consentimento do usuário.
    /// </summary>
    public IReadOnlyList<CanalNotificacao> ResolverCanaisPermitidos(
        CategoriaConteudoNotificacao categoria,
        IReadOnlyList<CanalNotificacao> canaisPreferidos,
        IReadOnlyList<ConsentimentoNotificacao> consentimentos,
        IReadOnlyList<ConfiguracaoCanal> configuracoes,
        IReadOnlyList<BloqueioNotificacao> bloqueios,
        DateTime agora)
    {
        var permitidos = new List<CanalNotificacao>();

        foreach (var canal in canaisPreferidos)
        {
            if (TemKillSwitch(bloqueios, canal, agora))
                continue;

            if (!CanalAtivo(configuracoes, canal))
                continue;

            if (!ConsentimentoPermite(consentimentos, canal, categoria))
                continue;

            permitidos.Add(canal);
        }

        // InApp nunca é bloqueado para Operacional (garante fallback mínimo)
        if (categoria == CategoriaConteudoNotificacao.Operacional
            && !permitidos.Contains(CanalNotificacao.InApp)
            && !TemKillSwitch(bloqueios, CanalNotificacao.InApp, agora))
        {
            permitidos.Add(CanalNotificacao.InApp);
        }

        return permitidos;
    }

    private static bool TemKillSwitch(
        IReadOnlyList<BloqueioNotificacao> bloqueios,
        CanalNotificacao canal,
        DateTime agora)
    {
        return bloqueios.Any(b =>
            b.EstaAtivo(agora) &&
            (b.Canal == null || b.Canal == canal) &&
            b.EmpresaId == null); // kill switch global; empresa-scoped é validado pelo caller
    }

    private static bool CanalAtivo(
        IReadOnlyList<ConfiguracaoCanal> configuracoes,
        CanalNotificacao canal)
    {
        var config = configuracoes.FirstOrDefault(c => c.Canal == canal);
        return config?.AtivoNoTenant ?? false;
    }

    private static bool ConsentimentoPermite(
        IReadOnlyList<ConsentimentoNotificacao> consentimentos,
        CanalNotificacao canal,
        CategoriaConteudoNotificacao categoria)
    {
        // Transacional bypassa consentimento (interesse legítimo)
        if (categoria == CategoriaConteudoNotificacao.Transacional)
            return true;

        var consentimento = consentimentos.FirstOrDefault(
            c => c.Canal == canal && c.Categoria == categoria);

        if (consentimento == null)
        {
            // Marketing exige opt-in explícito; Operacional assume permitido por default
            return categoria != CategoriaConteudoNotificacao.Marketing;
        }

        return consentimento.OptIn;
    }
}
