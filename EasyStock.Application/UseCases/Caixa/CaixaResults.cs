namespace EasyStock.Application.UseCases.Caixa;

/// <summary>DTOs de retorno do módulo Caixa (Onda P3).</summary>
public sealed record MovimentoCaixaResult(
    Guid Id,
    Guid EmpresaId,
    Guid? LojaId,
    string Tipo,
    decimal Valor,
    string? Descricao,
    string? Metodo,
    string? Categoria,
    string? Referencia,
    DateTime DataMovimento,
    Guid? RegistradoPorUserId,
    string? RegistradoPorNome,
    string? Origem,
    DateTime? EstornadoEm,
    Guid? EstornadoPorUserId,
    string? EstornadoPorNome,
    string? MotivoEstorno,
    DateTime CriadoEm,
    bool Ativo
);

public sealed record FechamentoCaixaResult(
    Guid Id,
    Guid EmpresaId,
    Guid? LojaId,
    DateOnly Data,
    decimal SaldoInicial,
    decimal TotalVendas,
    decimal TotalPagamentosPedidos,
    decimal TotalEntradasExtras,
    decimal TotalSaidasExtras,
    decimal SaldoFinal,
    Guid? FechadoPorUserId,
    string? FechadoPorNome,
    string? Observacoes,
    DateTime FechadoEm,
    /// <summary>
    /// #1776-11: dinheiro físico que ficou na gaveta ao fechar (abertura + entradas e vendas em dinheiro − saídas em
    /// dinheiro), pela conta de <see cref="GavetaCaixa"/>. Nulo quando a listagem não recalculou o dia.
    /// </summary>
    decimal? DinheiroNaGaveta = null
);

/// <summary>
/// Linha de venda ou pagamento de pedido que COMPÕE o saldo do caixa mas NÃO é um
/// <see cref="MovimentoCaixaResult"/> (vendas vivem na tabela Vendas; pagamentos em
/// PedidoPagamento). Exibida na lista "Movimentos do dia" para reconciliar (BUG-5):
/// soma das linhas visíveis (movimentos + estas) == saldo esperado.
/// </summary>
/// <param name="PedidoNumeroDoDia">#2082 B16: só no pagamento de pedido. Número do pedido no dia de produção (o que a
/// cozinha fala); nulo enquanto o pedido não foi numerado.</param>
/// <param name="ClienteNome">#2082 B16: só no pagamento de pedido. Nome do cliente no pedido.</param>
/// <param name="RegistradoPorNome">#2082 B16: só no pagamento de pedido. Quem registrou o recebimento: "Mercado Pago"
/// no que o provedor confirmou, o nome da pessoa na baixa manual.</param>
public sealed record CaixaLinhaExtraResult(
    DateTime Hora,
    string Tipo,        // "venda" | "pagamento" | "estorno" (venda estornada, valor negativo)
    decimal Valor,
    string? Descricao,
    string? Metodo,
    string Origem,      // "Venda" | "Pedido"
    int? PedidoNumeroDoDia = null,
    string? ClienteNome = null,
    string? RegistradoPorNome = null);

/// <summary>Resumo consolidado do caixa de um dia (mesmo se ainda não fechado).</summary>
/// <param name="AberturaPendenteCrossDay">True quando o dia consultado é hoje, não teve
/// abertura própria, mas há uma sessão aberta de um dia anterior (abertura sem fechamento).
/// Nesse caso os totais agregam a sessão desde <paramref name="AbertoDesde"/> (issue #596).</param>
/// <param name="AbertoDesde">Data civil (BRT) da abertura quando a sessão é cross-day; null caso contrário.</param>
/// <param name="LinhasExtras">Vendas e pagamentos de pedido do período como linhas, para a
/// lista "Movimentos do dia" reconciliar com o saldo (BUG-5).</param>
/// <param name="PosFechamento">#1630-2: no dia fechado, os totais e o saldo são os do fechamento; isto é
/// o que mudou depois de fechar (recálculo ao vivo menos o saldo fechado). Zero no dia aberto.</param>
public sealed record CaixaDiaResult(
    DateOnly Data,
    Guid EmpresaId,
    Guid? LojaId,
    decimal SaldoInicial,
    decimal TotalVendas,
    decimal TotalPagamentosPedidos,
    decimal TotalEntradasExtras,
    decimal TotalSaidasExtras,
    decimal SaldoEsperado,
    bool Aberto,
    bool Fechado,
    FechamentoCaixaResult? Fechamento,
    IReadOnlyList<MovimentoCaixaResult> Movimentos,
    bool AberturaPendenteCrossDay = false,
    DateOnly? AbertoDesde = null,
    IReadOnlyList<CaixaLinhaExtraResult>? LinhasExtras = null,
    decimal PosFechamento = 0m
);
