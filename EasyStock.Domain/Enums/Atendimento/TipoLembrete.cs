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
    ClienteSemResposta = 3,

    /// <summary>Teste de conexão de uma integração falhou (vigia da F16, #1246).</summary>
    IntegracaoParada = 4,

    /// <summary>A chave de uma integração vence em até 7 dias (vigia da F16, #1246).</summary>
    IntegracaoVencendo = 5
}
