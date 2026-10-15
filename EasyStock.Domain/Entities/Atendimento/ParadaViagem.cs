using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Parada da <see cref="Viagem"/>: um pedido, na ordem da rota. Na saída grava o retrato do
/// entregador (nome, veículo, placa, empresa), que não muda se o cadastro for editado depois: com
/// três "José" no mesmo dia é o que diz quem levou cada pedido.
/// </summary>
public class ParadaViagem
{
    public Guid Id { get; private set; }
    public Guid ViagemId { get; private set; }
    public Guid PedidoId { get; private set; }
    public int Ordem { get; internal set; }
    public DateTime? EntregueEm { get; private set; }

    public string? EntregadorNome { get; private set; }
    public string? Veiculo { get; private set; }
    public string? Placa { get; private set; }
    public EmpresaEntregador? EmpresaEntregador { get; private set; }

    public bool Entregue => EntregueEm is not null;

    // EF Core ctor sem parâmetros
    private ParadaViagem() { }

    internal ParadaViagem(Guid viagemId, Guid pedidoId, int ordem)
    {
        Id = Guid.NewGuid();
        ViagemId = viagemId;
        PedidoId = pedidoId;
        Ordem = ordem;
    }

    internal void GravarRetrato(Entregador entregador)
    {
        EntregadorNome = entregador.Nome;
        Veiculo = entregador.Veiculo;
        Placa = entregador.Placa;
        EmpresaEntregador = entregador.Empresa;
    }

    internal void MarcarEntregue(DateTime em) => EntregueEm ??= em;
}
