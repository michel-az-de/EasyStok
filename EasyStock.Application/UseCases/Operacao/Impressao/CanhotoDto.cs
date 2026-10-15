namespace EasyStock.Application.UseCases.Operacao.Impressao;

/// <summary>
/// Canhoto de produção do pedido pago (S20, US-035, US-036, RN-27 a RN-29): o papel basta para produzir
/// sem sistema. Horários já no fuso de Brasília. Os dois formatos (<c>html</c> 80 mm e <c>texto</c> 42
/// colunas) são montados na Api a partir deste modelo.
/// </summary>
public sealed record CanhotoDto(
    CanhotoCabecalhoDto Cabecalho,
    IReadOnlyList<CanhotoGrupoDto> Grupos,
    string? Observacoes,
    string Rodape);

/// <param name="Numero">Número curto do pedido (8 primeiros caracteres do id, maiúsculos), o que o cliente vê.</param>
/// <param name="AgendadoPara">Janela agendada (<c>Pedido.AgendadoParaEm</c>) em Brasília; nulo = para já.</param>
/// <param name="PagoEm">Último pagamento registrado, em Brasília.</param>
public sealed record CanhotoCabecalhoDto(
    string NomeCasa,
    string Numero,
    string? Cliente,
    string? Telefone,
    string? Endereco,
    DateTime? AgendadoPara,
    DateTime? PagoEm);

/// <param name="Linha">Snapshot da linha (<c>paraServir</c>, <c>prepararEmCasa</c>) ou <c>outros</c>.</param>
public sealed record CanhotoGrupoDto(string Linha, string Titulo, IReadOnlyList<CanhotoItemDto> Itens);

/// <param name="Porcao">Rótulo da variação escolhida (ex.: <c>800g</c>, <c>Cento</c>).</param>
/// <param name="Molho">Sugestão de molho do item do cardápio.</param>
public sealed record CanhotoItemDto(string Nome, string? Porcao, decimal Quantidade, string? Molho, string? Observacao);
