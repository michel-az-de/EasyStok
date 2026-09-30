namespace EasyStock.Application.UseCases.Operacao.Kds;

/// <summary>Card do KDS do console (S19).</summary>
/// <param name="NumeroCurto">8 primeiros caracteres do id em maiúsculas: o número que o cliente vê no checkout.</param>
/// <param name="StatusRotulo">Rótulo escrito do status (RN-30), de <c>StatusPedidoVocabulario</c>.</param>
/// <param name="Linhas">Linhas distintas dos itens (<c>paraServir</c>, <c>prepararEmCasa</c>).</param>
/// <param name="InicioPrevistoEm">Quando o preparo precisa começar (S21); nulo sem janela (pedido para já).</param>
/// <param name="Atrasado">Com início previsto: aguardando e o início já passou (S21). Sem ele: pedido aberto de
/// dia de produção anterior a hoje.</param>
/// <param name="Endereco">Endereço do cliente em texto (S14), para a gaveta de Entregas do console (F04).</param>
/// <param name="RequerAprovacao">Exceção que a dona aprova à mão, como entrega fora de área (S12).</param>
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
    string? Observacoes,
    string? Endereco,
    bool RequerAprovacao,
    string? MotivoRequerAprovacao);

public sealed record KdsJanelaDto(string Label, DateOnly Data, TimeOnly Inicio, TimeOnly Fim);

public sealed record KdsItemDto(
    string Nome,
    string? Variacao,
    decimal Qtd,
    string? Observacao,
    string? Linha,
    string? Molho);
