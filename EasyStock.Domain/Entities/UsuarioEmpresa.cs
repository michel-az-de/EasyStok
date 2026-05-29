namespace EasyStock.Domain.Entities
{
    public class UsuarioEmpresa
    {
        public Guid Id { get; set; }
        public Guid UsuarioId { get; set; }
        public Guid EmpresaId { get; set; }
        public bool Ativo { get; set; }
        public DateTime CriadoEm { get; set; }

        public Usuario? Usuario { get; set; }
        public Empresa? Empresa { get; set; }
    }
}
