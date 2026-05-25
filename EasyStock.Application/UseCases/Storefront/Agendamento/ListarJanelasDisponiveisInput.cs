namespace EasyStock.Application.UseCases.Storefront.Agendamento;

/// <summary>
/// Input para <see cref="ListarJanelasDisponiveisUseCase"/>.
/// Datas nulas usam default (hoje..hoje+14d). CEP nulo desativa filtro por zona.
/// </summary>
public record ListarJanelasDisponiveisInput(
    string Slug,
    DateOnly? DataInicio,
    DateOnly? DataFim,
    string? Cep);
