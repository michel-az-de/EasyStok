namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>
/// Situação da corrida chamada pela API (#2158, ADR-0064). <c>Solicitando</c> é gravado antes do POST ao
/// provedor; os demais vêm da reconsulta ao provedor. Corrida manual não tem status.
/// </summary>
public enum StatusCorrida
{
    Solicitando = 1,
    ProcurandoMotorista = 2,
    MotoristaACaminho = 3,
    Coletada = 4,
    Entregue = 5,
    Cancelada = 6,
    Rejeitada = 7,
    Expirada = 8,
    Falha = 9
}
