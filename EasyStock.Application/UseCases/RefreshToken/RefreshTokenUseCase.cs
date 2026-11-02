using EasyStock.Application.UseCases.AutenticarUsuario;
using AuditLogEntity = EasyStock.Domain.Entities.AuditLog;
using RefreshTokenEntity = EasyStock.Domain.Entities.RefreshToken;
using UsuarioEntity = EasyStock.Domain.Entities.Usuario;

namespace EasyStock.Application.UseCases.RefreshToken;

public sealed class RefreshTokenUseCase(
    IRefreshTokenRepository refreshTokenRepository,
    IUsuarioRepository usuarioRepository,
    IAuditLogRepository auditLogRepository,
    IJwtTokenService jwtTokenService,
    IUnitOfWork unitOfWork,
    ILogger<RefreshTokenUseCase> logger,
    Ports.Output.Auth.IProtetorRefreshNativo? protetor = null) : IUseCase<RefreshTokenCommand, RefreshTokenResult>
{
    public async Task<RefreshTokenResult> ExecuteAsync(RefreshTokenCommand command)
    {
        logger.LogInformation("Iniciando refresh token");

        var tokenHash = TokenHashHelper.ComputeSha256Hash(command.RefreshToken);
        var refreshToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash);
        if (refreshToken?.SessaoNativaId is { } sessaoId)
            return await unitOfWork.ExecuteInTransactionSemRetryAsync(async _ =>
            {
                var sessao = await refreshTokenRepository.ObterSessaoNativaComLockAsync(sessaoId);
                if (sessao is null || sessao.RevogadaEm is not null || sessao.UsuarioId != refreshToken.UsuarioId
                    || await refreshTokenRepository.DispositivoRevogadoAsync(sessao.DispositivoId))
                    throw new CredenciaisInvalidasException("Sessão do aparelho encerrada.");
                if (command.RequisicaoId == Guid.Empty)
                    throw new UseCaseValidationException("requisicaoId inválido.");
                var usuario = await ObterUsuarioValidoAsync(refreshToken.UsuarioId, sessao);
                if (command.RequisicaoId is { } requisicaoId && sessao.UltimaRenovacaoId == requisicaoId)
                {
                    if (sessao.RefreshAnteriorId != refreshToken.Id || sessao.RefreshSucessorProtegido is null)
                        throw new CredenciaisInvalidasException("Requisição de renovação não corresponde ao refresh.");
                    var recuperado = Protetor().Desproteger(sessao.Id, sessao.RefreshSucessorProtegido);
                    var sucessor = await refreshTokenRepository.GetByTokenHashAsync(TokenHashHelper.ComputeSha256Hash(recuperado));
                    if (sucessor is null || sucessor.SessaoNativaId != sessao.Id || sucessor.UsuarioId != usuario.Id || !sucessor.EstaValido())
                        throw new CredenciaisInvalidasException("A renovação já foi substituída ou encerrada.");
                    return EmitirResultado(usuario, sessao, recuperado);
                }
                if (!await refreshTokenRepository.ConsumirAsync(refreshToken.Id, DateTime.UtcNow))
                    throw new CredenciaisInvalidasException("Refresh já utilizado.");
                return await RenovarAsync(refreshToken, sessao, command.RequisicaoId, usuario);
            });
        return await RenovarAsync(refreshToken, null);
    }

    private async Task<RefreshTokenResult> RenovarAsync(RefreshTokenEntity? refreshToken, SessaoNativa? sessao,
        Guid? requisicaoId = null, UsuarioEntity? usuarioConferido = null)
    {
        if (refreshToken == null || !refreshToken.EstaValido())
        {
            logger.LogWarning("Refresh token invalido ou expirado");
            throw new CredenciaisInvalidasException("Refresh token invalido.");
        }

        var usuario = usuarioConferido ?? await ObterUsuarioValidoAsync(refreshToken.UsuarioId, sessao);

        if (sessao is null)
        {
            refreshToken.Revogar();
            await refreshTokenRepository.UpdateAsync(refreshToken);
        }

        var novoRefreshTokenValue = sessao is null ? Guid.NewGuid().ToString()
            : Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var novoTokenHash = TokenHashHelper.ComputeSha256Hash(novoRefreshTokenValue);
        var expiraEm = sessao is null ? DateTime.UtcNow.AddDays(7) : DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        var novoRefreshToken = RefreshTokenEntity.Criar(
            usuario.Id,
            novoTokenHash,
            expiraEm,
            null,
            null);
        novoRefreshToken.SessaoNativaId = sessao?.Id;
        await refreshTokenRepository.AddAsync(novoRefreshToken);

        if (sessao is not null)
            await refreshTokenRepository.SalvarReciboRenovacaoAsync(sessao.Id, requisicaoId,
                requisicaoId.HasValue ? refreshToken.Id : null,
                requisicaoId.HasValue ? Protetor().Proteger(sessao.Id, novoRefreshTokenValue) : null);

        var resultado = EmitirResultado(usuario, sessao, novoRefreshTokenValue);
        await auditLogRepository.AddAsync(AuditLogEntity.Criar(usuario.Id, "refresh", true,
            "Token renovado com sucesso", null, null));
        await unitOfWork.CommitAsync();
        logger.LogInformation("Refresh token executado com sucesso para usuario {UsuarioId}", usuario.Id);
        return resultado;
    }

    private Ports.Output.Auth.IProtetorRefreshNativo Protetor() =>
        protetor ?? throw new InvalidOperationException("Proteção de renovação nativa indisponível.");

    private async Task<UsuarioEntity> ObterUsuarioValidoAsync(Guid usuarioId, SessaoNativa? sessao)
    {
        var usuario = await usuarioRepository.GetByIdAsync(usuarioId);
        if (usuario is null || !usuario.Ativo) throw new CredenciaisInvalidasException("Usuario inativo.");
        if (sessao is not null && (usuario.SessoesValidasDesde is { } corte && sessao.CriadaEm <= corte
            || !usuario.EhSuperAdmin() && !usuario.Empresas.Any(e => e.EmpresaId == sessao.EmpresaId && e.Ativo)))
            throw new CredenciaisInvalidasException("Acesso à empresa revogado.");
        return usuario;
    }

    private RefreshTokenResult EmitirResultado(UsuarioEntity usuario, SessaoNativa? sessao, string refreshTokenValue)
    {
        var empresaId = sessao?.EmpresaId ?? ResolveEmpresaIdPadrao(usuario);

        var nivel = NivelAcesso.Visualizador;
        IReadOnlyCollection<Permissao> permissoes = [];
        string? moduloInicial = null;
        var permissoesExplicitas = false;
        var sessaoPersistente = false;

        if (empresaId.HasValue && usuario.Perfis is not null)
        {
            var perfil = usuario.Perfis
                .Where(p => p.EmpresaId == empresaId.Value || sessao is not null && p.Perfil != null && p.Perfil.Nivel == NivelAcesso.SuperAdmin)
                .OrderBy(p => p.Perfil != null ? (int)p.Perfil.Nivel : int.MaxValue)
                .FirstOrDefault();
            if (perfil?.Perfil is not null)
            {
                nivel = perfil.Perfil.Nivel;
                moduloInicial = perfil.Perfil.ModuloInicial;
                permissoesExplicitas = perfil.Perfil.PermissoesExplicitas;
                sessaoPersistente = Domain.Services.PerfisCasaDaBaba.PermiteSessaoPersistente(perfil.Perfil, empresaId.Value);
                permissoes = perfil.Perfil.Permissoes?.Select(p => p.Permissao).Distinct().ToArray() ?? [];
            }
        }

        var autenticarResult = new AutenticarUsuarioResult(
            usuario.Id,
            empresaId,
            usuario.Nome,
            usuario.Email,
            nivel,
            permissoes,
            moduloInicial,
            permissoesExplicitas,
            sessao is not null || sessaoPersistente,
            sessao?.Id,
            sessao?.DispositivoId);
        var accessToken = jwtTokenService.GerarToken(autenticarResult);

        return new RefreshTokenResult(accessToken, refreshTokenValue, jwtTokenService.ExpiresInSeconds);
    }

    private static Guid? ResolveEmpresaIdPadrao(UsuarioEntity usuario)
    {
        var empresasAtivas = usuario.Empresas?
            .Where(e => e.Ativo)
            .Select(e => e.EmpresaId)
            .Distinct()
            .ToArray() ?? [];

        return empresasAtivas.Length == 1 ? empresasAtivas[0] : null;
    }
}
