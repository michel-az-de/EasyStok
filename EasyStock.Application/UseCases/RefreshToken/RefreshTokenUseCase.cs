using EasyStock.Application.Ports.Output.Security;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.Services.Auth;
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
    Ports.Output.Auth.IProtetorRefreshNativo? protetor = null,
    IRowLevelSecurityBypass? rlsBypass = null) : IUseCase<RefreshTokenCommand, RefreshTokenResult>
{
    public async Task<RefreshTokenResult> ExecuteAsync(RefreshTokenCommand command)
    {
        logger.LogInformation("Iniciando refresh token");

        var tokenHash = TokenHashHelper.ComputeSha256Hash(command.RefreshToken);
        var refreshToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash);
        if (refreshToken?.SessaoNativaId is { } sessaoId)
        {
            // O segredo identifica a família antes de existir JWT/tenant; escopo termina após Commit/rollback.
            await using var rls = rlsBypass is null ? null : await rlsBypass.BeginAsync();
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
        }
        // #2745: consumo e sucessor na mesma transação; uma falha depois do consumo não queima o token sem sucessor.
        return await unitOfWork.ExecuteInTransactionSemRetryAsync(_ => RenovarAsync(refreshToken, null));
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

        // #2745: UPDATE condicional, como no ramo nativo; de duas renovações simultâneas só uma consome o token.
        // Em READ COMMITTED a perdedora espera a trava da linha e, ao reavaliar !Revogado, afeta 0 linhas.
        if (sessao is null && !await refreshTokenRepository.ConsumirAsync(refreshToken.Id, DateTime.UtcNow))
        {
            logger.LogWarning("Refresh token já consumido por outra renovação");
            throw new CredenciaisInvalidasException("Refresh token invalido.");
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
            null,
            refreshToken.EmpresaDaSessaoId);
        novoRefreshToken.SessaoNativaId = sessao?.Id;
        await refreshTokenRepository.AddAsync(novoRefreshToken);

        if (sessao is not null)
            await refreshTokenRepository.SalvarReciboRenovacaoAsync(sessao.Id, requisicaoId,
                requisicaoId.HasValue ? refreshToken.Id : null,
                requisicaoId.HasValue ? Protetor().Proteger(sessao.Id, novoRefreshTokenValue) : null);

        var resultado = EmitirResultado(usuario, sessao, novoRefreshTokenValue, refreshToken.EmpresaDaSessaoId);
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

    private RefreshTokenResult EmitirResultado(
        UsuarioEntity usuario, SessaoNativa? sessao, string refreshTokenValue, Guid? empresaDaSessaoWeb = null)
    {
        // M3 (#2070): a sessão web renova pela regra do login, na empresa em que foi aberta; o suporte mantém nível e
        // empresa. Token antigo (sem a coluna) segue a única empresa ativa. Sem vínculo ativo com a empresa da sessão o
        // resolvedor recusa, e a transação desfaz o consumo do token. A sessão do aparelho segue a empresa gravada nela.
        var autenticarResult = sessao is null
            ? ResolvedorDeSessao.Resolver(
                usuario, empresaDaSessaoWeb ?? ResolvedorDeSessao.EmpresaPadrao(usuario), empresaDoSuporte: empresaDaSessaoWeb)
            : ResultadoDaSessaoNativa(usuario, sessao);
        var accessToken = jwtTokenService.GerarToken(autenticarResult);

        return new RefreshTokenResult(accessToken, refreshTokenValue, jwtTokenService.ExpiresInSeconds);
    }

    /// <summary>Sessão do aparelho: empresa, sessão e dispositivo vêm da sessão nativa, sempre persistente.</summary>
    private static AutenticarUsuarioResult ResultadoDaSessaoNativa(UsuarioEntity usuario, SessaoNativa sessao)
    {
        Guid? empresaId = sessao.EmpresaId;

        var nivel = NivelAcesso.Visualizador;
        IReadOnlyCollection<Permissao> permissoes = [];
        string? moduloInicial = null;
        var permissoesExplicitas = false;
        var sessaoPersistente = false;

        if (empresaId.HasValue && usuario.Perfis is not null)
        {
            var perfil = usuario.Perfis
                .Where(p => p.EmpresaId == empresaId.Value || p.Perfil is { EhSuporteGlobal: true })
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

        return new AutenticarUsuarioResult(
            usuario.Id,
            empresaId,
            usuario.Nome,
            usuario.Email,
            nivel,
            permissoes,
            moduloInicial,
            permissoesExplicitas,
            true,
            sessao.Id,
            sessao.DispositivoId);
    }
}
