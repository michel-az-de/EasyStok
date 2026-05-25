using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Fiscal;

/// <summary>
/// Busca a <see cref="CredencialIntegracao"/> vinculada ao
/// <see cref="EasyStock.Domain.Fiscal.EmpresaConfiguracaoFiscal.CertificadoCredencialId"/>.
/// Conteudo do payload e cifrado em rest (AES-256-GCM com KEK rotacionavel) —
/// decifragem real e responsabilidade do <see cref="INfeCertificadoA1Service"/>.
///
/// <para>
/// Este repositorio nao decifra, nao valida e nao retorna o .pfx em claro. Apenas
/// localiza a credencial por tenant. Multi-tenant: respeitada via QueryFilter
/// global do contexto (config fiscal e credencial integration tem EmpresaId).
/// </para>
/// </summary>
public sealed class NfeCertificadoA1Repository(EasyStockDbContext db) : ICertificadoA1Repository
{
    public async Task<CertificadoA1CredencialDto?> GetByEmpresaIdAsync(Guid empresaId, CancellationToken ct = default)
    {
        var config = await db.EmpresaConfiguracoesFiscais
            .Where(c => c.EmpresaId == empresaId)
            .Select(c => new { c.CertificadoCredencialId })
            .FirstOrDefaultAsync(ct);

        if (config?.CertificadoCredencialId is null)
            return null;

        var credencial = await db.Set<CredencialIntegracao>()
            .Where(c => c.Id == config.CertificadoCredencialId.Value
                     && c.EmpresaId == empresaId
                     && c.Ativo
                     && c.Categoria == CategoriaIntegracao.Fiscal)
            .Select(c => new CertificadoA1CredencialDto(
                c.Id,
                c.EmpresaId,
                c.KekId,
                c.PayloadCifrado,
                c.Iv,
                c.Tag,
                c.ValidoAte))
            .FirstOrDefaultAsync(ct);

        return credencial;
    }
}
