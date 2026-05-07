using System;
using System.Collections.Generic;
using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Entities
{
    public class Perfil
    {
        public Guid Id { get; set; }
        public Guid? EmpresaId { get; set; }
        public string Nome { get; set; } = null!;
        public string? Descricao { get; set; }
        public NivelAcesso Nivel { get; set; }
        public DateTime CriadoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public ICollection<PerfilPermissao> Permissoes { get; set; } = new List<PerfilPermissao>();
        public ICollection<UsuarioPerfil> Usuarios { get; set; } = new List<UsuarioPerfil>();
    }
}
