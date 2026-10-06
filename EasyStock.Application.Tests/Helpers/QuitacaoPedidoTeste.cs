using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services.Pedidos;

namespace EasyStock.Application.Tests.Helpers;

internal static class QuitacaoPedidoTeste
{
    public static QuitacaoPedido Criar(IPedidoRepository pedidos,
        ICobrancaPedidoRepository? cobrancas = null, IConversaRepository? conversas = null,
        IImpressaoPendenteRepository? impressoes = null, IPublicadorEventoIntegracao? publicador = null,
        IOperacaoEventPublisher? eventos = null) => new(pedidos,
            cobrancas ?? Substitute.For<ICobrancaPedidoRepository>(), conversas ?? Substitute.For<IConversaRepository>(),
            impressoes ?? Substitute.For<IImpressaoPendenteRepository>(), publicador ?? Substitute.For<IPublicadorEventoIntegracao>(),
            eventos ?? Substitute.For<IOperacaoEventPublisher>());
}
