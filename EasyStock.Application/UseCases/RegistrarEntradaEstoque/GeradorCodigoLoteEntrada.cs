namespace EasyStock.Application.UseCases.RegistrarEntradaEstoque
{
    /// <summary>
    /// Gera o codigo de lote de uma ENTRADA de estoque no formato
    /// <c>LOTE-{SKU}-{AAMMDD}-{NNN}</c> (prefixo + sufixo + numeracao por data).
    /// NNN e o proximo sequencial livre do dia para aquele SKU (dedup via repositorio).
    /// Cabe no limite de 40 chars da coluna <c>lotes.codigo</c> e nos caracteres aceitos
    /// por <see cref="EasyStock.Domain.ValueObjects.CodigoLote"/> ([A-Z0-9-]).
    /// </summary>
    public static class GeradorCodigoLoteEntrada
    {
        /// <summary>Reduz o SKU a [A-Z0-9] (max 20), garantindo codigo final &lt;= 40 chars e VO valido.</summary>
        public static string Sanitizar(string? sku)
        {
            if (string.IsNullOrWhiteSpace(sku)) return "GERAL";
            var s = new string(sku.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (s.Length == 0) return "GERAL";
            return s.Length > 20 ? s[..20] : s;
        }

        /// <summary>
        /// Proximo codigo livre para a data informada (use a data civil de Brasilia).
        /// Faz dedup probando LOTE-SKU-AAMMDD-001, -002, ... ate achar um inexistente.
        /// </summary>
        public static async Task<string> GerarAsync(ILoteRepository repo, Guid empresaId, string? sku, DateTime data)
        {
            var prefixo = $"LOTE-{Sanitizar(sku)}-{data:yyMMdd}-";
            for (var seq = 1; seq <= 999; seq++)
            {
                var codigo = prefixo + seq.ToString("D3");
                if (await repo.FindByCodigoAsync(empresaId, codigo) is null)
                    return codigo;
            }
            // Volume extremo no mesmo dia/SKU: cai no segundo-do-dia (deterministico, sem random).
            return prefixo + ((int)data.TimeOfDay.TotalSeconds).ToString("D5");
        }
    }
}
