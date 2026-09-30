namespace EasyStock.Domain.Enums.Campanhas;

/// <summary>
/// Situação de cada cliente na campanha (S28): Pendente até a onda dele sair; Excluido com motivo
/// (<see cref="Entities.Campanhas.MotivoExclusaoCampanha"/>); Enfileirado no outbox de notificações;
/// Enviado ou Falhou conforme o outbox; Pediu quando fez pedido atribuído à campanha (S30).
/// </summary>
public enum StatusCampanhaDestinatario
{
    Pendente = 1,
    Excluido = 2,
    Enfileirado = 3,
    Enviado = 4,
    Falhou = 5,
    Pediu = 6
}
