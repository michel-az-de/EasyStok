namespace EasyStock.Application.Reporting;

/// <summary>
/// Regra única de acesso a um relatório, aplicada no enqueue, no preview e no data (#1508).
/// Antes só o enqueue conferia <see cref="IReportDefinition.PermissaoRequerida"/>; preview e data
/// executavam o handler para qualquer autenticado.
/// </summary>
public static class ReportAcesso
{
    public static void Garantir(ICurrentUserAccessor usuario, IReportDefinition definicao)
    {
        if (usuario.Nivel != NivelAcesso.SuperAdmin && !TemPermissao(usuario, definicao.PermissaoRequerida))
            throw new UnauthorizedAccessException($"Sem permissão para o relatório '{definicao.Key}'.");
    }

    // Permissão simples via NivelAcesso ou Claims — verifica por nome
    private static bool TemPermissao(ICurrentUserAccessor usuario, string permissaoRequerida) =>
        usuario.Nivel == NivelAcesso.SuperAdmin;
}
