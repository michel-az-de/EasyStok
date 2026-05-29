namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Ocupação concreta (janela × data × pedido) — fonte da verdade de capacidade (ADR-0014).
///
/// <para>
/// <strong>Por que linha por ocupação e não contador agregado?</strong>
/// Contador <c>vagas_consumidas INT</c> dessincroniza ao replicar pedido, aprovação
/// parcial, ou crash entre tx. Aqui cada linha é uma identidade: capacidade efetiva =
/// <c>COUNT(*) WHERE liberado_em IS NULL</c>. Impossível overbookear via TOCTOU
/// porque o INSERT é atômico no Postgres com condição (ver ADR-0014 §Solução 1).
/// </para>
///
/// <para>
/// <strong>Liberação é idempotente</strong> — handler de <c>PedidoCanceladoEvent</c>
/// pode disparar mais de uma vez (Outbox + retry + reconciliação). <see cref="Liberar"/>
/// é no-op se já liberada, preservando o primeiro <see cref="LiberadoEm"/>/<see cref="MotivoLiberacao"/>
/// como canônicos para auditoria.
/// </para>
/// </summary>
public class VagaOcupada
{
    public Guid Id { get; private set; }
    public Guid JanelaEntregaId { get; private set; }
    public DateOnly DataEntrega { get; private set; }
    public Guid PedidoId { get; private set; }
    public DateTime OcupadoEm { get; private set; }
    public DateTime? LiberadoEm { get; private set; }
    public string? MotivoLiberacao { get; private set; }

    // EF Core ctor sem parâmetros
    private VagaOcupada() { }

    /// <summary>
    /// Factory. A unicidade real (pedido só pode ter UMA vaga ativa) e a checagem
    /// de capacidade são responsabilidade da camada Infra (filtered unique index +
    /// INSERT ... WHERE COUNT &lt; capacidade). Aqui só garantimos invariantes do agregado.
    /// </summary>
    public static VagaOcupada Ocupar(Guid janelaEntregaId, DateOnly dataEntrega, Guid pedidoId)
    {
        if (janelaEntregaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("JanelaEntregaId é obrigatório.");

        if (pedidoId == Guid.Empty)
            throw new RegraDeDominioVioladaException("PedidoId é obrigatório.");

        return new VagaOcupada
        {
            Id = Guid.NewGuid(),
            JanelaEntregaId = janelaEntregaId,
            DataEntrega = dataEntrega,
            PedidoId = pedidoId,
            OcupadoEm = DateTime.UtcNow,
            LiberadoEm = null,
            MotivoLiberacao = null,
        };
    }

    /// <summary>
    /// Marca vaga como liberada. <strong>Idempotente</strong> — chamadas após a primeira
    /// são no-op (preserva o timestamp/motivo originais). Crítico porque o handler de
    /// <c>PedidoCanceladoEvent</c> pode rodar mais de uma vez (Outbox at-least-once +
    /// reconciliação). Ver ADR-0014 §Solução 3.
    /// </summary>
    public void Liberar(string motivo)
    {
        if (LiberadoEm.HasValue) return; // idempotente — primeiro a chegar fica gravado
        LiberadoEm = DateTime.UtcNow;
        MotivoLiberacao = motivo;
    }

    /// <summary>Vaga ativa = ainda consome capacidade. Inativa = liberada (cancelamento).</summary>
    public bool IsAtiva() => !LiberadoEm.HasValue;
}
