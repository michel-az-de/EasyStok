using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

public sealed class ResolvedorCanal
{
    /// <summary>
    /// Retorna os canais permitidos em ordem de preferência da rotina,
    /// filtrando por kill switches, ativação do canal e consentimento do usuário.
    /// <para>
    /// Pausa da empresa (N5): com <paramref name="empresaId"/>, o bloqueio dessa empresa, de um canal ou geral, suprime o
    /// canal, menos para <see cref="CategoriaConteudoNotificacao.Seguranca"/>. A pausa global (sem empresa) vale para
    /// tudo. Sem <paramref name="empresaId"/> só a pausa global é lida, como antes.
    /// </para>
    /// <para>
    /// O InApp que a categoria <c>Operacional</c> acrescenta sozinho só entra quando
    /// <paramref name="inAppTemTemplate"/> (não adianta enfileirar canal sem template).
    /// </para>
    /// <para>
    /// Remetente (N6): <see cref="OrigemRemetente.Loja"/> é o comportamento de sempre. No WhatsApp de
    /// <see cref="OrigemRemetente.Plataforma"/> a política da Meta exige opt-in antes da mensagem, então o canal só sai
    /// para usuário identificado (<paramref name="usuarioDestinoId"/>) com <c>ConsentimentoNotificacao</c> de WhatsApp
    /// na categoria e <c>OptIn</c>, inclusive em <see cref="CategoriaConteudoNotificacao.Seguranca"/> (que no e-mail
    /// segue ignorando opt-out). Telefone solto no payload, sem usuário, ou sem registro: o canal é pulado.
    /// </para>
    /// </summary>
    public IReadOnlyList<CanalNotificacao> ResolverCanaisPermitidos(
        CategoriaConteudoNotificacao categoria,
        IReadOnlyList<CanalNotificacao> canaisPreferidos,
        IReadOnlyList<ConsentimentoNotificacao> consentimentos,
        IReadOnlyList<ConfiguracaoCanal> configuracoes,
        IReadOnlyList<BloqueioNotificacao> bloqueios,
        DateTime agora,
        Guid? empresaId = null,
        bool inAppTemTemplate = true,
        OrigemRemetente remetente = OrigemRemetente.Loja,
        Guid? usuarioDestinoId = null)
    {
        var permitidos = new List<CanalNotificacao>();

        foreach (var canal in canaisPreferidos)
        {
            if (TemKillSwitch(bloqueios, canal, agora, empresaId, categoria))
                continue;

            if (!CanalAtivo(configuracoes, canal))
                continue;

            if (remetente == OrigemRemetente.Plataforma && canal == CanalNotificacao.WhatsApp)
            {
                if (!OptInExplicito(consentimentos, canal, categoria, usuarioDestinoId))
                    continue;
            }
            else if (!ConsentimentoPermite(consentimentos, canal, categoria))
            {
                continue;
            }

            permitidos.Add(canal);
        }

        // InApp nunca é bloqueado para Operacional (garante fallback mínimo)
        if (categoria == CategoriaConteudoNotificacao.Operacional
            && inAppTemTemplate
            && !permitidos.Contains(CanalNotificacao.InApp)
            && !TemKillSwitch(bloqueios, CanalNotificacao.InApp, agora, empresaId, categoria))
        {
            permitidos.Add(CanalNotificacao.InApp);
        }

        return permitidos;
    }

    private static bool TemKillSwitch(
        IReadOnlyList<BloqueioNotificacao> bloqueios,
        CanalNotificacao canal,
        DateTime agora,
        Guid? empresaId,
        CategoriaConteudoNotificacao categoria)
    {
        return bloqueios.Any(b =>
            b.EstaAtivo(agora) &&
            (b.Canal == null || b.Canal == canal) &&
            (b.EmpresaId == null
             || (empresaId.HasValue && b.EmpresaId == empresaId && categoria != CategoriaConteudoNotificacao.Seguranca)));
    }

    private static bool CanalAtivo(
        IReadOnlyList<ConfiguracaoCanal> configuracoes,
        CanalNotificacao canal)
    {
        var config = configuracoes.FirstOrDefault(c => c.Canal == canal);
        return config?.AtivoNoTenant ?? false;
    }

    private static bool OptInExplicito(
        IReadOnlyList<ConsentimentoNotificacao> consentimentos,
        CanalNotificacao canal,
        CategoriaConteudoNotificacao categoria,
        Guid? usuarioDestinoId)
    {
        // Marketing nunca sai pela plataforma, e sem usuário identificado não há quem tenha dado opt-in.
        if (categoria == CategoriaConteudoNotificacao.Marketing || usuarioDestinoId is null)
            return false;

        // O histórico é imutável: vale o registro mais recente do usuário para o canal e a categoria.
        return consentimentos
            .Where(c => c.UsuarioId == usuarioDestinoId && c.Canal == canal && c.Categoria == categoria)
            .OrderByDescending(c => c.AtualizadoEm)
            .FirstOrDefault()?.OptIn == true;
    }

    private static bool ConsentimentoPermite(
        IReadOnlyList<ConsentimentoNotificacao> consentimentos,
        CanalNotificacao canal,
        CategoriaConteudoNotificacao categoria)
    {
        // Transacional (interesse legítimo) e Segurança (segurança da conta, N2) bypassam consentimento
        if (categoria.IgnoraConsentimento())
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
