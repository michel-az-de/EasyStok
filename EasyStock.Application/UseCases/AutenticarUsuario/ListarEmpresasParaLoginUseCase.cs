namespace EasyStock.Application.UseCases.AutenticarUsuario;

/// <summary>
/// Valida credenciais e retorna a lista de Empresas disponíveis para o usuário
/// (step 1 do login 2-etapas para usuários não-SuperAdmin). NÃO emite JWT
/// nem atualiza o último acesso — é apenas uma pré-validação para popular
/// o seletor de Empresa na tela de login.
///
/// <para>
/// Senha errada conta como falha de login (#1352): mesmo contador e mesmo bloqueio do login completo.
/// Senha certa não conta falha: devolve a tentativa reservada antes do BCrypt (#1781).
/// </para>
///
/// <para>
/// Fluxo de uso:
/// 1. Cliente chama este use case com email + senha.
/// 2. Se IsSuperAdmin=true → cliente chama AutenticarUsuarioUseCase diretamente.
/// 3. Se IsSuperAdmin=false → exibe picker com Empresas[] → usuário seleciona
///    → cliente chama AutenticarUsuarioUseCase com o EmpresaId escolhido.
/// </para>
/// </summary>
public sealed record ListarEmpresasParaLoginCommand(string Email, string Senha);

public sealed record EmpresaParaLoginDto(Guid Id, string Nome);

public sealed record ListarEmpresasParaLoginResult(
    bool IsSuperAdmin,
    IReadOnlyList<EmpresaParaLoginDto> Empresas);

public class ListarEmpresasParaLoginUseCase(
    IUsuarioRepository usuarioRepository,
    IPasswordHasher passwordHasher,
    ILogger<ListarEmpresasParaLoginUseCase> logger)
{
    public async Task<ListarEmpresasParaLoginResult> ExecuteAsync(ListarEmpresasParaLoginCommand command)
    {
        var usuario = await usuarioRepository.GetByEmailAsync(command.Email);

        // Resposta genérica para não vazar distinção entre "usuário não existe" e "senha errada".
        // #1781: nem pelo tempo (BCrypt contra o hash fictício) nem para conta bloqueada.
        if (usuario is null || !usuario.Ativo || usuario.EstaBloqueado())
            throw RecusarSemConferir(command.Senha);

        // #1781: a tentativa é contada no banco, num UPDATE atômico, antes do BCrypt (mesmo contador do login).
        if (!await usuarioRepository.ReservarTentativaDeSenhaAsync(usuario.Id, DateTime.UtcNow))
            throw RecusarSemConferir(command.Senha);

        var senhaOk = passwordHasher.Verify(command.Senha, usuario.SenhaHash);
        if (!senhaOk)
        {
            // #1352: a senha errada do passo 1 conta como a do login completo (mesmo contador, mesmo bloqueio).
            // #1781: a reserva já gravou a falha; a entidade só espelha (log).
            usuario.RegistrarFalhaDeSenha();
            if (usuario.EstaBloqueado())
                logger.LogWarning("ListarEmpresasParaLogin: usuario {UsuarioId} bloqueado apos {Limite} tentativas falhas", usuario.Id, Domain.Entities.Usuario.FalhasParaBloquear);
            throw new CredenciaisInvalidasException();
        }

        // #1352: senha certa no passo 1 não zera nada (quem zera é o login completo): devolve a tentativa reservada.
        await usuarioRepository.DevolverTentativaDeSenhaAsync(usuario.Id);

        logger.LogDebug("ListarEmpresasParaLogin: credenciais válidas para {Domain}", MaskEmail(command.Email));

        // SuperAdmin: não tem empresas — login direto sem seleção.
        var isSuperAdmin = usuario.Perfis?
            .Select(up => up.Perfil)
            .Any(p => p != null && p.Nivel == NivelAcesso.SuperAdmin) ?? false;

        if (isSuperAdmin)
            return new ListarEmpresasParaLoginResult(true, []);

        // Retorna Empresas ativas onde o usuário tem algum perfil.
        var empresas = usuario.Empresas?
            .Where(e => e.Ativo)
            .Select(e => new EmpresaParaLoginDto(
                e.EmpresaId,
                e.Empresa?.Nome ?? e.EmpresaId.ToString("D")))
            .DistinctBy(e => e.Id)
            .OrderBy(e => e.Nome, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<EmpresaParaLoginDto>();

        return new ListarEmpresasParaLoginResult(false, empresas);
    }

    /// <summary>#1781: mesmo custo e mesma resposta da senha errada, sem conferir senha real nenhuma.</summary>
    private CredenciaisInvalidasException RecusarSemConferir(string senha)
    {
        passwordHasher.Verify(senha, SenhaFicticia.Hash);
        return new CredenciaisInvalidasException();
    }

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(vazio)";
        var at = email.IndexOf('@');
        return at > 0 ? "***@" + email[(at + 1)..] : "(invalido)";
    }
}
