using System;
using System.Collections.Generic;

namespace EasyStock.Domain.Entities
{
    public class Usuario
    {
        public Guid Id { get; set; }
        public string Nome { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? AvatarUrl { get; set; }
        public string TemaPreferido { get; set; } = "light";
        public string SenhaHash { get; set; } = null!;
        public bool Ativo { get; set; }
        public bool EmailConfirmado { get; set; }
        public DateTime? UltimoAcessoEm { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }
        public int FailedLoginAttempts { get; set; }
        public DateTime? LockoutEnd { get; set; }

        public ICollection<UsuarioEmpresa>? Empresas { get; set; }
        public ICollection<UsuarioPerfil>? Perfis { get; set; }

        public static Usuario Criar(string nome, string email, string senhaHash)
        {
            var agora = DateTime.UtcNow;
            return new Usuario
            {
                Id = Guid.NewGuid(),
                Nome = nome,
                Email = email,
                SenhaHash = senhaHash,
                Ativo = true,
                EmailConfirmado = false,
                CriadoEm = agora,
                AlteradoEm = agora,
                FailedLoginAttempts = 0,
                LockoutEnd = null
            };
        }

        public void AtualizarUltimoAcesso()
        {
            UltimoAcessoEm = DateTime.UtcNow;
            AlteradoEm = DateTime.UtcNow;
        }

        public void IncrementarTentativasFalha()
        {
            FailedLoginAttempts++;
            AlteradoEm = DateTime.UtcNow;
        }

        public void ResetarTentativasFalha()
        {
            FailedLoginAttempts = 0;
            LockoutEnd = null;
            AlteradoEm = DateTime.UtcNow;
        }

        public void BloquearPorTentativas(int minutosLockout = 15)
        {
            LockoutEnd = DateTime.UtcNow.AddMinutes(minutosLockout);
            AlteradoEm = DateTime.UtcNow;
        }

        public bool EstaBloqueado()
        {
            return LockoutEnd.HasValue && LockoutEnd > DateTime.UtcNow;
        }

        /// <summary>
        /// LGPD Art. 18 — direito ao esquecimento. Substitui campos PII por valores
        /// pseudonimizados deterministicos baseados no Id (preserva FKs em audit logs,
        /// movimentacoes e demais entidades com valor historico/forense).
        /// Login fica impossivel (SenhaHash=null + Ativo=false). Operacao irreversivel.
        /// </summary>
        public void Anonimizar()
        {
            var pseudoId = Id.ToString("N").Substring(0, 12);
            Nome = "[Anonimizado]";
            Email = $"anonimizado-{pseudoId}@anonimizado.local";
            AvatarUrl = null;
            SenhaHash = string.Empty;
            Ativo = false;
            EmailConfirmado = false;
            FailedLoginAttempts = 0;
            LockoutEnd = null;
            AlteradoEm = DateTime.UtcNow;
        }
    }
}
