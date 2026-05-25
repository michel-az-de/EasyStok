namespace EasyStock.Application.UseCases.Storefront.Agendamento;

/// <summary>
/// DTO público de uma janela de entrega disponível (ou esgotada) numa data.
/// Retornado por <see cref="ListarJanelasDisponiveisUseCase"/>.
/// </summary>
public record JanelaDisponivelDto(
    DateOnly Data,
    Guid JanelaId,
    string Label,
    TimeOnly HoraInicio,
    TimeOnly HoraFim,
    int VagasRestantes,
    int Capacidade,
    bool Esgotado);
