using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Services;
using EasyStock.Domain.Storefront.Frete;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Queries;

/// <summary>
/// Contagens da prontidão dos passos essenciais (#2070), lidas das mesmas tabelas dos repositórios do expediente, da
/// vitrine, do cardápio, das janelas, das zonas e dos usuários. Consultas de uma linha ou COUNT, com <c>EmpresaId</c>
/// explícito (a vitrine é achada pela empresa; janelas, zonas e itens, pela vitrine achada) além do RLS (ADR-0010).
/// </summary>
public sealed class RetratoDaConfiguracaoInicialQuery(EasyStockDbContext db) : IRetratoDaConfiguracaoInicialQuery
{
    public async Task<RetratoDaConfiguracaoInicial> ObterAsync(Guid empresaId, CancellationToken ct = default)
    {
        var expediente = await db.ExpedientesLoja.AsNoTracking().FirstOrDefaultAsync(e => e.EmpresaId == empresaId, ct);
        var vitrine = await db.Storefronts.AsNoTracking().FirstOrDefaultAsync(s => s.EmpresaId == empresaId, ct);
        var equipe = await EquipeAtivaPorNivelAsync(empresaId, ct);

        var (itens, janelas, zonas) = vitrine is null
            ? (0, 0, 0)
            : (await ItensVisiveisComPrecoAsync(empresaId, vitrine.Id, ct),
               await db.JanelasEntrega.CountAsync(j => j.StorefrontId == vitrine.Id && j.Ativa, ct),
               await db.FreteZonas.CountAsync(z => z.StorefrontId == vitrine.Id && z.Ativa, ct));

        return new RetratoDaConfiguracaoInicial(
            HorariosGravados: expediente is not null,
            DiasAbertos: expediente?.Horarios.Select(h => h.DiaDaSemana).Distinct().Count() ?? 0,
            TemVitrine: vitrine is not null,
            TemLogo: !string.IsNullOrWhiteSpace(vitrine?.LogoUrl),
            ItensVisiveisComPreco: itens,
            JanelasAtivas: janelas,
            ZonasAtivas: zonas,
            RaioConfigurado: vitrine is not null && FreteRaioConfigFactory.TentarCriar(vitrine) is not null,
            EquipeAtivaPorNivel: equipe);
    }

    /// <summary>
    /// Itens no site (visíveis e não arquivados, como o cardápio público) com preço próprio, de porção ou do produto. O
    /// preço do produto é um valor convertido (<c>Dinheiro</c>): só os ids sem preço próprio voltam do banco para conferir.
    /// </summary>
    private async Task<int> ItensVisiveisComPrecoAsync(Guid empresaId, Guid vitrineId, CancellationToken ct)
    {
        var noSite = db.CardapioItens.AsNoTracking()
            .Where(c => c.StorefrontId == vitrineId && c.Visivel && c.ArquivadoEm == null);
        var comPrecoProprio = await noSite
            .CountAsync(c => c.PrecoStorefront > 0 || c.Variacoes.Any(v => v.PrecoStorefront > 0), ct);

        var produtosSemPrecoProprio = await noSite
            .Where(c => c.ProdutoId != null && !(c.PrecoStorefront > 0) && !c.Variacoes.Any(v => v.PrecoStorefront > 0))
            .Select(c => c.ProdutoId!.Value)
            .ToListAsync(ct);
        if (produtosSemPrecoProprio.Count == 0) return comPrecoProprio;

        var precos = await db.Produtos.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && produtosSemPrecoProprio.Contains(p.Id))
            .Select(p => new { p.Id, p.PrecoReferencia })
            .ToDictionaryAsync(p => p.Id, p => p.PrecoReferencia, ct);
        return comPrecoProprio + produtosSemPrecoProprio.Count(id => precos.GetValueOrDefault(id) is { Valor: > 0 });
    }

    /// <summary>
    /// Pessoas com vínculo e conta ativos, pelo nível do perfil atribuído nesta empresa (cada pessoa conta uma vez por
    /// nível). Perfil padrão sem empresa fica fora pelo filtro do tenant; a equipe da casa usa os perfis da empresa.
    /// </summary>
    private async Task<IReadOnlyDictionary<NivelAcesso, int>> EquipeAtivaPorNivelAsync(Guid empresaId, CancellationToken ct)
    {
        var linhas = await (
                from v in db.UsuariosEmpresas.AsNoTracking()
                where v.EmpresaId == empresaId && v.Ativo && v.Usuario!.Ativo
                join up in db.UsuariosPerfis.AsNoTracking() on new { v.UsuarioId, v.EmpresaId } equals new { up.UsuarioId, up.EmpresaId }
                select new { v.UsuarioId, up.Perfil!.Nivel })
            .Distinct()
            .ToListAsync(ct);
        return linhas.GroupBy(l => l.Nivel).ToDictionary(g => g.Key, g => g.Count());
    }
}
