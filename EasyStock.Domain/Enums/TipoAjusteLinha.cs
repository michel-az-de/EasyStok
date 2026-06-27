namespace EasyStock.Domain.Enums
{
    /// <summary>Classificacao de cada linha de ajuste aplicado (provenance do apply).</summary>
    public enum TipoAjusteLinha
    {
        /// <summary>Lote existente: contado &lt; esperado (delta negativo).</summary>
        Falta,

        /// <summary>Lote existente: contado &gt; esperado (delta positivo).</summary>
        Sobra,

        /// <summary>Descoberto: nao existia ItemEstoque; criado no apply.</summary>
        LoteNovo,

        /// <summary>Lote existente nao encontrado fisicamente: contado = 0.</summary>
        LoteZerado
    }
}
