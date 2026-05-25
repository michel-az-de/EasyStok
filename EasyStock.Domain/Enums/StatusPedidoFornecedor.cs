namespace EasyStock.Domain.Enums;

public enum StatusPedidoFornecedor
{
    Aberto = 1,
    EmTransito = 2,
    Recebido = 3,
    Cancelado = 4,

    // Pedido com recebimento incompleto: soma de QuantidadeRecebida < soma de
    // Quantidade pedida. Permite reprocessar ate completar — vira Recebido
    // quando totaliza.
    RecebidoParcial = 5
}
