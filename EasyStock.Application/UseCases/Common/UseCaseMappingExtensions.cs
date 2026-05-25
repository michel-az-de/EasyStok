using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Common
{
    internal static class UseCaseMappingExtensions
    {
        public static Dimensoes? ToValueObjectOrNull(this DimensoesInput? input) =>
            input is null ? null : Dimensoes.From(input.Peso, input.Largura, input.Altura, input.Comprimento);
    }
}
