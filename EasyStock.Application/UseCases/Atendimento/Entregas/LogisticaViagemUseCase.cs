using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using System.Text.Json;

namespace EasyStock.Application.UseCases.Atendimento.Entregas;

/// <summary>Composição com as viagens existentes; quem chama controla transação e commit.</summary>
public sealed class LogisticaViagemUseCase(IViagemRepository viagens, IEntregadorRepository entregadores,
    IPedidoRepository pedidos, IPublicadorEventoIntegracao publicador, TimeProvider relogio)
{
    public async Task<Guid> CriarAsync(Guid empresaId, string provedor, IReadOnlyList<Guid> pedidoIds, CancellationToken ct)
    {
        var nome = provedor switch { "uber_direct" => "Uber (simulação)", "lalamove" => "Lalamove (simulação)", "em_maos" => "Entrega em mãos (demonstração)", _ => "Entregador manual (demonstração)" };
        var empresa = provedor switch { "uber_direct" => EmpresaEntregador.Uber, "lalamove" => EmpresaEntregador.Lalamove, "em_maos" => EmpresaEntregador.Propria, _ => EmpresaEntregador.Outra };
        var entregador = (await entregadores.ListarAsync(empresaId, false, ct)).FirstOrDefault(e => e.Nome == nome && e.Empresa == empresa);
        if (entregador is null)
        {
            entregador = Entregador.Criar(empresaId, nome, provedor is "uber_direct" or "lalamove" ? TipoEntregador.Plataforma : TipoEntregador.Proprio,
                empresa, null, "Demonstração", null, Agora);
            await entregadores.AddAsync(entregador, ct);
        }
        var viagem = Viagem.Criar(empresaId, Agora);
        viagem.DefinirEntregador(entregador);
        foreach (var pedidoId in pedidoIds) viagem.IncluirParada(pedidoId, false);
        await viagens.AddAsync(viagem, ct);
        return viagem.Id;
    }

    public async Task AplicarAsync(DespachoLogistica despacho, string evento, CancellationToken ct)
    {
        var viagem = await viagens.ObterAsync(despacho.EmpresaId, despacho.ViagemId, ct)
            ?? throw new ViagemNaoEncontradaException(despacho.ViagemId);
        var cotados = JsonSerializer.Deserialize<List<Guid>>(despacho.PedidoIdsJson) ?? [];
        if (!viagem.Paradas.OrderBy(p => p.Ordem).Select(p => p.PedidoId).SequenceEqual(cotados))
            throw new UseCaseValidationException("A rota foi alterada depois da contratação. Confira a viagem antes de continuar; os pedidos precisam corresponder à cotação.");
        if (evento == "a_caminho" && viagem.Situacao == SituacaoViagem.Montando)
        {
            var entregador = viagem.EntregadorId is { } id ? await entregadores.ObterAsync(despacho.EmpresaId, id, ct) : null;
            viagem.Sair(entregador, Agora);
            foreach (var parada in viagem.Paradas)
                await TransicaoPedidoViagem.AplicarAsync(pedidos, publicador, despacho.EmpresaId, parada.PedidoId, StatusPedido.SaiuParaEntrega, Agora, ct);
        }
        else if (evento == "entregue")
        {
            foreach (var parada in viagem.Paradas.Where(p => !p.Entregue).ToList())
            {
                viagem.MarcarParadaEntregue(parada.PedidoId, Agora);
                await TransicaoPedidoViagem.AplicarAsync(pedidos, publicador, despacho.EmpresaId, parada.PedidoId, StatusPedido.Entregue, Agora, ct);
            }
        }
        else if (evento == "cancelado") viagem.Desfazer();
        else if (evento == "devolvido" && viagem.Situacao == SituacaoViagem.EmRota)
        {
            foreach (var parada in viagem.Paradas.Where(p => !p.Entregue).ToList())
            {
                var pedido = await pedidos.GetByIdAsync(despacho.EmpresaId, parada.PedidoId)
                    ?? throw new PedidoNaoEncontradoParaViagemException(parada.PedidoId);
                var antigo = pedido.Status;
                pedido.RegistrarRetornoEntrega();
                await pedidos.AddEventoAsync(new PedidoEvento { Id = Guid.NewGuid(), PedidoId = pedido.Id,
                    Tipo = "status_changed", StatusAntigo = antigo, StatusNovo = pedido.Status,
                    UsuarioId = despacho.UsuarioId, Origem = "logistica", OcorridoEm = Agora });
                await publicador.PublicarAsync(pedido.EmpresaId, "pedido.mudou_status", "pedido", pedido.Id,
                    new PedidoMudouStatusEvent(pedido.Id, pedido.EmpresaId, pedido.LojaId, antigo, pedido.Status, "logistica", despacho.UsuarioId, null, Agora),
                    correlationId: pedido.Id.ToString(), ct: ct);
                await pedidos.UpdateAsync(pedido);
            }
            viagem.RegistrarRetorno(Agora);
        }
    }
    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;
}

internal static class ViagemContratadaGuard
{
    public static async Task ExigirFluxoManualAsync(IViagemRepository viagens, Guid empresaId, Guid viagemId, CancellationToken ct)
    {
        if (await viagens.VinculadaLogisticaAsync(empresaId, viagemId, ct))
            throw new UseCaseValidationException("Esta viagem tem frete contratado. Use Cotar frete e acompanhamento para atualizar ou cancelar a entrega.");
    }
}
