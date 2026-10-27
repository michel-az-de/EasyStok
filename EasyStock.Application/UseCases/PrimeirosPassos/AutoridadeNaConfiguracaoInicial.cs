namespace EasyStock.Application.UseCases.PrimeirosPassos;

/// <summary>Quem a pessoa é na configuração inicial da empresa do pedido (#2070).</summary>
public sealed record AutoridadeNaConfiguracao(bool EhDona, bool EhSuporteGlobal)
{
    /// <summary>Adiar e concluir são da dona ativa ou do suporte global.</summary>
    public bool PodeDecidir => EhDona || EhSuporteGlobal;
}

/// <summary>
/// Lê do banco se a pessoa é dona ativa da empresa do JWT ou suporte global (#2070), nunca de claim. Regra provisória
/// deste pacote, isolada aqui: a política de hierarquia da #2070 é dona dela e a substitui na integração.
/// </summary>
public sealed class AutoridadeNaConfiguracaoInicial(IUsuarioRepository usuarios)
{
    /// <summary>Código do 403 de quem não é dona nem suporte.</summary>
    public const string CodigoHierarquia = "HIERARQUIA";

    public async Task<AutoridadeNaConfiguracao> AvaliarAsync(Guid usuarioId, Guid empresaId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var usuario = await usuarios.GetByIdAsync(usuarioId);
        return new AutoridadeNaConfiguracao(EhDonaAtiva(usuario, empresaId), EhSuporteGlobal(usuario));
    }

    /// <summary>Recusa com <see cref="CodigoHierarquia"/> quem não é dona ativa nem suporte global.</summary>
    public async Task ExigirQuemDecideAsync(Guid usuarioId, Guid empresaId, string acao, CancellationToken ct = default)
    {
        if (!(await AvaliarAsync(usuarioId, empresaId, ct)).PodeDecidir)
            throw new UseCaseValidationException(CodigoHierarquia, $"Só a dona da loja ou o suporte pode {acao} a configuração inicial.");
    }

    /// <summary>Dona ativa: marca de dona, vínculo e conta ativos e perfil de nível Admin nesta empresa.</summary>
    private static bool EhDonaAtiva(Usuario? usuario, Guid empresaId) =>
        usuario is { Ativo: true }
        && usuario.Empresas.Any(v => v.EmpresaId == empresaId && v.Ativo && v.EhDona)
        && usuario.Perfis.Any(up => up.EmpresaId == empresaId && up.Perfil is { Nivel: NivelAcesso.Admin });

    /// <summary>Suporte global: perfil de nível SuperAdmin sem empresa (perfil da empresa com esse nível não conta).</summary>
    private static bool EhSuporteGlobal(Usuario? usuario) =>
        usuario is { Ativo: true }
        && usuario.Perfis.Any(up => up.Perfil is { Nivel: NivelAcesso.SuperAdmin, EmpresaId: null });
}
