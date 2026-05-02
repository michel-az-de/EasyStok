using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.ExportarMeusDados;

public sealed record ExportarMeusDadosResult(
    DateTime GeradoEm,
    UsuarioExport Usuario,
    IReadOnlyCollection<EmpresaExport> Empresas,
    IReadOnlyCollection<TokenAtivoExport> RefreshTokensAtivos);

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
    DateTime? UltimoAcessoEm);

public sealed record EmpresaExport(Guid EmpresaId, string? Nome);

public sealed record TokenAtivoExport(Guid Id, DateTime CriadoEm, DateTime ExpiraEm);

/// <summary>
/// LGPD Art. 18 — direito de acesso/portabilidade. Devolve um snapshot dos
/// dados pessoais do usuario autenticado em formato estruturado.
/// </summary>
public sealed class ExportarMeusDadosUseCase(
    IUsuarioRepository usuarioRepository,
    IUsuarioEmpresaRepository usuarioEmpresaRepository,
    IRefreshTokenRepository refreshTokenRepository,
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
                usuario.UltimoAcessoEm),
            Empresas: empresas
                .Select(ue => new EmpresaExport(ue.EmpresaId, ue.Empresa?.Nome))
                .ToList(),
            RefreshTokensAtivos: refreshTokens
                .Where(rt => rt.ExpiraEm > agora && rt.RevogadoEm is null)
                .Select(rt => new TokenAtivoExport(rt.Id, rt.CriadoEm, rt.ExpiraEm))
                .ToList());

        logger.LogInformation("AUDIT: Export LGPD gerado para usuario {UsuarioId}.", usuarioId);
        return result;
    }
}
