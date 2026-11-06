using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.ChaveDeAcesso;

public sealed record EmitirChaveDeAcessoCommand(Guid UsuarioId) : ICommand;

/// <summary>O texto da chave sai uma vez, nesta resposta. Depois só o prefixo do hash identifica a chave.</summary>
public sealed record ChaveDeAcessoEmitida(string Chave, DateTime ExpiraEm, string PrefixoDoHash);

/// <summary>
/// #2201: a dona (ou o superadmin) emite a chave de acesso de uso único de uma pessoa da casa. Emitir de novo revoga a
/// anterior. Mesma guarda de tenant do <c>ReenviarConviteUseCase</c>, com a mesma mensagem do não encontrado para não
/// confirmar a existência entre empresas. Superadmin nunca é alvo.
/// </summary>
public sealed class EmitirChaveDeAcessoUseCase(
    IUsuarioRepository usuarioRepository,
    IResetTokenRepository tokens,
    ICurrentUserAccessor currentUser,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<EmitirChaveDeAcessoUseCase> logger) : IUseCase<EmitirChaveDeAcessoCommand, ChaveDeAcessoEmitida>
{
    private const string NaoEncontrado = "Usuario nao encontrado.";

    public async Task<ChaveDeAcessoEmitida> ExecuteAsync(EmitirChaveDeAcessoCommand command)
    {
        var (usuario, empresaId) = await AlvoAsync(command.UsuarioId);
        var agora = relogio.GetUtcNow().UtcDateTime;

        await tokens.InvalidarChavesDeAcessoAbertasAsync(usuario.Id);

        var chave = ChavesDeAcesso.Gerar(empresaId);
        var hash = ChavesDeAcesso.HashDe(chave);
        var expiraEm = agora.Add(ChavesDeAcesso.Validade);
        await tokens.AddAsync(ResetToken.Criar(usuario.Id, hash, expiraEm, currentUser.Ip, currentUser.UserAgent,
            FinalidadeResetToken.Ativacao, canal: "Manual", criadoEm: agora));

        var prefixo = ChavesDeAcesso.PrefixoDoHash(hash);
        var autor = await RastroDeUsuarios.AutorAsync(usuarioRepository, currentUser);
        await auditLogRepository.AddAsync(RastroDeUsuarios.Linha(usuario.Id, ChavesDeAcesso.AcaoEmitida,
            $"Chave de acesso {prefixo} emitida por {autor}, vale até {expiraEm:yyyy-MM-dd}", currentUser, empresaId));
        await unitOfWork.CommitAsync();

        logger.LogInformation("Chave de acesso {Prefixo} emitida para o usuario {UsuarioId} por {Executor}",
            prefixo, usuario.Id, currentUser.UsuarioId);
        return new ChaveDeAcessoEmitida(chave, expiraEm, prefixo);
    }

    /// <summary>Revoga as chaves abertas da pessoa, sem emitir outra.</summary>
    public async Task<int> RevogarAsync(Guid usuarioId)
    {
        var (usuario, empresaId) = await AlvoAsync(usuarioId);
        var revogadas = await tokens.InvalidarChavesDeAcessoAbertasAsync(usuario.Id);
        var autor = await RastroDeUsuarios.AutorAsync(usuarioRepository, currentUser);
        await auditLogRepository.AddAsync(RastroDeUsuarios.Linha(usuario.Id, ChavesDeAcesso.AcaoRevogada,
            $"Chave de acesso revogada por {autor} ({revogadas} aberta(s))", currentUser, empresaId));
        await unitOfWork.CommitAsync();
        return revogadas;
    }

    private async Task<(Usuario Usuario, Guid EmpresaId)> AlvoAsync(Guid usuarioId)
    {
        var usuario = await usuarioRepository.GetByIdAsync(usuarioId)
            ?? throw new UseCaseValidationException(NaoEncontrado);

        // A empresa da sessão que a chave vai abrir: a do token de quem emite; o superadmin usa a única ativa da pessoa.
        Guid? empresaId = currentUser.EmpresaId != Guid.Empty
            ? currentUser.EmpresaId
            : currentUser.Nivel == NivelAcesso.SuperAdmin ? EmpresaAtivaUnica(usuario) : null;

        if (empresaId is null || !usuario.Empresas.Any(ue => ue.Ativo && ue.EmpresaId == empresaId))
            throw new UseCaseValidationException(NaoEncontrado);
        if (usuario.EhSuperAdmin())
            throw new UseCaseValidationException("CHAVE_SUPERADMIN", "Superadmin não recebe chave de acesso.");
        if (!usuario.Ativo)
            throw new UseCaseValidationException("CHAVE_USUARIO_INATIVO", "Este usuário está desativado.");
        // Revisão de segurança: um Admin não abre a sessão de outro Admin (a dona) sem a senha dele. Só o suporte emite.
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && usuario.Perfis.Any(up => up.Perfil is { Nivel: <= NivelAcesso.Admin }))
            throw new UseCaseValidationException("CHAVE_NIVEL_ALTO", "Só o suporte gera código de acesso para um administrador.");

        return (usuario, empresaId.Value);
    }

    private static Guid? EmpresaAtivaUnica(Usuario usuario)
    {
        var ativas = usuario.Empresas.Where(ue => ue.Ativo).Select(ue => ue.EmpresaId).Distinct().ToArray();
        return ativas.Length == 1 ? ativas[0] : null;
    }
}
