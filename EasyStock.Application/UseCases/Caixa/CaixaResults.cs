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
    decimal? DinheiroNaGaveta = null,
    /// <summary>#2084 (B3): o que a gerente contou ao fechar. Nulo nos fechamentos antigos.</summary>
    decimal? ValorContado = null,
    /// <summary>#2084 (D1): o dinheiro que o sistema esperava na gaveta no dia do fechamento (dia da abertura).</summary>
    decimal? DinheiroEsperado = null,
    /// <summary>#2084 (D1): caixa esquecido; o dinheiro de hoje que já estava na gaveta contada e fica no caixa de hoje.</summary>
    decimal? DinheiroEntrouDepois = null,
    /// <summary>#2084 (B4): dinheiro recebido no mesmo dia depois de fechar. Já somado em <see cref="DinheiroNaGaveta"/>.</summary>
    decimal DinheiroPosFechamento = 0m
);

/// <summary>
/// Linha de venda ou pagamento de pedido que COMPÕE o saldo do caixa mas NÃO é um
/// <see cref="MovimentoCaixaResult"/> (vendas vivem na tabela Vendas; pagamentos em
/// PedidoPagamento). Exibida na lista "Movimentos do dia" para reconciliar (BUG-5):
/// soma das linhas visíveis (movimentos + estas) == saldo esperado.
/// </summary>
public sealed record CaixaLinhaExtraResult(
    DateTime Hora,
    string Tipo,        // "venda" | "pagamento" | "estorno" (venda estornada, valor negativo)
    decimal Valor,
    string? Descricao,
    string? Metodo,
    string Origem);     // "Venda" | "Pedido"

/// <summary>Resumo consolidado do caixa de um dia (mesmo se ainda não fechado).</summary>
/// <param name="AberturaPendenteCrossDay">True quando o dia consultado é hoje, não teve
/// abertura própria, mas há uma sessão aberta de um dia anterior (abertura sem fechamento).
/// Nesse caso os totais agregam a sessão desde <paramref name="AbertoDesde"/> (issue #596).</param>
/// <param name="AbertoDesde">Data civil (BRT) da abertura quando a sessão é cross-day; null caso contrário.</param>
/// <param name="LinhasExtras">Vendas e pagamentos de pedido do período como linhas, para a
/// lista "Movimentos do dia" reconciliar com o saldo (BUG-5).</param>
/// <param name="PosFechamento">#1630-2: no dia fechado, os totais e o saldo são os do fechamento; isto é
/// o que mudou depois de fechar (recálculo ao vivo menos o saldo fechado). Zero no dia aberto.</param>
/// <param name="AberturaAConfirmar">#2084 (B1): o caixa abriu sozinho no primeiro pagamento em dinheiro, com a
/// gaveta herdada do último fechamento, e a gerente ainda não conferiu. A abertura manual (POST /api/caixa/abrir)
/// confirma com o valor contado.</param>
/// <param name="DinheiroEsperadoNoFechamento">#2084 (D1): só no caixa esquecido (<paramref name="AberturaPendenteCrossDay"/>).
/// O dinheiro esperado na gaveta no dia da abertura, que é o que o fechamento retrata (ADR-0034).</param>
/// <param name="DinheiroEntrouDepois">#2084 (D1): só no caixa esquecido. O dinheiro que entrou hoje sob a sessão:
/// está na gaveta, mas fica no caixa de hoje e não é diferença a justificar.</param>
/// <param name="DinheiroPosFechamento">#2084 (B4): no dia fechado, o dinheiro que entrou depois de fechar. Vai
/// para a gaveta sugerida na abertura seguinte.</param>
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
    decimal PosFechamento = 0m,
    bool AberturaAConfirmar = false,
    decimal? DinheiroEsperadoNoFechamento = null,
    decimal? DinheiroEntrouDepois = null,
    decimal DinheiroPosFechamento = 0m
);
