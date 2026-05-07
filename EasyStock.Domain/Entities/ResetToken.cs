using System;

namespace EasyStock.Domain.Entities
{
    public class ResetToken
    {
        public Guid Id { get; set; }
        public Guid UsuarioId { get; set; }
        // SHA-256 do token enviado por email. Plaintext nunca persiste — em
        // breach o atacante não consegue redefinir senha com o que está no DB.
        public string TokenHash { get; set; } = null!;
        public DateTime CriadoEm { get; set; }
        public DateTime ExpiraEm { get; set; }
        public bool Usado { get; set; }
        public string? IpCriacao { get; set; }
        public string? UserAgent { get; set; }

        public Usuario? Usuario { get; set; }

        public static ResetToken Criar(Guid usuarioId, string tokenHash, DateTime expiraEm, string? ip, string? userAgent)
        {
            return new ResetToken
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuarioId,
                TokenHash = tokenHash,
                CriadoEm = DateTime.UtcNow,
                ExpiraEm = expiraEm,
                Usado = false,
                IpCriacao = ip,
                UserAgent = userAgent
            };
        }

        public void MarcarComoUsado()
        {
            Usado = true;
        }

        public bool EstaValido()
        {
            return !Usado && ExpiraEm > DateTime.UtcNow;
        }
    }
}