namespace EasyStock.Domain.Entities
{
    public class RefreshToken
    {
        public Guid Id { get; set; }
        public Guid UsuarioId { get; set; }
        public string TokenHash { get; set; } = null!;
        public DateTime CriadoEm { get; set; }
        public DateTime ExpiraEm { get; set; }
        public bool Revogado { get; set; }
        public DateTime? RevogadoEm { get; set; }
        public string? IpCriacao { get; set; }
        public string? UserAgent { get; set; }

        /// <summary>
        /// A empresa da sessão emitida (#2070, M3): o refresh renova nela. Nulo em token antigo ou sessão sem empresa, e aí
        /// vale a regra de antes (a única empresa ativa do usuário).
        /// </summary>
        public Guid? EmpresaDaSessaoId { get; set; }

        public Usuario? Usuario { get; set; }

        public static RefreshToken Criar(
            Guid usuarioId, string tokenHash, DateTime expiraEm, string? ip, string? userAgent, Guid? empresaDaSessaoId = null)
        {
            return new RefreshToken
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuarioId,
                TokenHash = tokenHash,
                CriadoEm = DateTime.UtcNow,
                ExpiraEm = expiraEm,
                Revogado = false,
                IpCriacao = ip,
                UserAgent = userAgent,
                EmpresaDaSessaoId = empresaDaSessaoId
            };
        }

        public void Revogar()
        {
            Revogado = true;
            RevogadoEm = DateTime.UtcNow;
        }

        public bool EstaValido()
        {
            return !Revogado && ExpiraEm > DateTime.UtcNow;
        }
    }
}