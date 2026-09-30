using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Queries;

/// <summary>Público da campanha (S29): projeções leves, <c>EmpresaId</c> no WHERE além do RLS (ADR-0010).</summary>
public sealed class CampanhaPublicoQueries(EasyStockDbContext db) : ICampanhaPublicoQueries
{
    public async Task<IReadOnlyList<CandidatoPublicoCampanha>> ListarCandidatosAsync(
        Guid empresaId, Guid campanhaId, Guid? comprouItemId, CancellationToken ct = default)
    {
        var entregue = StatusPedidoMapper.Entregue;
        var linhas = await db.Clientes
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.Ativo)
            .OrderBy(c => c.Nome)
            .Select(c => new
            {
                c.Id,
                c.Nome,
                c.Telefone,
                c.Bloqueado,
                c.ConsentiuMarketing,
                ConsentimentoWhatsApp = db.ConsentimentosContato
                    .Where(k => k.EmpresaId == empresaId && k.ClienteId == c.Id
                        && k.Canal == CanalConversa.WhatsApp && k.Finalidade == FinalidadeContato.Marketing)
                    .Select(k => (SituacaoConsentimento?)k.Situacao)
                    .FirstOrDefault(),
                Tags = c.Tags.Where(t => t.EmpresaId == empresaId).Select(t => t.Tag).ToList(),
                UltimaCompraItemEm = comprouItemId == null
                    ? null
                    : db.Pedidos
                        .Where(p => p.EmpresaId == empresaId && p.ClienteId == c.Id && p.Status == entregue
                            && p.Itens.Any(i => i.CardapioItemId == comprouItemId || i.ProdutoId == comprouItemId))
                        .Max(p => (DateTime?)(p.EntreguEm ?? p.CriadoEm)),
                UltimaCampanhaRecebidaEm = db.CampanhaDestinatarios
                    .Where(d => d.EmpresaId == empresaId && d.ClienteId == c.Id && d.CampanhaId != campanhaId
                        && (d.Status == StatusCampanhaDestinatario.Enviado || d.Status == StatusCampanhaDestinatario.Pediu))
                    .Max(d => d.EnviadoEm),
            })
            .ToListAsync(ct);

        return linhas
            .Select(l => new CandidatoPublicoCampanha(
                l.Id,
                l.Nome,
                !string.IsNullOrWhiteSpace(l.Telefone),
                l.Bloqueado,
                // A linha do canal (S38) manda; sem linha, vale o opt-in legado do cadastro.
                l.ConsentimentoWhatsApp is { } situacao ? situacao == SituacaoConsentimento.Concedido : l.ConsentiuMarketing,
                l.Tags,
                l.UltimaCompraItemEm,
                l.UltimaCampanhaRecebidaEm,
                l.Telefone))
            .ToList();
    }

    public async Task<IReadOnlyList<DestinatarioCampanhaResumo>> ListarDestinatariosAsync(
        Guid empresaId, Guid campanhaId, StatusCampanhaDestinatario? status, int limite, CancellationToken ct = default)
    {
        var query = db.CampanhaDestinatarios
            .AsNoTracking()
            .Where(d => d.EmpresaId == empresaId && d.CampanhaId == campanhaId);
        if (status is { } s) query = query.Where(d => d.Status == s);

        var linhas = await query
            .Join(db.Clientes.Where(c => c.EmpresaId == empresaId), d => d.ClienteId, c => c.Id,
                (d, c) => new { d.ClienteId, c.Nome, d.Status, d.MotivoExclusao, d.Onda, d.EnviadoEm })
            .OrderBy(x => x.Nome)
            .ThenBy(x => x.ClienteId)
            .Take(Math.Clamp(limite, 1, 1000))
            .ToListAsync(ct);

        return linhas
            .Select(l => new DestinatarioCampanhaResumo(l.ClienteId, l.Nome, l.Status, l.MotivoExclusao, l.Onda, l.EnviadoEm))
            .ToList();
    }
}
