namespace EasyStock.Domain.Services;

/// <summary>
/// Quem age ou sofre a ação na hierarquia de uma empresa (#2070), sempre lido do banco no vínculo da empresa do pedido:
/// nunca do JWT, nunca de uma claim de dona.
/// </summary>
/// <param name="Nivel">O nível na empresa: o perfil de mais poder nela. Sem perfil é Visualizador (B2).</param>
/// <param name="EhDona">A marca <see cref="UsuarioEmpresa.EhDona"/> do vínculo com a empresa.</param>
/// <param name="ContaProvada">E-mail confirmado ou convite aceito por e-mail ou Google (<see cref="Usuario.ContaProvada"/>).</param>
/// <param name="EhSuporteGlobal">Tem o perfil global de suporte (<see cref="Perfil.EhSuporteGlobal"/>).</param>
public sealed record ParticipanteDaHierarquia(
    Guid UsuarioId,
    NivelAcesso Nivel,
    bool EhDona,
    bool VinculoAtivo,
    bool UsuarioAtivo,
    bool ContaProvada,
    bool EhSuporteGlobal = false)
{
    /// <summary>
    /// Dona ativa (#2070): a marca, o vínculo e o usuário ativos e o nível Admin na empresa. A convidada que ainda não
    /// aceitou conta. É o mesmo predicado do <c>ContarDonasAtivasAsync</c> no banco.
    /// </summary>
    public bool EhDonaAtiva => EhDona && VinculoAtivo && UsuarioAtivo && Nivel == NivelAcesso.Admin;

    /// <summary>Acesso ativo de nível Admin na empresa: a dona ou a Administração.</summary>
    public bool AdministraAEmpresa => VinculoAtivo && UsuarioAtivo && Nivel == NivelAcesso.Admin;

    /// <summary>Lê o participante do usuário carregado com vínculos e perfis, no recorte da empresa.</summary>
    public static ParticipanteDaHierarquia De(Usuario usuario, Guid empresaId, bool ehSuporteGlobal)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var vinculo = usuario.Empresas?.FirstOrDefault(e => e.EmpresaId == empresaId);
        var nivel = usuario.Perfis?
            .Where(up => up.EmpresaId == empresaId && up.Perfil is not null)
            .Select(up => (NivelAcesso?)up.Perfil!.NivelNaEmpresa)
            .Min() ?? NivelAcesso.Visualizador;
        return new(usuario.Id, nivel, vinculo?.EhDona ?? false, vinculo?.Ativo ?? false, usuario.Ativo,
            usuario.ContaProvada, ehSuporteGlobal);
    }

    /// <summary>
    /// Nome, e-mail e convite são da conta, não do vínculo (#2070): valem em toda empresa da pessoa. Para essas ações
    /// (<see cref="PoliticaDeHierarquia.EhDaConta"/>) o alvo é visto pelo recorte mais protegido das empresas dele: dona se
    /// tem a marca em alguma, e o nível mais alto que tem em qualquer uma. <paramref name="contaInteira"/> é a leitura com
    /// todas as empresas, a de antes da trava.
    /// </summary>
    public ParticipanteDaHierarquia ComAContaInteira(Usuario contaInteira)
    {
        ArgumentNullException.ThrowIfNull(contaInteira);
        var donaEmAlguma = contaInteira.Empresas?.Any(e => e.EhDona) ?? false;
        var nivelMaisAlto = contaInteira.Perfis?
            .Where(up => up.Perfil is { EhSuporteGlobal: false })
            .Select(up => (NivelAcesso?)up.Perfil!.NivelNaEmpresa)
            .Min();
        return this with
        {
            EhDona = EhDona || donaEmAlguma,
            Nivel = nivelMaisAlto is { } nivel && nivel < Nivel ? nivel : Nivel,
        };
    }

    /// <summary>A pessoa que ainda vai nascer (<see cref="AcaoSobreUsuario.Criar"/>): o nível do perfil escolhido e a marca pedida.</summary>
    public static ParticipanteDaHierarquia Novo(NivelAcesso? nivel, bool dona) =>
        new(Guid.Empty, nivel ?? NivelAcesso.Visualizador, dona, VinculoAtivo: true, UsuarioAtivo: true, ContaProvada: false);

    /// <summary>Sem usuário autenticado (job, chamada sem token): não pode nada.</summary>
    public static ParticipanteDaHierarquia Ninguem { get; } =
        new(Guid.Empty, NivelAcesso.Visualizador, false, false, false, false);
}
