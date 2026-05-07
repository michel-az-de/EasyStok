using System;
using System.Collections.Generic;
using EasyStock.Domain.Enums;

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

        /// <summary>
        /// Nivel preferencial do atendente no helpdesk (N1..N4). NULL para usuarios
        /// que nao atuam no atendimento. Define a fila de tickets que ele ve por default.
        /// </summary>
        public NivelAtendimento? NivelAtendimentoPreferido { get; set; }

        public ICollection<UsuarioEmpresa> Empresas { get; set; } = new List<UsuarioEmpresa>();
        public ICollection<UsuarioPerfil> Perfis { get; set; } = new List<UsuarioPerfil>();

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
        /// Login fica impossivel (SenhaHash com prefixo bcrypt invalido + Ativo=false).
        /// Operacao irreversivel.
        /// </summary>
        public void Anonimizar()
        {
            var pseudoId = Id.ToString("N").Substring(0, 12);
            Nome = "[Anonimizado]";
            Email = $"anonimizado-{pseudoId}@anonimizado.local";
            AvatarUrl = null;
            // Hash invalido com prefixo bcrypt $2a$ — impede match com qualquer senha
            // (BCrypt.Verify devolve false para hash que nao bate o pattern). Usar
            // string.Empty era inseguro: alguns caminhos comparavam literal e podiam
            // aceitar entrada em branco.
            SenhaHash = $"$2a$10$INVALIDATED_{pseudoId}";
            Ativo = false;
            EmailConfirmado = false;
            FailedLoginAttempts = 0;
            LockoutEnd = null;
            AlteradoEm = DateTime.UtcNow;
        }
    }
}
