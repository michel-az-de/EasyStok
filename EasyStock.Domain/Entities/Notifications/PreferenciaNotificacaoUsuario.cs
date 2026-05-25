using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Domain.Entities.Notifications;

public class PreferenciaNotificacaoUsuario
{
    public Guid UsuarioId { get; set; }
    public Guid EmpresaId { get; set; }
    public string RotinaCodigo { get; set; } = null!;
    public bool Habilitada { get; set; } = true;
    public CanalNotificacao? CanalPreferido { get; set; }
    public DateTime AtualizadaEm { get; set; }

    public Usuario? Usuario { get; set; }
    public Empresa? Empresa { get; set; }

    public static PreferenciaNotificacaoUsuario Criar(
        Guid usuarioId,
        Guid empresaId,
        string rotinaCodigo,
        bool habilitada = true,
        CanalNotificacao? canalPreferido = null) => new()
        {
            UsuarioId = usuarioId,
            EmpresaId = empresaId,
            RotinaCodigo = rotinaCodigo,
            Habilitada = habilitada,
            CanalPreferido = canalPreferido,
            AtualizadaEm = DateTime.UtcNow
        };

    public void Atualizar(bool habilitada, CanalNotificacao? canalPreferido)
    {
        Habilitada = habilitada;
        CanalPreferido = canalPreferido;
        AtualizadaEm = DateTime.UtcNow;
    }
}
