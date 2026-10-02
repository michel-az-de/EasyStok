namespace EasyStock.Domain.Entities
{
    /// <summary>Para que o segredo serve (N8, N9). Uma linha de <c>reset_tokens</c> só vale para a finalidade que a criou.</summary>
    public static class FinalidadeResetToken
    {
        /// <summary>Link de redefinição de senha enviado por e-mail: 32 bytes aleatórios em base64url, 30 min, 1 uso.</summary>
        public const string Reset = "Reset";

        /// <summary>Código de 6 dígitos enviado por WhatsApp: 10 min, 5 tentativas, 1 uso. Hash de <c>{Id}:{código}</c>.</summary>
        public const string ResetCodigo = "ResetCodigo";
    }

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

        /// <summary>Para que o segredo serve (<see cref="FinalidadeResetToken"/>). Linhas anteriores à N8 valem <c>Reset</c>.</summary>
        public string Finalidade { get; set; } = FinalidadeResetToken.Reset;

        /// <summary>Tentativas de confirmação já gastas (só o código usa). Quem conta é o UPDATE condicional do repositório.</summary>
        public int Tentativas { get; set; }

        /// <summary>Canal por onde o segredo sai (<c>Email</c>, <c>WhatsApp</c>). Informativo.</summary>
        public string? Canal { get; set; }

        /// <summary>Erros de código que matam o segredo: a 6ª tentativa nunca é registrada.</summary>
        public const int TentativasMaximas = 5;

        public Usuario? Usuario { get; set; }

        public static ResetToken Criar(
            Guid usuarioId, string tokenHash, DateTime expiraEm, string? ip, string? userAgent,
            string finalidade = FinalidadeResetToken.Reset, string? canal = null, Guid? id = null, DateTime? criadoEm = null)
        {
            return new ResetToken
            {
                Id = id ?? Guid.NewGuid(),
                UsuarioId = usuarioId,
                TokenHash = tokenHash,
                CriadoEm = criadoEm ?? DateTime.UtcNow,
                ExpiraEm = expiraEm,
                Usado = false,
                IpCriacao = ip,
                UserAgent = userAgent,
                Finalidade = finalidade,
                Canal = canal
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