namespace EasyStock.Domain.Entities
{
    public class UsuarioEmpresa
    {
        public Guid Id { get; set; }
        public Guid UsuarioId { get; set; }
        public Guid EmpresaId { get; set; }
        public bool Ativo { get; set; }
        /// <summary>Titular da empresa (#2070, ADR-0038). Só a política de hierarquia muda esta marca.</summary>
        public bool EhDona { get; set; }
        public DateTime CriadoEm { get; set; }

        public Usuario? Usuario { get; set; }
        public Empresa? Empresa { get; set; }
    }
}
