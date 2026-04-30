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
    DateTime FechadoEm
);

/// <summary>Resumo consolidado do caixa de um dia (mesmo se ainda não fechado).</summary>
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
    IReadOnlyList<MovimentoCaixaResult> Movimentos
);
