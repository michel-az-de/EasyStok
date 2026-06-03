using EasyStock.Application.Demo;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Data.DemoStore;

/// <summary>
/// Semeia a loja de demonstracao no tenant atual usando os Ids deterministicos do
/// <see cref="DemoManifest"/>. Idempotente por Id (re-carregar nao duplica). So
/// ESCREVE (categorias e produtos nesta fatia; itens/movimentos/vendas entram em
/// seguida). Deve rodar numa conexao escopada por [ValidateEmpresaId]: a RLS
/// garante o isolamento por tenant, sem bypass.
/// </summary>
public sealed class DemoStoreSeeder(EasyStockDbContext db)
{
    /// <summary>Retorna quantas linhas novas foram criadas.</summary>
    public async Task<int> CarregarAsync(Guid empresaId, DateTime agora, CancellationToken ct = default)
    {
        var criados = 0;

        foreach (var c in DemoManifest.Categorias)
        {
            var id = DemoManifest.CategoriaId(empresaId, c.Slot);
            var categoria = await db.Categorias.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (categoria is null)
            {
                categoria = new Categoria { Id = id, EmpresaId = empresaId, Nome = c.Nome, CriadoEm = agora };
                db.Categorias.Add(categoria);
                criados++;
            }
            categoria.Nome = c.Nome;
            categoria.AlteradoEm = agora;
        }

        foreach (var p in DemoManifest.Produtos)
        {
            var id = DemoManifest.ProdutoId(empresaId, p.Slot);
            var produto = await db.Produtos.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (produto is null)
            {
                produto = new Produto { Id = id, EmpresaId = empresaId, Nome = p.Nome, CriadoEm = agora };
                db.Produtos.Add(produto);
                criados++;
            }
            produto.CategoriaId = DemoManifest.CategoriaId(empresaId, p.CategoriaSlot);
            produto.Nome = p.Nome;
            produto.Tipo = TipoProduto.Fisico;
            produto.Status = StatusProduto.Ativo;
            produto.CustoReferencia = Dinheiro.FromDecimal(p.Custo);
            produto.PrecoReferencia = Dinheiro.FromDecimal(p.Preco);
            produto.AlteradoEm = agora;
        }

        await db.SaveChangesAsync(ct);
        return criados;
    }
}
