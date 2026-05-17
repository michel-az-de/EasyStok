using System;
using System.Collections.Generic;
using System.Linq;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Services
{
    public sealed class CalculadoraResumoVenda
    {
        public record Resumo(decimal ValorTotal, decimal QuantidadeTotal);

        // Recebe itens com quantidade e preco unitario e calcula um resumo simples
        public Resumo Calcular(IEnumerable<(Quantidade quantidade, Dinheiro precoUnitario)> itens)
        {
            if (itens == null) throw new ArgumentNullException(nameof(itens));
            decimal somaQuantidade = 0m;
            decimal somaValor = 0m;
            foreach (var (quantidade, precoUnitario) in itens)
            {
                somaQuantidade += quantidade.Value;
                somaValor += precoUnitario.Valor * quantidade.Value;
            }
            return new Resumo(somaValor, somaQuantidade);
        }
    }
}
