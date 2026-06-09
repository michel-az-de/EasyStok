namespace EasyStock.Application.UseCases.AutenticarUsuario;

/// <summary>
/// Valida credenciais e retorna a lista de Empresas disponíveis para o usuário
/// (step 1 do login 2-etapas para usuários não-SuperAdmin). NÃO emite JWT
/// nem atualiza o último acesso — é apenas uma pré-validação para popular
/// o seletor de Empresa na tela de login.
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
        if (usuario is null || !usuario.Ativo)
            throw new CredenciaisInvalidasException();

        if (usuario.EstaBloqueado())
            throw new CredenciaisInvalidasException("Conta bloqueada temporariamente.");

        var senhaOk = passwordHasher.Verify(command.Senha, usuario.SenhaHash);
        if (!senhaOk)
            throw new CredenciaisInvalidasException();

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

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(vazio)";
        var at = email.IndexOf('@');
        return at > 0 ? "***@" + email[(at + 1)..] : "(invalido)";
    }
}
