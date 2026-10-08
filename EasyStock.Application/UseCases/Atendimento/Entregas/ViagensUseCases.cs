using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.Atendimento.Entregas;

/// <summary>
/// Parada no despacho: o entregador de plataforma identifica o pedido pelo número, e o retrato
/// (nome, veículo, placa, empresa) é o gravado na saída, não o cadastro atual.
/// </summary>
public sealed record ParadaViagemResult(
    Guid PedidoId, string NumeroPedido, int Ordem, string? ClienteNome, string? Endereco, DateTime? EntregueEm,
    string? EntregadorNome, string? Veiculo, string? Placa, EmpresaEntregador? EmpresaEntregador);

public sealed record ViagemResult(
    Guid Id, Guid? EntregadorId, SituacaoViagem Situacao, DateTime CriadaEm, DateTime? SaiuEm, DateTime? ConcluidaEm,
    string? RotaUrl, IReadOnlyList<ParadaViagemResult> Paradas);

public sealed class ViagemNaoEncontradaException(Guid id) : Exception($"Viagem {id} não encontrada.");

public sealed class PedidoNaoEncontradoParaViagemException(Guid id) : Exception($"Pedido {id} não encontrado.");

/// <summary>Monta o <see cref="ViagemResult"/> com número, cliente e endereço de cada parada e o link de rota.</summary>
public sealed class ObterViagemUseCase(IViagemRepository viagens, IPedidoRepository pedidos, IClienteRepository clientes)
{
    public async Task<ViagemResult> ExecuteAsync(Guid empresaId, Guid id, CancellationToken ct = default)
    {
        var viagem = await viagens.ObterAsync(empresaId, id, ct) ?? throw new ViagemNaoEncontradaException(id);
        return await MontarAsync(viagem, ct);
    }

    internal async Task<ViagemResult> MontarAsync(Viagem viagem, CancellationToken ct)
    {
        var paradas = new List<ParadaViagemResult>();
        foreach (var p in viagem.Paradas.OrderBy(p => p.Ordem))
        {
            var pedido = await pedidos.GetByIdAsync(viagem.EmpresaId, p.PedidoId);
            var cliente = pedido?.ClienteId is { } cid ? await clientes.GetByIdAsync(viagem.EmpresaId, cid) : null;
            paradas.Add(new ParadaViagemResult(
                p.PedidoId, NumeroPedido(p.PedidoId), p.Ordem, cliente?.Nome ?? pedido?.ClienteNome, Endereco(cliente),
                p.EntregueEm, p.EntregadorNome, p.Veiculo, p.Placa, p.EmpresaEntregador));
        }
        return new ViagemResult(viagem.Id, viagem.EntregadorId, viagem.Situacao, viagem.CriadaEm, viagem.SaiuEm,
            viagem.ConcluidaEm, RotaMaps.Montar(paradas.Select(p => p.Endereco)), paradas);
    }

    /// <summary>Número curto do pedido: 8 primeiros caracteres do id, maiúsculos (o que o cliente vê).</summary>
    public static string NumeroPedido(Guid pedidoId) => pedidoId.ToString("N")[..8].ToUpperInvariant();

    private static string? Endereco(Domain.Entities.Cliente? c)
    {
        if (c is null || string.IsNullOrWhiteSpace(c.Endereco)) return null;
        var partes = new[] { c.Endereco, c.Bairro, c.Cidade }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim());
        return string.Join(", ", partes);
    }
}

public sealed class ListarViagensUseCase(IViagemRepository viagens, ObterViagemUseCase obter)
{
    public const int LimitePadrao = 50;

    public async Task<IReadOnlyList<ViagemResult>> ExecuteAsync(Guid empresaId, SituacaoViagem? situacao, CancellationToken ct = default)
    {
        var resultado = new List<ViagemResult>();
        foreach (var v in await viagens.ListarAsync(empresaId, situacao, LimitePadrao, ct))
            resultado.Add(await obter.MontarAsync(v, ct));
        return resultado;
    }
}

/// <summary>S44: nova viagem, opcionalmente já com o entregador.</summary>
public sealed class CriarViagemUseCase(
    IViagemRepository viagens, IEntregadorRepository entregadores, ObterViagemUseCase obter, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<ViagemResult> ExecuteAsync(Guid empresaId, Guid? entregadorId, CancellationToken ct = default)
    {
        var viagem = Regra.Validar(() => Viagem.Criar(empresaId, relogio.GetUtcNow().UtcDateTime));
        if (entregadorId is { } eid)
        {
            var entregador = await entregadores.ObterAsync(empresaId, eid, ct) ?? throw new EntregadorNaoEncontradoException(eid);
            Regra.Validar(() => viagem.DefinirEntregador(entregador));
        }
        await viagens.AddAsync(viagem, ct);
        await unitOfWork.CommitAsync();
        return await obter.MontarAsync(viagem, ct);
    }
}

/// <summary>S44 (<c>CHAMAR_ENTREGADOR</c> resolvido): define ou tira o entregador da viagem que está montando.</summary>
public sealed class DefinirEntregadorViagemUseCase(
    IViagemRepository viagens, IEntregadorRepository entregadores, IUnitOfWork unitOfWork)
{
    public async Task ExecuteAsync(Guid empresaId, Guid viagemId, Guid? entregadorId, CancellationToken ct = default)
    {
        var viagem = await viagens.ObterAsync(empresaId, viagemId, ct) ?? throw new ViagemNaoEncontradaException(viagemId);
        Entregador? entregador = null;
        if (entregadorId is { } eid)
            entregador = await entregadores.ObterAsync(empresaId, eid, ct) ?? throw new EntregadorNaoEncontradoException(eid);
        Regra.Validar(() => viagem.DefinirEntregador(entregador));
        await unitOfWork.CommitAsync();
    }
}

/// <summary>
/// S44 (<c>POR_NA_VIAGEM</c>): o pedido entra como a última parada. RN-14: cliente bloqueado não
/// entra. Pedido finalizado ou já em outra viagem ativa também não.
/// </summary>
public sealed class IncluirParadaViagemUseCase(
    IViagemRepository viagens, IPedidoRepository pedidos, IClienteRepository clientes, IUnitOfWork unitOfWork)
{
    public async Task ExecuteAsync(Guid empresaId, Guid viagemId, Guid pedidoId, CancellationToken ct = default)
    {
        var viagem = await viagens.ObterAsync(empresaId, viagemId, ct) ?? throw new ViagemNaoEncontradaException(viagemId);
        var pedido = await pedidos.GetByIdAsync(empresaId, pedidoId) ?? throw new PedidoNaoEncontradoParaViagemException(pedidoId);
        if (pedido.EstaFinalizado || pedido.StatusEnum == StatusPedido.SaiuParaEntrega)
            throw new UseCaseValidationException("Pedido finalizado ou já em rota não entra na viagem.");
        if (await viagens.PedidoEmViagemAtivaAsync(empresaId, pedidoId, ct))
            throw new UseCaseValidationException("Pedido já está em outra viagem.");

        var bloqueado = pedido.ClienteId is { } cid && (await clientes.GetByIdAsync(empresaId, cid))?.Bloqueado == true;
        var parada = Regra.Validar(() => viagem.IncluirParada(pedidoId, bloqueado));
        await viagens.RegistrarParadaNovaAsync(parada, ct);
        await unitOfWork.CommitAsync();
    }
}

public sealed class RetirarParadaViagemUseCase(IViagemRepository viagens, IUnitOfWork unitOfWork)
{
    public async Task ExecuteAsync(Guid empresaId, Guid viagemId, Guid pedidoId, CancellationToken ct = default)
    {
        var viagem = await viagens.ObterAsync(empresaId, viagemId, ct) ?? throw new ViagemNaoEncontradaException(viagemId);
        Regra.Validar(() => viagem.RetirarParada(pedidoId));
        await unitOfWork.CommitAsync();
    }
}

/// <summary>S44 (<c>REORDENAR_PARADA</c>).</summary>
public sealed class ReordenarParadaViagemUseCase(IViagemRepository viagens, IUnitOfWork unitOfWork)
{
    public async Task ExecuteAsync(Guid empresaId, Guid viagemId, Guid pedidoId, int novaOrdem, CancellationToken ct = default)
    {
        var viagem = await viagens.ObterAsync(empresaId, viagemId, ct) ?? throw new ViagemNaoEncontradaException(viagemId);
        Regra.Validar(() => viagem.ReordenarParada(pedidoId, novaOrdem));
        await unitOfWork.CommitAsync();
    }
}

/// <summary>S44 (<c>DESFAZER_VIAGEM</c>): só antes da saída; os pedidos voltam soltos para a esteira.</summary>
public sealed class DesfazerViagemUseCase(IViagemRepository viagens, IUnitOfWork unitOfWork)
{
    public async Task ExecuteAsync(Guid empresaId, Guid viagemId, CancellationToken ct = default)
    {
        var viagem = await viagens.ObterAsync(empresaId, viagemId, ct) ?? throw new ViagemNaoEncontradaException(viagemId);
        Regra.Validar(viagem.Desfazer);
        await unitOfWork.CommitAsync();
    }
}

/// <summary>
/// Transição de status do pedido feita pela viagem: valida na máquina de estados, grava o evento de
/// auditoria e publica <c>pedido.mudou_status</c> no outbox, que é o gancho dos avisos ao cliente (S13).
/// Nada é gravado aqui: quem chama faz um único commit para a viagem inteira.
/// </summary>
internal static class TransicaoPedidoViagem
{
    public const string Origem = "viagem";

    /// <summary>Nulo quando o pedido já estava no status (idempotente: nada publicado).</summary>
    public static async Task<(Domain.Entities.Pedido Pedido, string Antigo, string Novo)?> AplicarAsync(
        IPedidoRepository pedidos, IPublicadorEventoIntegracao publicador, Guid empresaId, Guid pedidoId,
        StatusPedido novo, DateTime agora, CancellationToken ct)
    {
        var pedido = await pedidos.GetByIdAsync(empresaId, pedidoId) ?? throw new PedidoNaoEncontradoParaViagemException(pedidoId);
        if (pedido.StatusEnum == novo) return null;
        var antigo = pedido.Status;
        try
        {
            PedidoStateMachine.EnsureTransicaoValida(pedido.StatusEnum, novo);
        }
        catch (TransicaoInvalidaException ex)
        {
            throw new UseCaseValidationException($"Pedido {ObterViagemUseCase.NumeroPedido(pedidoId)}: {ex.Message}");
        }
        pedido.MudarStatus(novo);
        var novoStr = StatusPedidoMapper.Format(novo);

        await pedidos.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "status_changed",
            StatusAntigo = antigo,
            StatusNovo = novoStr,
            Origem = Origem,
            OcorridoEm = agora,
        });
        await publicador.PublicarAsync(
            empresaId: pedido.EmpresaId,
            tipoEvento: "pedido.mudou_status",
            aggregateType: "pedido",
            aggregateId: pedido.Id,
            payload: new PedidoMudouStatusEvent(
                PedidoId: pedido.Id, EmpresaId: pedido.EmpresaId, LojaId: pedido.LojaId,
                StatusAntigo: antigo, StatusNovo: novoStr, Origem: Origem,
                UsuarioId: null, UsuarioNome: null, OcorridoEm: agora),
            correlationId: pedido.Id.ToString(),
            ct: ct);
        await pedidos.UpdateAsync(pedido);
        return (pedido, antigo, novoStr);
    }
}

/// <summary>
/// S44 (<c>SAIR_PARA_ENTREGA</c>): a viagem sai (RN-32: sem entregador é recusada antes de tocar em
/// qualquer pedido), cada pedido vai para "saiu para entrega" e cada cliente recebe o aviso pelo
/// outbox. Tudo num commit só: um pedido que não pode sair derruba a saída inteira.
/// </summary>
public sealed class SairParaEntregaUseCase(
    IViagemRepository viagens,
    IEntregadorRepository entregadores,
    IPedidoRepository pedidos,
    IPublicadorEventoIntegracao publicador,
    IOperacaoEventPublisher operacao,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public async Task<ViagemResult> ExecuteAsync(Guid empresaId, Guid viagemId, CancellationToken ct = default)
    {
        var viagem = await viagens.ObterAsync(empresaId, viagemId, ct) ?? throw new ViagemNaoEncontradaException(viagemId);
        var entregador = viagem.EntregadorId is { } eid ? await entregadores.ObterAsync(empresaId, eid, ct) : null;
        var agora = relogio.GetUtcNow().UtcDateTime;

        Regra.Validar(() => viagem.Sair(entregador, agora));

        var mudancas = new List<(Domain.Entities.Pedido Pedido, string Antigo, string Novo)>();
        foreach (var parada in viagem.Paradas.OrderBy(p => p.Ordem))
        {
            if (await TransicaoPedidoViagem.AplicarAsync(
                    pedidos, publicador, empresaId, parada.PedidoId, StatusPedido.SaiuParaEntrega, agora, ct) is { } mudanca)
                mudancas.Add(mudanca);
        }

        await unitOfWork.CommitAsync();

        foreach (var (pedido, antigo, novo) in mudancas)
            await operacao.PublicarAsync(EventosOperacao.PedidoMudouStatus, empresaId,
                new PedidoMudouStatusOperacao(pedido.Id, antigo, novo), ct);

        return new ViagemResult(viagem.Id, viagem.EntregadorId, viagem.Situacao, viagem.CriadaEm, viagem.SaiuEm, viagem.ConcluidaEm,
            null, viagem.Paradas.OrderBy(p => p.Ordem).Select(p => new ParadaViagemResult(
                p.PedidoId, ObterViagemUseCase.NumeroPedido(p.PedidoId), p.Ordem, null, null, p.EntregueEm,
                p.EntregadorNome, p.Veiculo, p.Placa, p.EmpresaEntregador)).ToList());
    }
}

/// <summary>S44 (<c>MARCAR_PARADA_ENTREGUE</c>): a parada e o pedido ficam entregues; a última conclui a viagem.</summary>
public sealed class MarcarParadaEntregueUseCase(
    IViagemRepository viagens,
    IPedidoRepository pedidos,
    IPublicadorEventoIntegracao publicador,
    IOperacaoEventPublisher operacao,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public async Task ExecuteAsync(Guid empresaId, Guid viagemId, Guid pedidoId, CancellationToken ct = default)
    {
        var viagem = await viagens.ObterAsync(empresaId, viagemId, ct) ?? throw new ViagemNaoEncontradaException(viagemId);
        var agora = relogio.GetUtcNow().UtcDateTime;
        Regra.Validar(() => viagem.MarcarParadaEntregue(pedidoId, agora));

        var mudanca = await TransicaoPedidoViagem.AplicarAsync(
            pedidos, publicador, empresaId, pedidoId, StatusPedido.Entregue, agora, ct);
        await unitOfWork.CommitAsync();

        if (mudanca is { } m)
            await operacao.PublicarAsync(EventosOperacao.PedidoMudouStatus, empresaId,
                new PedidoMudouStatusOperacao(m.Pedido.Id, m.Antigo, m.Novo), ct);
    }
}
