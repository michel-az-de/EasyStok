using System;
using System.Collections.Generic;
using System.Linq;
using EasyStock.Domain.ValueObjects;
using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Services
{
    public sealed class AnalisadorSaudeEstoque
    {
        // Representa um sinal simples de saúde do estoque
        public record SinalSaude(string ProdutoNome, TipoAlertaEstoque TipoAlerta, string Mensagem);

        // Analisa itens em estoque e retorna sinais (alertas) sutis úteis ao operador.
        public IEnumerable<SinalSaude> Analisar(IEnumerable<(string produtoNome, Quantidade quantidade, Validade? validade)> itens,
            int limiteEstoqueBaixo, int diasProximoVencimento)
        {
            if (itens == null) throw new ArgumentNullException(nameof(itens));
            if (limiteEstoqueBaixo < 0) throw new ArgumentOutOfRangeException(nameof(limiteEstoqueBaixo));
            if (diasProximoVencimento < 0) throw new ArgumentOutOfRangeException(nameof(diasProximoVencimento));

            var now = DateTime.UtcNow.Date;
            var sinais = new List<SinalSaude>();

            foreach (var (produtoNome, quantidade, validade) in itens)
            {
                if (quantidade.Value <= limiteEstoqueBaixo)
                {
                    sinais.Add(new SinalSaude(produtoNome, TipoAlertaEstoque.EstoqueBaixo, $"Quantidade atual ({quantidade.Value}) abaixo do limite ({limiteEstoqueBaixo})."));
                }

                if (validade is not null)
                {
                    var dias = validade.DiasAteVencimento(now);
                    if (dias < 0)
                    {
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
