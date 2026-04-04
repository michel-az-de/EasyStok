using System;
using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Entities
{
    public class AssinaturaEmpresa
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid PlanoId { get; set; }
        public DateTime DataInicio { get; set; }
        public DateTime? DataFim { get; set; }
        public StatusAssinatura Status { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Plano? Plano { get; set; }

        public void Suspender()
        {
            Status = StatusAssinatura.Suspensa;
            AlteradoEm = DateTime.UtcNow;
        }

        public void Cancelar()
        {
            Status = StatusAssinatura.Cancelada;
            AlteradoEm = DateTime.UtcNow;
        }

        public void Reativar()
        {
            Status = StatusAssinatura.Ativa;
            AlteradoEm = DateTime.UtcNow;
        }
    }
}
