using System;

namespace EasyStock.Domain.Entities
{
    public class EmailConfirmationToken
    {
        public Guid Id { get; set; }
        public Guid UsuarioId { get; set; }
        // SHA-256 do token enviado por email. Plaintext nunca persiste — em
        // breach o atacante não consegue confirmar email com o que está no DB.
        public string TokenHash { get; set; } = null!;
        public DateTime CriadoEm { get; set; }
        public DateTime ExpiraEm { get; set; }
        public bool Confirmado { get; set; }
        public DateTime? ConfirmadoEm { get; set; }
        public string? IpCriacao { get; set; }
        public string? UserAgent { get; set; }

        public Usuario? Usuario { get; set; }

        public static EmailConfirmationToken Criar(Guid usuarioId, string tokenHash, string? ip, string? userAgent)
        {
            return new EmailConfirmationToken
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuarioId,
                TokenHash = tokenHash,
                CriadoEm = DateTime.UtcNow,
                ExpiraEm = DateTime.UtcNow.AddHours(24),
                Confirmado = false,
                ConfirmadoEm = null,
                IpCriacao = ip,
                UserAgent = userAgent
            };
        }

        public bool EstaValido() => !Confirmado && ExpiraEm > DateTime.UtcNow;

        public void MarcarComoConfirmado()
        {
            Confirmado = true;
            ConfirmadoEm = DateTime.UtcNow;
        }
    }
}
