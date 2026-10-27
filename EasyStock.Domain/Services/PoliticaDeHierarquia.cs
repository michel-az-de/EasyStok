namespace EasyStock.Domain.Services;

/// <summary>
/// Regra única de quem pode mexer no acesso de quem numa empresa (#2070, ADR-0038). O suporte global está acima de tudo,
/// a dona acima da Administração e a Administração só alcança da Gerência para baixo. Ordem da avaliação:
/// <list type="number">
///   <item>o suporte só é alvo do próprio suporte e nunca recebe a marca (<c>DONA_SUPORTE</c>);</item>
///   <item>quem não é suporte precisa de acesso ativo de nível Admin na empresa (<c>HIERARQUIA</c>);</item>
///   <item>sobre si mesmo, só o nome (<c>USUARIO_PROPRIO</c>);</item>
///   <item>a Administração nunca alcança a dona (<c>HIERARQUIA</c>), nem quando ela é a última: a resposta não manda marcar
///   outra dona, ação que a Administração não tem, e não conta quantas donas existem;</item>
///   <item>a empresa nunca fica sem dona ativa (<c>ULTIMA_DONA</c>), para o suporte e para a dona;</item>
///   <item>a tabela do ator: suporte, dona ou Administração.</item>
/// </list>
/// </summary>
public static class PoliticaDeHierarquia
{
    /// <param name="donasAtivas">Donas ativas da empresa, contadas depois da trava da hierarquia.</param>
    /// <param name="nivelNovo">O nível do perfil novo em <see cref="AcaoSobreUsuario.TrocarPerfil"/> e <see cref="AcaoSobreUsuario.Criar"/>.</param>
    public static DecisaoDaHierarquia Avaliar(
        ParticipanteDaHierarquia ator, ParticipanteDaHierarquia alvo, AcaoSobreUsuario acao, int donasAtivas,
        NivelAcesso? nivelNovo = null)
    {
        ArgumentNullException.ThrowIfNull(ator);
        ArgumentNullException.ThrowIfNull(alvo);

        if (alvo.EhSuporteGlobal)
        {
            if (acao == AcaoSobreUsuario.MarcarDona) return Nega(CodigosDaHierarquia.DonaSuporte);
            if (!ator.EhSuporteGlobal) return Nega(CodigosDaHierarquia.Hierarquia);
        }

        if (!ator.EhSuporteGlobal && !ator.AdministraAEmpresa) return Nega(CodigosDaHierarquia.Hierarquia);

        if (!ator.EhSuporteGlobal && ator.UsuarioId == alvo.UsuarioId)
            return acao == AcaoSobreUsuario.EditarNome ? DecisaoDaHierarquia.Permite : Nega(CodigosDaHierarquia.UsuarioProprio);

        if (!ator.EhSuporteGlobal && !ator.EhDonaAtiva && alvo.EhDona) return Nega(CodigosDaHierarquia.Hierarquia);

        if (alvo.EhDonaAtiva && TiraDona(acao, nivelNovo) && donasAtivas <= 1)
            return Nega(CodigosDaHierarquia.UltimaDona);

        if (ator.EhSuporteGlobal) return DoSuporte(alvo, acao, nivelNovo);
        if (ator.EhDonaAtiva) return DaDona(alvo, acao);
        return DaAdministracao(alvo, acao, nivelNovo);
    }

    /// <summary>
    /// Anonimizar a própria conta (LGPD) tira a pessoa da contagem de donas em toda empresa em que ela é dona: a última
    /// dona ativa não sai (<c>ULTIMA_DONA</c>); a dona que não é a última perde a marca.
    /// </summary>
    public static DecisaoDaHierarquia AvaliarAnonimizacao(ParticipanteDaHierarquia propria, int donasAtivas)
    {
        ArgumentNullException.ThrowIfNull(propria);
        return propria.EhDonaAtiva && donasAtivas <= 1 ? Nega(CodigosDaHierarquia.UltimaDona) : DecisaoDaHierarquia.Permite;
    }

    /// <summary>
    /// Ações sobre a conta, que valem em toda empresa da pessoa: o nome, o e-mail e o convite (o link entra na conta).
    /// O alvo delas é lido por <see cref="ParticipanteDaHierarquia.ComAContaInteira"/>.
    /// </summary>
    public static bool EhDaConta(AcaoSobreUsuario acao) =>
        acao is AcaoSobreUsuario.EditarNome or AcaoSobreUsuario.TrocarEmail or AcaoSobreUsuario.ReenviarConvite;

    /// <summary>Quem pode dar um perfil de cada nível: suporte e dona dão todos; a Administração só abaixo de Admin.</summary>
    public static bool PodeDarNivel(ParticipanteDaHierarquia ator, NivelAcesso nivel)
    {
        ArgumentNullException.ThrowIfNull(ator);
        if (ator.EhSuporteGlobal || ator.EhDonaAtiva) return true;
        return ator.AdministraAEmpresa && nivel > NivelAcesso.Admin;
    }

    /// <summary>
    /// O suporte pode tudo, mas justifica o que tira a conta ou a marca da dona: trocar o e-mail, tirar a marca e
    /// rebaixar (o rebaixamento tira a marca, o mesmo efeito de <see cref="AcaoSobreUsuario.DesmarcarDona"/>).
    /// </summary>
    private static DecisaoDaHierarquia DoSuporte(ParticipanteDaHierarquia alvo, AcaoSobreUsuario acao, NivelAcesso? nivelNovo)
    {
        if (acao == AcaoSobreUsuario.MarcarDona) return AlvoDaMarca(alvo);
        if (acao == AcaoSobreUsuario.Criar && alvo.EhDona) return NovaDona(alvo);
        if (!alvo.EhDona) return DecisaoDaHierarquia.Permite;
        return acao is AcaoSobreUsuario.TrocarEmail or AcaoSobreUsuario.DesmarcarDona
            || (acao == AcaoSobreUsuario.TrocarPerfil && nivelNovo != NivelAcesso.Admin)
            ? DecisaoDaHierarquia.PermiteComMotivo
            : DecisaoDaHierarquia.Permite;
    }

    private static DecisaoDaHierarquia DaDona(ParticipanteDaHierarquia alvo, AcaoSobreUsuario acao)
    {
        if (!alvo.EhDona)
            return acao == AcaoSobreUsuario.MarcarDona ? AlvoDaMarca(alvo) : DecisaoDaHierarquia.Permite;

        return acao switch
        {
            AcaoSobreUsuario.Criar => NovaDona(alvo),
            AcaoSobreUsuario.TrocarEmail => Nega(CodigosDaHierarquia.Hierarquia),
            // Reativar desfaz a desativação, que sobre a dona só o suporte faz: a decisão volta para ele.
            AcaoSobreUsuario.TrocarPerfil or AcaoSobreUsuario.Desativar or AcaoSobreUsuario.Reativar
                => Nega(CodigosDaHierarquia.DonaProtegida),
            _ => DecisaoDaHierarquia.Permite,
        };
    }

    private static DecisaoDaHierarquia DaAdministracao(ParticipanteDaHierarquia alvo, AcaoSobreUsuario acao, NivelAcesso? nivelNovo)
    {
        if (alvo.EhDona || alvo.Nivel <= NivelAcesso.Admin) return Nega(CodigosDaHierarquia.Hierarquia);
        if (acao is AcaoSobreUsuario.MarcarDona or AcaoSobreUsuario.DesmarcarDona) return Nega(CodigosDaHierarquia.Hierarquia);
        if ((acao is AcaoSobreUsuario.TrocarPerfil or AcaoSobreUsuario.Criar) && nivelNovo is { } nivel && nivel <= NivelAcesso.Admin)
            return Nega(CodigosDaHierarquia.Hierarquia);
        return DecisaoDaHierarquia.Permite;
    }

    /// <summary>A marca só vai para conta provada com acesso ativo de nível Admin; marcar quem já é dona não muda nada.</summary>
    private static DecisaoDaHierarquia AlvoDaMarca(ParticipanteDaHierarquia alvo)
    {
        if (alvo.EhDona) return DecisaoDaHierarquia.Permite;
        if (!alvo.AdministraAEmpresa) return Nega(CodigosDaHierarquia.DonaNaoAdmin);
        return alvo.ContaProvada ? DecisaoDaHierarquia.Permite : Nega(CodigosDaHierarquia.DonaContaNaoProvada);
    }

    /// <summary>A dona que nasce no cadastro precisa de perfil de nível Admin. A conta é provada depois, pelo convite por e-mail.</summary>
    private static DecisaoDaHierarquia NovaDona(ParticipanteDaHierarquia alvo) =>
        alvo.Nivel == NivelAcesso.Admin ? DecisaoDaHierarquia.Permite : Nega(CodigosDaHierarquia.DonaNaoAdmin);

    /// <summary>Ações que tiram a pessoa da contagem de donas ativas. Troca de perfil sem nível conhecido conta como rebaixar.</summary>
    private static bool TiraDona(AcaoSobreUsuario acao, NivelAcesso? nivelNovo) => acao switch
    {
        AcaoSobreUsuario.DesmarcarDona or AcaoSobreUsuario.Desativar => true,
        AcaoSobreUsuario.TrocarPerfil => nivelNovo != NivelAcesso.Admin,
        _ => false,
    };

    private static DecisaoDaHierarquia Nega(string codigo) => DecisaoDaHierarquia.Nega(codigo);
}
