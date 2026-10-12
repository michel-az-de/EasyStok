using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Domain.Entities.Notifications;

public class ConsentimentoNotificacao
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public CanalNotificacao Canal { get; set; }
    public CategoriaConteudoNotificacao Categoria { get; set; }
    public bool OptIn { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public string AtualizadoPor { get; set; } = null!;
    public string? IpOrigem { get; set; }
    public string? MotivoOptOut { get; set; }

    public Usuario? Usuario { get; set; }

    /// <summary>Motivo gravado em <see cref="MotivoOptOut"/> quando a anonimização (LGPD) revoga o consentimento.</summary>
    public const string MotivoAnonimizacao = "anonimizacao";

    public static ConsentimentoNotificacao Registrar(
        Guid usuarioId,
        CanalNotificacao canal,
        CategoriaConteudoNotificacao categoria,
        bool optIn,
        string atualizadoPor,
        string? ipOrigem = null,
        string? motivoOptOut = null) => new()
    {
        Id = Guid.NewGuid(),
        UsuarioId = usuarioId,
        Canal = canal,
        Categoria = categoria,
        OptIn = optIn,
        AtualizadoEm = DateTime.UtcNow,
        AtualizadoPor = atualizadoPor,
        IpOrigem = ipOrigem,
        MotivoOptOut = optIn ? null : motivoOptOut
    };
}
