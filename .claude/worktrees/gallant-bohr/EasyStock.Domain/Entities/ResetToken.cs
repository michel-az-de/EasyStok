using System;

namespace EasyStock.Domain.Entities
{
    public class ResetToken
    {
        public Guid Id { get; set; }
        public Guid UsuarioId { get; set; }
        public string Token { get; set; } = null!;
        public DateTime CriadoEm { get; set; }
        public DateTime ExpiraEm { get; set; }
        public bool Usado { get; set; }
        public string? IpCriacao { get; set; }
        public string? UserAgent { get; set; }

        public Usuario? Usuario { get; set; }

        public static ResetToken Criar(Guid usuarioId, string token, DateTime expiraEm, string? ip, string? userAgent)
        {
            return new ResetToken
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuarioId,
                Token = token,
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