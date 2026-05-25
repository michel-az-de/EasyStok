using System;

namespace EasyStock.Domain.Entities
{
    public class UsuarioPerfil
    {
        public Guid Id { get; set; }
        public Guid UsuarioId { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid PerfilId { get; set; }
        public Guid? LojaId { get; set; }
        public DateTime AtribuidoEm { get; set; }
        public Guid? AtribuidoPorId { get; set; }

        public Usuario? Usuario { get; set; }
        public Perfil? Perfil { get; set; }
    }
}
