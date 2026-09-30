namespace EasyStock.Application.UseCases.Operacao.Kds;

/// <summary>Card do KDS do console (S19).</summary>
/// <param name="NumeroCurto">8 primeiros caracteres do id em maiúsculas: o número que o cliente vê no checkout.</param>
/// <param name="StatusRotulo">Rótulo escrito do status (RN-30), de <c>StatusPedidoVocabulario</c>.</param>
/// <param name="Linhas">Linhas distintas dos itens (<c>paraServir</c>, <c>prepararEmCasa</c>).</param>
/// <param name="InicioPrevistoEm">Preenchido pela S21; nulo até lá.</param>
/// <param name="Atrasado">Até a S21: pedido aberto de dia de produção anterior a hoje.</param>
public sealed record KdsPedidoDto(
    Guid Id,
    string NumeroCurto,
    string? ClienteNome,
    string? ClienteApt,
    KdsJanelaDto? Janela,
    DateTime? AgendadoParaEm,
    string Status,
    string StatusRotulo,
    IReadOnlyList<string> Linhas,
    IReadOnlyList<KdsItemDto> Itens,
    DateTime? InicioPrevistoEm,
    bool Atrasado,
    DateTime? PagoEm,
    DateTime CriadoEm,
    string? Observacoes);

public sealed record KdsJanelaDto(string Label, DateOnly Data, TimeOnly Inicio, TimeOnly Fim);

public sealed record KdsItemDto(
    string Nome,
    string? Variacao,
    decimal Qtd,
    string? Observacao,
    string? Linha,
    string? Molho);
