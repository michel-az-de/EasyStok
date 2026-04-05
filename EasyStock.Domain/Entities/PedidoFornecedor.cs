using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Domain.Entities;

public class PedidoFornecedor
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid FornecedorId { get; set; }
    public DateTime DataPedido { get; set; }
    public DateTime? PrevisaoEntrega { get; set; }
    public DateTime? DataRecebimento { get; set; }
    public decimal? ValorEstimado { get; set; }
    public StatusPedidoFornecedor Status { get; set; }
    public string? Canal { get; set; }
    public string? Tracking { get; set; }
    public string? Observacoes { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    public Fornecedor? Fornecedor { get; set; }
    public ICollection<ItemPedidoFornecedor> Itens { get; set; } = new List<ItemPedidoFornecedor>();

    public static PedidoFornecedor Criar(
        Guid empresaId,
        Guid fornecedorId,
        DateTime? previsaoEntrega,
        decimal? valorEstimado,
        string? canal,
        string? tracking,
        string? observacoes)
    {
        var agora = DateTime.UtcNow;
        return new PedidoFornecedor
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            FornecedorId = fornecedorId,
            DataPedido = agora,
            PrevisaoEntrega = previsaoEntrega,
            ValorEstimado = valorEstimado,
            Status = StatusPedidoFornecedor.Aberto,
            Canal = Normalizar(canal),
            Tracking = Normalizar(tracking),
            Observacoes = Normalizar(observacoes),
            CriadoEm = agora,
            AlteradoEm = agora
        };
    }

    public void Atualizar(
        DateTime? previsaoEntrega,
        decimal? valorEstimado,
        string? canal,
        string? tracking,
        string? observacoes)
    {
        PrevisaoEntrega = previsaoEntrega;
        ValorEstimado = valorEstimado;
        Canal = Normalizar(canal);
        Tracking = Normalizar(tracking);
        Observacoes = Normalizar(observacoes);
        AlteradoEm = DateTime.UtcNow;
    }

    public bool PodeTransicionarPara(StatusPedidoFornecedor novoStatus) =>
        (Status, novoStatus) switch
        {
            (StatusPedidoFornecedor.Aberto, StatusPedidoFornecedor.EmTransito) => true,
            (StatusPedidoFornecedor.Aberto, StatusPedidoFornecedor.Cancelado) => true,
            (StatusPedidoFornecedor.EmTransito, StatusPedidoFornecedor.Recebido) => true,
            (StatusPedidoFornecedor.EmTransito, StatusPedidoFornecedor.Cancelado) => true,
            _ => false
        };

    public void IniciarTransito(string? tracking = null)
    {
        if (!PodeTransicionarPara(StatusPedidoFornecedor.EmTransito))
            throw new RegraDeDominioVioladaException(
                $"Transicao invalida: pedido com status '{Status}' nao pode ser movido para 'EmTransito'.");
        Status = StatusPedidoFornecedor.EmTransito;
        if (!string.IsNullOrWhiteSpace(tracking))
            Tracking = tracking.Trim();
        AlteradoEm = DateTime.UtcNow;
    }

    public void Cancelar()
    {
        if (!PodeTransicionarPara(StatusPedidoFornecedor.Cancelado))
            throw new RegraDeDominioVioladaException(
                $"Transicao invalida: pedido com status '{Status}' nao pode ser cancelado.");
        Status = StatusPedidoFornecedor.Cancelado;
        AlteradoEm = DateTime.UtcNow;
    }

    public void Receber()
    {
        if (!PodeTransicionarPara(StatusPedidoFornecedor.Recebido))
            throw new RegraDeDominioVioladaException(
                $"Transicao invalida: pedido com status '{Status}' nao pode ser marcado como recebido.");
        Status = StatusPedidoFornecedor.Recebido;
        DataRecebimento = DateTime.UtcNow;
        AlteradoEm = DateTime.UtcNow;
    }

    private static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
