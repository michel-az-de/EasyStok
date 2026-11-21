namespace EasyStock.Domain.Entities;

/// <summary>Sessão revogável do aplicativo. A identidade não muda durante a rotação do refresh.</summary>
public sealed class SessaoNativa
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public Guid EmpresaId { get; set; }
    public string DispositivoId { get; set; } = null!;
    public DateTime CriadaEm { get; set; }
    public DateTime? RevogadaEm { get; set; }
    public Guid? UltimaRenovacaoId { get; set; }
    public Guid? RefreshAnteriorId { get; set; }
    public string? RefreshSucessorProtegido { get; set; }
}
