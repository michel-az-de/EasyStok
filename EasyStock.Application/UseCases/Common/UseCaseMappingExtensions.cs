using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Common
{
    internal static class UseCaseMappingExtensions
    {
        public static Dimensoes? ToValueObjectOrNull(this DimensoesInput? input)
        {
            if (input is null) return null;
            var dimensoes = Dimensoes.From(input.Peso, input.Largura, input.Altura, input.Comprimento);
            // #688/BUG-010a: valida coerência só no write path (criar/atualizar produto),
            // não na desserialização — barra "10 x 0 x 0 cm" sem quebrar leitura de legados.
            dimensoes.EnsureCoerente();
            return dimensoes;
        }
    }
}
