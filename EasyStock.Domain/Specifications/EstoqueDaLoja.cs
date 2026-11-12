using System.Linq.Expressions;

namespace EasyStock.Domain.Specifications;

/// <summary>
/// #2318: regra unica de "estoque que pertence a uma loja", a mesma da baixa de venda. Item sem loja
/// (LojaId nulo: nao ha backfill e varias rotas criam assim) pertence a qualquer loja da empresa;
/// consulta sem loja enxerga a empresa toda. Usada pelo repositorio (Web), divergencia, Reconcile e painel.
/// </summary>
public static class EstoqueDaLoja
{
    public static Expression<Func<ItemEstoque, bool>> Pertence(Guid? lojaId) =>
        lojaId is null || lojaId == Guid.Empty
            ? i => true
            : i => i.LojaId == lojaId || i.LojaId == null;
}
