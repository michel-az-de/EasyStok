namespace EasyStock.Web.Models.Api;

public record MovimentoCaixa
{
    public required string Id { get; init; }
    public Guid EmpresaId { get; init; }
    public Guid? LojaId { get; init; }
    public required string Tipo { get; init; }
    public decimal Valor { get; init; }
    public string? Descricao { get; init; }
    public string? Metodo { get; init; }
    public string? Categoria { get; init; }
    public string? Referencia { get; init; }
    public DateTime DataMovimento { get; init; }
    public Guid? RegistradoPorUserId { get; init; }
    public string? RegistradoPorNome { get; init; }
    public string? Origem { get; init; }
    public DateTime? EstornadoEm { get; init; }
    public Guid? EstornadoPorUserId { get; init; }
    public string? EstornadoPorNome { get; init; }
    public string? MotivoEstorno { get; init; }
    public DateTime CriadoEm { get; init; }
    public bool Ativo { get; init; }
}

public record FechamentoCaixa
{
    public required string Id { get; init; }
    public Guid EmpresaId { get; init; }
    public Guid? LojaId { get; init; }
    public DateOnly Data { get; init; }
    public decimal SaldoInicial { get; init; }
    public decimal TotalVendas { get; init; }
    public decimal TotalPagamentosPedidos { get; init; }
    public decimal TotalEntradasExtras { get; init; }
    public decimal TotalSaidasExtras { get; init; }
    public decimal SaldoFinal { get; init; }
    public Guid? FechadoPorUserId { get; init; }
    public string? FechadoPorNome { get; init; }
    public string? Observacoes { get; init; }
    public DateTime FechadoEm { get; init; }
}

public record CaixaDia
{
    public DateOnly Data { get; init; }
    public Guid EmpresaId { get; init; }
    public Guid? LojaId { get; init; }
    public decimal SaldoInicial { get; init; }
    public decimal TotalVendas { get; init; }
    public decimal TotalPagamentosPedidos { get; init; }
    public decimal TotalEntradasExtras { get; init; }
    public decimal TotalSaidasExtras { get; init; }
    public decimal SaldoEsperado { get; init; }
    public bool Aberto { get; init; }
    public bool Fechado { get; init; }
    public FechamentoCaixa? Fechamento { get; init; }
    public List<MovimentoCaixa> Movimentos { get; init; } = new();

    // Sessão aberta de um dia anterior, ainda sem fechamento (issue #596). Quando true,
    // os totais somam a sessão desde AbertoDesde e o caixa aparece aberto convidando a fechar.
    public bool AberturaPendenteCrossDay { get; init; }
    public DateOnly? AbertoDesde { get; init; }
}

public record MobileCashSummary
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public decimal Amount { get; init; }
    public required string Description { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid? EmpresaId { get; init; }
    public Guid? LojaId { get; init; }
    public Guid? ErpMovimentoCaixaId { get; init; }
    public string? LastDeviceId { get; init; }
    public string? LastOperatorName { get; init; }
    public bool Linked => ErpMovimentoCaixaId.HasValue && ErpMovimentoCaixaId.Value != Guid.Empty;
}
