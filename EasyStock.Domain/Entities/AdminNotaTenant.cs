namespace EasyStock.Domain.Entities;

public enum TipoNotaTenant
{
    Info = 0,
    Alerta = 1,
    Escalonamento = 2
}

/// <summary>
/// Nota interna do back-office por tenant — handoff entre operadores
/// (ex: "cliente prefere ser atendido por email", "abrir chamado X só após
/// confirmar com fulano da empresa"). Soft-delete pra preservar histórico LGPD
/// de quem comentou o que.
/// </summary>
public class AdminNotaTenant
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    /// <summary>UsuarioId do admin autor (referência fraca; se admin for removido, mantém histórico).</summary>
    public Guid AutorAdminId { get; private set; }
    public string AutorEmail { get; private set; } = null!;
    public string Texto { get; private set; } = null!;
    public TipoNotaTenant Tipo { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }
    public DateTime? ExcluidoEm { get; private set; }

    private AdminNotaTenant() { }

    public static AdminNotaTenant Criar(Guid tenantId, Guid autorAdminId, string autorEmail, string texto, TipoNotaTenant tipo)
    {
        var agora = DateTime.UtcNow;
        return new AdminNotaTenant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AutorAdminId = autorAdminId,
            AutorEmail = autorEmail,
            Texto = texto,
            Tipo = tipo,
            CriadoEm = agora,
            AlteradoEm = agora
        };
    }

    public void Atualizar(string texto, TipoNotaTenant tipo)
    {
        Texto = texto;
        Tipo = tipo;
        AlteradoEm = DateTime.UtcNow;
    }

    public void Excluir() => ExcluidoEm = DateTime.UtcNow;
}
