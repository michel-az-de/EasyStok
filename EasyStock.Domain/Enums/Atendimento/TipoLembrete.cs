namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>
/// Origem do lembrete da dona (S43). O manual é escrito por alguém do console; os demais nascem do
/// avaliador a partir de consultas e somem sozinhos quando a condição deixa de valer.
/// </summary>
public enum TipoLembrete
{
    Manual = 1,

    /// <summary>Pedido há 15 min ou mais em <c>AguardandoPagamento</c>.</summary>
    PagamentoSemBaixa = 2,

    /// <summary>Conversa assumida com o cliente esperando resposta há 10 min ou mais.</summary>
    ClienteSemResposta = 3
}
