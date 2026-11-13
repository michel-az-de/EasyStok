namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>Revisão humana de identidade. Não modifica os cadastros nem os retratos dos pedidos.</summary>
public sealed class AlteracaoVinculoConversa
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EmpresaId { get; init; }
    public Guid ConversaId { get; init; }
    public Guid RequestId { get; init; }
    public Guid UsuarioId { get; init; }
    public Guid? ClienteAnteriorId { get; init; }
    public Guid? ClienteNovoId { get; init; }
    public long RevisaoAnterior { get; init; }
    public long Revisao { get; init; }
    public string Acao { get; init; } = null!;
    public string Motivo { get; init; } = null!;
    public DateTime CriadaEm { get; init; }
}
