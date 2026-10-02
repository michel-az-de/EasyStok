using EasyStock.Application.Ports.Output.Notifications;

namespace EasyStock.Application.UseCases.ExportarMeusDados;

public sealed record ExportarMeusDadosResult(
    DateTime GeradoEm,
    UsuarioExport Usuario,
    IReadOnlyCollection<EmpresaExport> Empresas,
    IReadOnlyCollection<TokenAtivoExport> RefreshTokensAtivos,
    IReadOnlyCollection<ConsentimentoExport> Consentimentos,
    IReadOnlyCollection<PreferenciaExport> Preferencias);

public sealed record UsuarioExport(
    Guid Id,
    string Nome,
    string Email,
    string? AvatarUrl,
    string TemaPreferido,
    bool Ativo,
    bool EmailConfirmado,
    DateTime CriadoEm,
    DateTime AlteradoEm,
    DateTime? UltimoAcessoEm,
    string? Telefone,
    DateTime? TelefoneVerificadoEm,
    string? EmailPendente);

public sealed record EmpresaExport(Guid EmpresaId, string? Nome);

public sealed record TokenAtivoExport(Guid Id, DateTime CriadoEm, DateTime ExpiraEm);

/// <summary>Opt-in de notificação (N4): canal, categoria, estado atual, quando e por quem mudou, e o IP de origem.</summary>
public sealed record ConsentimentoExport(
    string Canal, string Categoria, bool OptIn, DateTime AtualizadoEm, string AtualizadoPor, string? IpOrigem, string? MotivoOptOut);

public sealed record PreferenciaExport(Guid EmpresaId, string RotinaCodigo, bool Habilitada, string? CanalPreferido, DateTime AtualizadaEm);

/// <summary>
/// LGPD Art. 18 — direito de acesso/portabilidade. Devolve um snapshot dos
/// dados pessoais do usuario autenticado em formato estruturado.
/// </summary>
public sealed class ExportarMeusDadosUseCase(
    IUsuarioRepository usuarioRepository,
    IUsuarioEmpresaRepository usuarioEmpresaRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IConsentimentoRepository consentimentoRepository,
    IPreferenciaNotificacaoRepository preferenciaRepository,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<ExportarMeusDadosUseCase> logger)
{
    public async Task<ExportarMeusDadosResult> ExecuteAsync()
    {
        var usuarioId = currentUserAccessor.UsuarioId;
        if (usuarioId == Guid.Empty)
            throw new UsuarioNaoAutorizadoException("Usuario nao autenticado.");

        var usuario = await usuarioRepository.GetByIdAsync(usuarioId)
            ?? throw new RegraDeDominioVioladaException("Usuario nao encontrado.");

        var empresas = await usuarioEmpresaRepository.GetByUsuarioIdAsync(usuarioId);
        var refreshTokens = await refreshTokenRepository.GetByUsuarioIdAsync(usuarioId);

        var consentimentos = await consentimentoRepository.ListarPorUsuarioAsync(usuarioId);
        var preferencias = await preferenciaRepository.ListarPorUsuarioAsync(usuarioId);

        var agora = DateTime.UtcNow;

        var result = new ExportarMeusDadosResult(
            GeradoEm: agora,
            Usuario: new UsuarioExport(
                usuario.Id,
                usuario.Nome,
                usuario.Email,
                usuario.AvatarUrl,
                usuario.TemaPreferido,
                usuario.Ativo,
                usuario.EmailConfirmado,
                usuario.CriadoEm,
                usuario.AlteradoEm,
                usuario.UltimoAcessoEm,
                usuario.Telefone?.Value,
                usuario.TelefoneVerificadoEm,
                usuario.EmailPendente),
            Empresas: empresas
                .Select(ue => new EmpresaExport(ue.EmpresaId, ue.Empresa?.Nome))
                .ToList(),
            RefreshTokensAtivos: refreshTokens
                .Where(rt => rt.ExpiraEm > agora && rt.RevogadoEm is null)
                .Select(rt => new TokenAtivoExport(rt.Id, rt.CriadoEm, rt.ExpiraEm))
                .ToList(),
            Consentimentos: consentimentos
                .Select(c => new ConsentimentoExport(
                    c.Canal.ToString(), c.Categoria.ToString(), c.OptIn, c.AtualizadoEm, c.AtualizadoPor, c.IpOrigem, c.MotivoOptOut))
                .ToList(),
            Preferencias: preferencias
                .Select(p => new PreferenciaExport(
                    p.EmpresaId, p.RotinaCodigo, p.Habilitada, p.CanalPreferido?.ToString(), p.AtualizadaEm))
                .ToList());

        logger.LogInformation("AUDIT: Export LGPD gerado para usuario {UsuarioId}.", usuarioId);
        return result;
    }
}
