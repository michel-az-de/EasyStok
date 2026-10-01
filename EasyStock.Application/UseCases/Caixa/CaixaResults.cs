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
    // F14 (#1244), aditivos: conferência do fechamento. Null quando fechou sem contar.
    decimal? ValorContado = null,
    decimal? Diferenca = null
);

/// <summary>
/// Linha de venda ou pagamento de pedido que COMPÕE o saldo do caixa mas NÃO é um
/// <see cref="MovimentoCaixaResult"/> (vendas vivem na tabela Vendas; pagamentos em
/// PedidoPagamento). Exibida na lista "Movimentos do dia" para reconciliar (BUG-5):
/// soma das linhas visíveis (movimentos + estas) == saldo esperado.
/// </summary>
public sealed record CaixaLinhaExtraResult(
    DateTime Hora,
    string Tipo,        // "venda" | "pagamento"
    decimal Valor,
    string? Descricao,
    string? Metodo,
    string Origem,      // "Venda" | "Pedido"
    // F14 (#1244), aditivos: de qual pedido veio o pagamento, para o console listar
    // "Pagamentos de pedidos hoje". Null nas linhas de venda.
    Guid? PedidoId = null,
    string? ClienteNome = null);

/// <summary>Quanto cada método movimentou no dia (F14, #1244): pagamentos de pedido + entradas − saídas.</summary>
public sealed record CaixaMetodoResult(string Metodo, decimal Valor);

/// <summary>Resumo consolidado do caixa de um dia (mesmo se ainda não fechado).</summary>
/// <param name="AberturaPendenteCrossDay">True quando o dia consultado é hoje, não teve
/// abertura própria, mas há uma sessão aberta de um dia anterior (abertura sem fechamento).
/// Nesse caso os totais agregam a sessão desde <paramref name="AbertoDesde"/> (issue #596).</param>
/// <param name="AbertoDesde">Data civil (BRT) da abertura quando a sessão é cross-day; null caso contrário.</param>
/// <param name="LinhasExtras">Vendas e pagamentos de pedido do período como linhas, para a
/// lista "Movimentos do dia" reconciliar com o saldo (BUG-5).</param>
/// <param name="SaldoAtendimento">F14 (#1244), aditivo: saldo que o console mostra, igual a
/// <paramref name="SaldoEsperado"/> sem as vendas do PDV (inicial + pagamentos de pedido + entradas
/// − saídas; reembolso de ocorrência já é saída). <paramref name="SaldoEsperado"/> segue o de sempre.</param>
/// <param name="PorMetodo">F14 (#1244), aditivo: resumo por método do saldo do atendimento, sem a abertura.</param>
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
    decimal SaldoAtendimento = 0m,
    IReadOnlyList<CaixaMetodoResult>? PorMetodo = null
);
