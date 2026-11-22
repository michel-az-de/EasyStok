using EasyStock.Application.UseCases.AutenticarUsuario;

namespace EasyStock.Application.Services.Auth;

/// <summary>
/// Nível, empresa e permissões de uma sessão (#2070, M3): a regra única do login por senha, do Google e do refresh.
/// O suporte é o perfil global (<see cref="Perfil.EhSuporteGlobal"/>, B1) e mantém a empresa pedida; os demais precisam de
/// vínculo ativo com a empresa da sessão e entram no nível do perfil dela (<see cref="Perfil.NivelNaEmpresa"/>).
/// Credencial já conferida: aqui não há senha nem token.
/// </summary>
public static class ResolvedorDeSessao
{
    /// <param name="empresaId">A empresa da sessão de quem não é suporte. Nula: só quem tem algum vínculo ativo entra, sem empresa.</param>
    /// <param name="empresaDoSuporte">A empresa em que o suporte entra (#1342, #1326); nula no painel da plataforma.</param>
    public static AutenticarUsuarioResult Resolver(Usuario usuario, Guid? empresaId, Guid? empresaDoSuporte)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        // O suporte não tem vínculo em UsuarioEmpresa: sem este ramo o fluxo da empresa o deixava Visualizador (M3).
        var perfilDoSuporte = usuario.Perfis?.Select(up => up.Perfil).FirstOrDefault(p => p is { EhSuporteGlobal: true });
        if (perfilDoSuporte is not null)
            return new AutenticarUsuarioResult(
                UsuarioId: usuario.Id,
                EmpresaId: empresaDoSuporte,
                Nome: usuario.Nome,
                Email: usuario.Email,
                Nivel: NivelAcesso.SuperAdmin,
                Permissoes: perfilDoSuporte.Permissoes?.Select(pp => pp.Permissao).Distinct().ToArray() ?? [],
                ModuloInicial: perfilDoSuporte.ModuloInicial,
                PermissoesExplicitas: perfilDoSuporte.PermissoesExplicitas);

        if (empresaId.HasValue)
        {
            if (usuario.Empresas?.Any(e => e.EmpresaId == empresaId.Value && e.Ativo) != true)
                throw new CredenciaisInvalidasException();
        }
        // Desativar zera o vínculo, não o usuário: sem vínculo ativo não há sessão a abrir.
        else if (usuario.Empresas?.Any(e => e.Ativo) != true)
            throw new CredenciaisInvalidasException();

        var perfil = empresaId.HasValue
            ? usuario.Perfis?
                .Where(p => p.EmpresaId == empresaId.Value && p.Perfil is not null)
                .Select(p => p.Perfil!)
                .OrderBy(p => p.NivelNaEmpresa)
                .FirstOrDefault()
            : null;

        if (perfil is null)
            return new AutenticarUsuarioResult(usuario.Id, empresaId, usuario.Nome, usuario.Email, NivelAcesso.Visualizador, []);

        return new AutenticarUsuarioResult(
            UsuarioId: usuario.Id,
            EmpresaId: empresaId,
            Nome: usuario.Nome,
            Email: usuario.Email,
            Nivel: perfil.NivelNaEmpresa,
            Permissoes: perfil.Permissoes?.Select(p => p.Permissao).Distinct().ToArray() ?? [],
            ModuloInicial: perfil.ModuloInicial,
            PermissoesExplicitas: perfil.PermissoesExplicitas,
            SessaoPersistente: EmpresaPadrao(usuario) == empresaId
                && Domain.Services.PerfisCasaDaBaba.PermiteSessaoPersistente(perfil, empresaId!.Value));
    }

    /// <summary>A empresa ativa do usuário quando ele tem uma só; com várias, nenhuma (a escolha é do login).</summary>
    public static Guid? EmpresaPadrao(Usuario usuario)
    {
        var ativas = usuario.Empresas?.Where(e => e.Ativo).Select(e => e.EmpresaId).Distinct().ToArray() ?? [];
        return ativas.Length == 1 ? ativas[0] : null;
    }

    /// <summary>A primeira empresa ativa por nome (#1324: o Google sem empresa pedida entra nela).</summary>
    public static Guid? PrimeiraEmpresaAtiva(Usuario usuario) =>
        usuario.Empresas?
            .Where(e => e.Ativo)
            .OrderBy(e => e.Empresa?.Nome)
            .Select(e => (Guid?)e.EmpresaId)
            .FirstOrDefault();
}
