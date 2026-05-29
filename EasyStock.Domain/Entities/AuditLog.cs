namespace EasyStock.Domain.Entities
{
    public class AuditLog
    {
        public Guid Id { get; set; }
        public Guid UsuarioId { get; set; }
        public string Acao { get; set; } = null!; // login, logout, reset-password, etc.
        public DateTime DataHora { get; set; }
        public bool Sucesso { get; set; }
        public string? Detalhes { get; set; }
        public string? Ip { get; set; }
        public string? UserAgent { get; set; }

        public Usuario? Usuario { get; set; }

        public static AuditLog Criar(Guid usuarioId, string acao, bool sucesso, string? detalhes, string? ip, string? userAgent)
        {
            return new AuditLog
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuarioId,
                Acao = acao,
                DataHora = DateTime.UtcNow,
                Sucesso = sucesso,
                Detalhes = detalhes,
                Ip = ip,
                UserAgent = userAgent
            };
        }
    }
}