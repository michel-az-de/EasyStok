using EasyStock.Application.Ports.Output.Atendimento.Email;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

/// <summary>Consultas da entrada de e-mail do atendimento (#1432).</summary>
public sealed class EmailAtendimentoQuery(EasyStockDbContext db) : IEmailAtendimentoQuery
{
    public async Task<IReadOnlyList<Guid>> ListarEmpresasComCaixaAsync(CancellationToken ct = default) =>
        // Varredura cross-tenant do job: quem chama liga o bypass de RLS; o filtro global sai pelo mesmo motivo.
        await db.Set<CredencialIntegracao>()
            .IgnoreQueryFilters()
            .Where(c => c.ProviderKey == CaixaEmailAtendimento.ProviderKey
                        && c.Ambiente == AmbienteIntegracao.Production && c.Ativo)
            .Select(c => c.EmpresaId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<Guid?> ObterClienteIdPorEmailAsync(Guid empresaId, string email, CancellationToken ct = default)
    {
        var alvo = email.Trim().ToLowerInvariant();
        if (alvo.Length == 0)
            return null;

        // #3101: compara a coluna gerada email_lower (migração AddClienteEmailLower), do índice
        // (EmpresaId, email_lower); lower("Email") em LINQ não usa índice sob RLS. Só o Id sai na
        // projeção: xmin está mapeado em ClienteConfiguration e o SELECT * não o traz, então a
        // entidade nunca pode ser materializada. O filtro global de tenant vale por cima do FromSql.
        return await db.Clientes
            .FromSql($"""SELECT * FROM clientes WHERE "EmpresaId" = {empresaId} AND email_lower = {alvo}""")
            .AsNoTracking()
            .Where(c => c.Ativo)
            .OrderBy(c => c.CriadoEm)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
    }
}
