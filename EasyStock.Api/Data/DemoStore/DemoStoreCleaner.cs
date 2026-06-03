using System.Data;
using EasyStock.Application.Demo;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Data.DemoStore;

/// <summary>
/// Remove a loja de demonstracao do tenant atual com seguranca: so apaga linhas
/// cujo Id pertence ao <see cref="DemoManifest"/> E que nenhuma linha viva
/// (criada pelo usuario, Id fora do manifesto) referencia. Roda numa transacao
/// SERIALIZABLE para fechar a janela TOCTOU (venda/movimento concorrente sobre um
/// alvo => conflito de serializacao e rollback, nunca perda) e sob a RLS da
/// conexao escopada por [ValidateEmpresaId] (sem bypass).
///
/// Cobre categorias e produtos (escopo do seeder atual). Itens/movimentos/vendas
/// demo entram quando o seeder os criar; a politica (DemoCleanupPlanner) ja e
/// generica por Id.
/// </summary>
public sealed class DemoStoreCleaner(EasyStockDbContext db)
{
    public sealed record ResultadoLimpeza(int ProdutosRemovidos, int CategoriasRemovidas, int ProdutosPreservados);

    public async Task<ResultadoLimpeza> LimparAsync(Guid empresaId, CancellationToken ct = default)
    {
        var idsProdutos = DemoManifest.Produtos
            .Select(p => DemoManifest.ProdutoId(empresaId, p.Slot)).ToHashSet();
        var idsCategorias = DemoManifest.Categorias
            .Select(c => DemoManifest.CategoriaId(empresaId, c.Slot)).ToHashSet();

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // Produtos demo referenciados por linha viva (item de estoque, venda ou
        // movimentacao reais) sao preservados — apagar quebraria/desreferenciaria
        // dado real.
        var refItens = await db.ItensEstoque
            .Where(i => idsProdutos.Contains(i.ProdutoId)).Select(i => i.ProdutoId).ToListAsync(ct);
        var refVendas = await db.ItensVenda
            .Where(i => idsProdutos.Contains(i.ProdutoId)).Select(i => i.ProdutoId).ToListAsync(ct);
        var refMovs = await db.MovimentacoesEstoque
            .Where(m => idsProdutos.Contains(m.ProdutoId)).Select(m => m.ProdutoId).ToListAsync(ct);
        var produtosComReferenciaViva = refItens.Concat(refVendas).Concat(refMovs).ToHashSet();

        var planoProdutos = DemoCleanupPlanner.Plan(
            new DemoCleanupRequest(idsProdutos, produtosComReferenciaViva));
        var produtosApagar = planoProdutos.Apagar.ToHashSet();

        var produtosRemovidos = produtosApagar.Count == 0
            ? 0
            : await db.Produtos.Where(p => produtosApagar.Contains(p.Id)).ExecuteDeleteAsync(ct);

        // Categoria demo so e apagada se nenhum produto (real OU demo preservado)
        // ainda a referencia apos remover os produtos demo apagaveis.
        var categoriasComProduto = (await db.Produtos
            .Where(p => idsCategorias.Contains(p.CategoriaId))
            .Select(p => p.CategoriaId).Distinct().ToListAsync(ct)).ToHashSet();
        var categoriasApagar = idsCategorias.Where(id => !categoriasComProduto.Contains(id)).ToHashSet();

        var categoriasRemovidas = categoriasApagar.Count == 0
            ? 0
            : await db.Categorias.Where(c => categoriasApagar.Contains(c.Id)).ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);

        return new ResultadoLimpeza(produtosRemovidos, categoriasRemovidas, planoProdutos.Preservar.Count);
    }
}
