namespace EasyStock.Domain.Entities.Atendimento;

public sealed class CreditoClienteComanda
{
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid PedidoOrigemId { get; private set; }
    public Guid OperacaoId { get; private set; }
    public string Codigo { get; private set; } = null!;
    public decimal Valor { get; private set; }
    public decimal Saldo { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public ICollection<UsoCreditoClienteComanda> Usos { get; private set; } = [];

    private CreditoClienteComanda() { }

    public static CreditoClienteComanda Criar(Guid empresaId, Guid clienteId, Guid pedidoId, Guid operacaoId, decimal valor, DateTime agora)
    {
        if (empresaId == Guid.Empty || clienteId == Guid.Empty || pedidoId == Guid.Empty || operacaoId == Guid.Empty || valor <= 0 || valor != decimal.Round(valor, 2))
            throw new RegraDeDominioVioladaException("Dados do crédito inválidos.");
        var id = Guid.NewGuid();
        return new() { Id = id, EmpresaId = empresaId, ClienteId = clienteId, PedidoOrigemId = pedidoId,
            OperacaoId = operacaoId, Codigo = $"CB-{id:N}".ToUpperInvariant(), Valor = valor, Saldo = valor, CriadoEm = agora };
    }

    public UsoCreditoClienteComanda Usar(Guid pedidoId, decimal valor, DateTime agora)
    {
        if (pedidoId == Guid.Empty || valor <= 0 || valor > Saldo || valor != decimal.Round(valor, 2))
            throw new RegraDeDominioVioladaException("Saldo do cupom insuficiente.");
        Saldo -= valor;
        var uso = new UsoCreditoClienteComanda { Id = Guid.NewGuid(), EmpresaId = EmpresaId, CreditoId = Id,
            PedidoId = pedidoId, Valor = valor, CriadoEm = agora };
        Usos.Add(uso);
        return uso;
    }

    /// <summary>Devolve ao cupom o valor de um uso cujo pedido foi desfeito sem cobrança (#1961).</summary>
    public void Devolver(UsoCreditoClienteComanda uso)
    {
        ArgumentNullException.ThrowIfNull(uso);
        if (uso.CreditoId != Id || Saldo + uso.Valor > Valor)
            throw new RegraDeDominioVioladaException("Uso do cupom não pertence a este crédito.");
        Saldo += uso.Valor;
        Usos.Remove(uso);
    }
}

public sealed class UsoCreditoClienteComanda
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid CreditoId { get; set; }
    public Guid PedidoId { get; set; }
    public decimal Valor { get; set; }
    public DateTime CriadoEm { get; set; }
}
