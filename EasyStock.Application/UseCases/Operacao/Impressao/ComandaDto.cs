namespace EasyStock.Application.UseCases.Operacao.Impressao;

/// <summary>
/// Comanda de cozinha (S52): papel de produção, sem preço, endereço nem cobrança. Horários em Brasília. Layout
/// aprovado em <c>docs/plan/atendimento-whatsapp/impressos/comanda-aprovada.html</c>.
/// </summary>
/// <param name="Numero">Número curto do pedido (8 hex maiúsculos), o mesmo do Pedido e do código de barras.</param>
/// <param name="NumeroDoDia">Sequência do dia de produção para falar na cozinha ("042"); nulo antes de o pedido
/// entrar na fila (aí o código ocupa o lugar).</param>
/// <param name="Alergias">Alergias do cadastro do cliente, já em texto ("CASTANHA"); valem para o pedido inteiro.</param>
public sealed record ComandaDto(
    string Numero,
    int? NumeroDoDia,
    PedidoImpressoPrazoDto Prazo,
    string? Cliente,
    PedidoImpressoEntregaDto? Entrega,
    IReadOnlyList<string> Alergias,
    IReadOnlyList<ComandaGrupoDto> Grupos,
    string? Observacao,
    DateTime SolicitadoEm,
    DateTime ImpressoEm)
{
    /// <summary>Linhas a marcar, somando os grupos (cada linha tem uma caixa).</summary>
    public int TotalLinhas => Grupos.Sum(g => g.Itens.Count);
}

/// <param name="Linha"><c>prepararEmCasa</c>, <c>paraServir</c> ou <c>outros</c>.</param>
/// <param name="Conservacao">Só em "preparar em casa": <c>congelado</c> ou <c>refrigerado</c> (S53); nulo nos demais.</param>
public sealed record ComandaGrupoDto(string Linha, string Titulo, IReadOnlyList<ComandaItemDto> Itens, string? Conservacao = null);

/// <param name="Porcao">Variação escolhida (<c>800 g</c>, <c>Família</c>).</param>
/// <param name="Molho">Sugestão de molho do item do cardápio.</param>
public sealed record ComandaItemDto(
    decimal Quantidade, string Unidade, string Nome, string? Porcao, string? Molho, string? Observacao);
