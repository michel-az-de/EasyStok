using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Services
{
    /// <summary>
    /// Analisa a saúde do estoque e emite sinais de alerta para o operador.
    /// </summary>
    public sealed class AnalisadorSaudeEstoque
    {
        /// <summary>Representa um sinal simples de saúde do estoque.</summary>
        public record SinalSaude(string ProdutoNome, TipoAlertaEstoque TipoAlerta, string Mensagem);

        /// <summary>
        /// Analisa a lista de itens e retorna os alertas aplicáveis com base nos limiares fornecidos.
        /// </summary>
        public IEnumerable<SinalSaude> Analisar(
            IEnumerable<(string produtoNome, Quantidade quantidade, Validade? validade)> itens,
            int limiteEstoqueBaixo,
            int diasProximoVencimento)
        {
            if (itens == null) throw new ArgumentNullException(nameof(itens));
            if (limiteEstoqueBaixo < 0) throw new ArgumentOutOfRangeException(nameof(limiteEstoqueBaixo));
            if (diasProximoVencimento < 0) throw new ArgumentOutOfRangeException(nameof(diasProximoVencimento));

            var hoje = DateTime.UtcNow.Date;
            var sinais = new List<SinalSaude>();

            foreach (var (produtoNome, quantidade, validade) in itens)
            {
                if (quantidade.Value <= limiteEstoqueBaixo)
                {
                    sinais.Add(new SinalSaude(
                        produtoNome,
                        TipoAlertaEstoque.EstoqueBaixo,
                        $"Quantidade atual ({quantidade.Value}) abaixo do limite ({limiteEstoqueBaixo})."));
                }

                if (validade is not null)
                {
                    var dias = validade.DiasAteVencimento(hoje);
                    if (dias < 0)
                    {
                        // Produto já vencido — usa ProdutoParado pois não há tipo específico para vencimento no enum
                        sinais.Add(new SinalSaude(produtoNome, TipoAlertaEstoque.ProdutoParado, "Produto vencido."));
                    }
                    else if (dias <= diasProximoVencimento)
                    {
                        sinais.Add(new SinalSaude(produtoNome, TipoAlertaEstoque.ProximoVencimento, $"Vence em {dias} dias."));
                    }
                }
            }

            return sinais;
        }
    }
}
