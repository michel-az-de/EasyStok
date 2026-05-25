using System;
using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Entities
{
    public class PerfilPermissao
    {
        public Guid Id { get; set; }
        public Guid PerfilId { get; set; }
        public Permissao Permissao { get; set; }

        public Perfil? Perfil { get; set; }
    }
}
