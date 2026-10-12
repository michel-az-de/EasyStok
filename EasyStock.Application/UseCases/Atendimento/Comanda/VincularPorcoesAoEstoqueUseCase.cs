using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

/// <summary>
/// D-M1-03 = a (#1529): cada porção de um prato ligado ao estoque tem a sua variação do estoque
/// (<see cref="ProdutoVariacao"/>), que é o que dá saldo próprio à porção (300 g × 800 g). Cria a
/// variação que falta, reaproveita a de mesmo nome e acompanha a troca de rótulo. Prato avulso
/// (sem produto) fica de fora: ganha o vínculo quando a primeira produção o ligar ao estoque.
/// Não chama Commit: quem chama grava junto com o item.
/// </summary>
public sealed class VincularPorcoesAoEstoqueUseCase(IProdutoVariacaoRepository variacaoRepository)
{
    public async Task ExecuteAsync(Guid empresaId, CardapioItem item)
    {
        if (item.ProdutoId is not { } produtoId || item.Variacoes.Count == 0) return;

        var doProduto = (await variacaoRepository.GetByProdutoAsync(empresaId, produtoId)).ToList();
        foreach (var porcao in item.Variacoes)
        {
            var atual = porcao.ProdutoVariacaoId is { } id ? doProduto.FirstOrDefault(v => v.Id == id) : null;
            atual ??= doProduto.FirstOrDefault(v => string.Equals(v.Nome, porcao.Rotulo, StringComparison.OrdinalIgnoreCase));
            if (atual is null)
            {
                var agora = DateTime.UtcNow;
                var sku = porcao.Sku is { } s && !await variacaoRepository.ExistsSkuAsync(empresaId, s.Value) ? s : null;
                atual = new ProdutoVariacao
                {
                    Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produtoId, Nome = porcao.Rotulo,
                    Sku = sku, Ativa = true, CriadoEm = agora, AlteradoEm = agora,
                };
                await variacaoRepository.InsertAsync(atual);
                doProduto.Add(atual);
            }
            else if (!string.Equals(atual.Nome, porcao.Rotulo, StringComparison.Ordinal))
            {
                atual.Nome = porcao.Rotulo;
                atual.AlteradoEm = DateTime.UtcNow;
                await variacaoRepository.UpdateAsync(atual);
            }
            porcao.VincularProdutoVariacao(atual.Id);
        }
    }
}
