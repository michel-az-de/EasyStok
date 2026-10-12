using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Services.Pedidos;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests;

internal static class EnderecoEntregaPedidoTeste
{
    public static EnderecoEntregaPedido Criar() => new(Substitute.For<ICepLookupClient>(),
        Substitute.For<IGeocodingClient>(), TimeProvider.System, NullLogger<EnderecoEntregaPedido>.Instance);
}
