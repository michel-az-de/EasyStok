namespace EasyStock.Application.UseCases.Storefront.Agendamento;

/// <summary>
/// Input para <see cref="ListarJanelasDisponiveisUseCase"/>.
/// Datas nulas usam default (hoje..hoje+14d). CEP nulo desativa filtro por zona.
/// <see cref="PrazoMinimoMinutos"/> (S16, RN-21) tira a janela cujo início, no fuso da loja, seja antes
/// de agora + prazo; nulo não corta (site).
/// </summary>
public record ListarJanelasDisponiveisInput(
    string Slug,
    DateOnly? DataInicio,
    DateOnly? DataFim,
    string? Cep,
    int? PrazoMinimoMinutos = null);
