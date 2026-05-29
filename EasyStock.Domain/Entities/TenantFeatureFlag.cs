namespace EasyStock.Domain.Entities
{
    public class TenantFeatureFlag
    {
        public Guid Id { get; private set; }
        public Guid EmpresaId { get; private set; }
        public string Feature { get; private set; } = null!;
        public bool Ativo { get; private set; }
        public DateTime AlteradoEm { get; private set; }
        public string AlteradoPor { get; private set; } = null!;

        private TenantFeatureFlag() { }

        public static TenantFeatureFlag Criar(Guid empresaId, string feature, bool ativo, string adminEmail)
            => new()
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                Feature = feature,
                Ativo = ativo,
                AlteradoEm = DateTime.UtcNow,
                AlteradoPor = adminEmail
            };

        public void Atualizar(bool ativo, string adminEmail)
        {
            Ativo = ativo;
            AlteradoPor = adminEmail;
            AlteradoEm = DateTime.UtcNow;
        }
    }
}
