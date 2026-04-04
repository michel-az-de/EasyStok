using System;
using System.Collections.Generic;

namespace EasyStock.Domain.Entities
{
    public class Usuario
    {
        public Guid Id { get; set; }
        public string Nome { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string SenhaHash { get; set; } = null!;
        public bool Ativo { get; set; }
        public DateTime? UltimoAcessoEm { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

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
                CriadoEm = agora,
                AlteradoEm = agora
            };
        }

        public void AtualizarUltimoAcesso()
        {
            UltimoAcessoEm = DateTime.UtcNow;
            AlteradoEm = DateTime.UtcNow;
        }
    }
}
