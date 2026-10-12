using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Viagem de entrega (S44, ADR-0051): paradas ordenadas que saem juntas com um entregador.
/// RN-32: sem entregador resolvido a viagem não sai (e, por isso, nenhum aviso sai). RN-14: pedido de
/// cliente bloqueado não entra. O instante vem sempre por parâmetro (UTC).
/// </summary>
public class Viagem
{
    private readonly List<ParadaViagem> _paradas = new();

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid? EntregadorId { get; private set; }
    public SituacaoViagem Situacao { get; private set; }
    public DateTime CriadaEm { get; private set; }
    public DateTime? SaiuEm { get; private set; }
    public DateTime? ConcluidaEm { get; private set; }

    public IReadOnlyCollection<ParadaViagem> Paradas => _paradas;

    // EF Core ctor sem parâmetros
    private Viagem() { }

    public static Viagem Criar(Guid empresaId, DateTime agora)
    {
        if (empresaId == Guid.Empty) throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        return new Viagem
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Situacao = SituacaoViagem.Montando,
            CriadaEm = Datas.Utc(agora),
        };
    }

    /// <summary>Nulo tira o entregador. Só de empresa igual e ativo.</summary>
    public void DefinirEntregador(Entregador? entregador)
    {
        GarantirMontando();
        if (entregador is not null)
        {
            if (entregador.EmpresaId != EmpresaId) throw new RegraDeDominioVioladaException("Entregador de outra empresa.");
            if (!entregador.Ativo) throw new RegraDeDominioVioladaException("Entregador inativo.");
        }
        EntregadorId = entregador?.Id;
    }

    public ParadaViagem IncluirParada(Guid pedidoId, bool clienteBloqueado)
    {
        GarantirMontando();
        if (pedidoId == Guid.Empty) throw new RegraDeDominioVioladaException("Pedido é obrigatório.");
        if (clienteBloqueado) throw new RegraDeDominioVioladaException("Cliente bloqueado: o pedido não entra na viagem (RN-14).");
        if (_paradas.Any(p => p.PedidoId == pedidoId)) throw new RegraDeDominioVioladaException("Pedido já está na viagem.");

        var parada = new ParadaViagem(Id, pedidoId, _paradas.Count + 1);
        _paradas.Add(parada);
        return parada;
    }

    public void RetirarParada(Guid pedidoId)
    {
        GarantirMontando();
        var parada = Parada(pedidoId);
        _paradas.Remove(parada);
        Renumerar(_paradas.OrderBy(p => p.Ordem).ToList());
    }

    /// <summary>Move a parada para <paramref name="novaOrdem"/> (começa em 1, limitada ao tamanho) e renumera.</summary>
    public void ReordenarParada(Guid pedidoId, int novaOrdem)
    {
        GarantirMontando();
        var parada = Parada(pedidoId);
        var ordenadas = _paradas.OrderBy(p => p.Ordem).Where(p => p != parada).ToList();
        ordenadas.Insert(Math.Clamp(novaOrdem, 1, ordenadas.Count + 1) - 1, parada);
        Renumerar(ordenadas);
    }

    /// <summary>RN-32: recusa sem entregador resolvido. Grava o retrato do entregador em cada parada.</summary>
    public void Sair(Entregador? entregador, DateTime agora)
    {
        GarantirMontando();
        if (EntregadorId is null || entregador is null || entregador.Id != EntregadorId)
            throw new RegraDeDominioVioladaException("Viagem sem entregador não sai (RN-32): defina o entregador antes.");
        if (!entregador.Ativo) throw new RegraDeDominioVioladaException("Entregador inativo.");
        if (_paradas.Count == 0) throw new RegraDeDominioVioladaException("Viagem sem paradas não sai.");

        foreach (var parada in _paradas) parada.GravarRetrato(entregador);
        Situacao = SituacaoViagem.EmRota;
        SaiuEm = Datas.Utc(agora);
    }

    public void MarcarParadaEntregue(Guid pedidoId, DateTime agora)
    {
        if (Situacao != SituacaoViagem.EmRota) throw new RegraDeDominioVioladaException("Só viagem em rota tem parada entregue.");
        Parada(pedidoId).MarcarEntregue(Datas.Utc(agora));
        if (_paradas.All(p => p.Entregue))
        {
            Situacao = SituacaoViagem.Concluida;
            ConcluidaEm = Datas.Utc(agora);
        }
    }

    /// <summary>Dissolve a viagem antes da saída: os pedidos voltam a ficar soltos na esteira.</summary>
    public void Desfazer()
    {
        GarantirMontando();
        _paradas.Clear();
        Situacao = SituacaoViagem.Desfeita;
    }

    private ParadaViagem Parada(Guid pedidoId) =>
        _paradas.FirstOrDefault(p => p.PedidoId == pedidoId)
        ?? throw new RegraDeDominioVioladaException("Pedido não está na viagem.");

    private void GarantirMontando()
    {
        if (Situacao != SituacaoViagem.Montando)
            throw new RegraDeDominioVioladaException("A viagem já saiu ou foi desfeita.");
    }

    private static void Renumerar(IList<ParadaViagem> ordenadas)
    {
        for (var i = 0; i < ordenadas.Count; i++) ordenadas[i].Ordem = i + 1;
    }
}
