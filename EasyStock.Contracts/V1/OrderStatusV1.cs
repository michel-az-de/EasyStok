using System.Text.Json.Serialization;

namespace EasyStock.Contracts.V1;

/// <summary>
/// Status do pedido no contrato V1. Serialização JSON via
/// <see cref="JsonStringEnumConverter"/> com naming policy lowercase
/// pra preservar shape esperado por PWA, mobile e MAUI
/// (<c>"status": "aguardando"</c>).
///
/// <para>
/// Espelha <c>EasyStock.Domain.Sales.StatusPedido</c> mas vive em
/// projeto público sem dependência do Domain — clients externos
/// podem referenciar Contracts sem arrastar regra de negócio.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<OrderStatusV1>))]
public enum OrderStatusV1
{
    Aguardando = 1,
    Preparando = 2,
    Pronto = 3,
    Entregue = 4,
    Cancelado = 5,
}
