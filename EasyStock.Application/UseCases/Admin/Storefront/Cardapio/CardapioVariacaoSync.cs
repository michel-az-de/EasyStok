using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Admin.Storefront.Cardapio;

/// <summary>
/// Reconciliação das opções (CardapioItemVariacao) de um item guarda-chuva a partir do
/// payload da autoria admin (ADR-0035 / #652). Keyed-by-Id (R2#1): NÃO é replace-set cego.
///
/// <list type="bullet">
/// <item><c>opcoes == null</c>: não mexe nas opções (deixa como está).</item>
/// <item><c>Id</c> presente no payload e no banco: <c>update</c> preservando o Id.</item>
/// <item><c>Id</c> só no payload (ou ausente): <c>insert</c>.</item>
/// <item>opção no banco ausente do payload: <c>delete</c> (seguro — pedidos guardam snapshot).</item>
/// </list>
///
/// <para>A unicidade case-insensitive de rótulo é garantida no banco pela constraint
/// <c>uq_cardapio_item_variacao_rotulo</c> DEFERRABLE (checada no COMMIT), permitindo trocar
/// rótulos numa transação sem colisão transiente. EhPadrao (≤1 por item) é invariante do agregado.</para>
/// </summary>
internal static class CardapioVariacaoSync
{
    public static void Reconciliar(CardapioItem item, IReadOnlyList<CardapioItemVariacaoInput>? opcoes)
    {
        if (opcoes is null) return;

        var existentes = item.Variacoes.ToDictionary(v => v.Id);
        var idsNoPayload = opcoes.Where(o => o.Id.HasValue).Select(o => o.Id!.Value).ToHashSet();

        // delete: opções no banco que sumiram do payload
        foreach (var v in item.Variacoes.Where(v => !idsNoPayload.Contains(v.Id)).ToList())
            item.RemoverVariacao(v.Id);

        // upsert (preserva o Id no update — chaves de pedido/carrinho não invalidam)
        var resultantes = new List<(CardapioItemVariacaoInput Op, CardapioItemVariacao Variacao)>();
        foreach (var op in opcoes)
        {
            var sku = ConverterSku(op.Sku);
            if (op.Id.HasValue && existentes.TryGetValue(op.Id.Value, out var existente))
            {
                existente.Atualizar(op.Rotulo, op.PrecoStorefront, op.OrdemExibicao, op.PesoExibicao, sku);
                if (op.Disponivel) existente.MarcarDisponivel(); else existente.MarcarEsgotado();
                resultantes.Add((op, existente));
            }
            else
            {
                var nova = CardapioItemVariacao.Criar(
                    item.Id, op.Rotulo, op.PrecoStorefront,
                    ordemExibicao: op.OrdemExibicao, ehPadrao: false,
                    pesoExibicao: op.PesoExibicao, sku: sku);
                if (!op.Disponivel) nova.MarcarEsgotado();
                item.AdicionarVariacao(nova);
                resultantes.Add((op, nova));
            }
        }

        // padrão (≤1): aplica o primeiro marcado. Se nenhum marcado, mantém o padrão atual.
        var padrao = resultantes.FirstOrDefault(r => r.Op.EhPadrao);
        if (padrao.Variacao is not null)
            item.DefinirVariacaoPadrao(padrao.Variacao.Id);
    }

    private static CodigoSku? ConverterSku(string? sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) return null;
        try
        {
            return CodigoSku.From(sku.Trim());
        }
        catch (ArgumentException ex)
        {
            throw new UseCaseValidationException("SKU_INVALIDO", $"SKU de opção inválido: {ex.Message}");
        }
    }
}
