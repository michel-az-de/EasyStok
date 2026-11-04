using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.ChaveDeAcesso;

public sealed record AtivarComChaveCommand(string Chave, string? Ip, string? UserAgent) : ICommand;

/// <summary>Quem a chave identificou e em que empresa a sessão abre. A API emite a sessão com isso.</summary>
public sealed record AtivarComChaveResult(Guid UsuarioId, Guid EmpresaId, Usuario Usuario);

/// <summary>
/// #2201: troca a chave de acesso pela identidade da pessoa, uma vez. Teto por IP do <see cref="LimitePedidosAcesso"/>
/// antes de tudo; toda recusa responde igual; o consumo é o UPDATE condicional, depois das conferências e antes de
/// qualquer efeito. Não aceita convite nem mexe em senha. Nenhum log leva a chave: só o <c>UsuarioId</c>.
/// </summary>
public sealed class AtivarComChaveUseCase(
    IResetTokenRepository tokens,
    IUsuarioRepository usuarioRepository,
    IAuditLogRepository auditLogRepository,
    LimitePedidosAcesso limitePorIp,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<AtivarComChaveUseCase> logger) : IUseCase<AtivarComChaveCommand, AtivarComChaveResult>
{
    public const string MensagemDeRecusa = "Código de acesso inválido ou expirado. Peça um novo à responsável.";

    public async Task<AtivarComChaveResult> ExecuteAsync(AtivarComChaveCommand command)
    {
        await limitePorIp.ExigirAsync(command.Ip, balde: LimitePedidosAcesso.BaldeChaveDeAcesso);

        var chave = command.Chave?.Trim() ?? string.Empty;
        if (!ChavesDeAcesso.TentarLerEmpresa(chave, out var empresaId))
            throw Recusar("formato invalido");

        var agora = relogio.GetUtcNow().UtcDateTime;
        var token = await tokens.GetByTokenAsync(chave);
        if (token is null || !token.ServePara(FinalidadeResetToken.Ativacao) || token.Usado || token.ExpiraEm <= agora)
            throw Recusar("chave inexistente, usada, vencida ou de outra finalidade");

        var usuario = await usuarioRepository.GetByIdAsync(token.UsuarioId);
        if (usuario is null || !usuario.Ativo || usuario.EstaBloqueado() || usuario.EhSuperAdmin()
            || !usuario.Empresas.Any(ue => ue.Ativo && ue.EmpresaId == empresaId))
            throw Recusar($"usuario {token.UsuarioId} inexistente, inativo, bloqueado, superadmin ou sem vinculo");

        if (!await tokens.ConsumirAsync(token.Id, agora))
            throw Recusar($"chave do usuario {usuario.Id} ja consumida");

        await auditLogRepository.AddAsync(AuditLog.Criar(usuario.Id, ChavesDeAcesso.AcaoUsada, true,
            $"Entrada pela chave de acesso {ChavesDeAcesso.PrefixoDoHash(token.TokenHash)}", command.Ip, command.UserAgent));
        await unitOfWork.CommitAsync();

        logger.LogInformation("Chave de acesso usada pelo usuario {UsuarioId}", usuario.Id);
        return new AtivarComChaveResult(usuario.Id, empresaId, usuario);
    }

    private RegraDeDominioVioladaException Recusar(string motivo)
    {
        logger.LogWarning("Chave de acesso recusada: {Motivo}", motivo);
        return new RegraDeDominioVioladaException(MensagemDeRecusa);
    }
}
