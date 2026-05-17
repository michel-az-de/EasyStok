using System;

namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Modelo padrão de impressão por empresa.
    /// PK = EmpresaId — garante exatamente 1 default por empresa.
    /// TemplateOrigem = "Sistema" | "Empresa".
    /// </summary>
    public class EtiquetaEmpresaDefault
    {
        public Guid EmpresaId { get; set; }

        /// <summary>"Sistema" ou "Empresa".</summary>
        public string TemplateOrigem { get; set; } = null!;

        /// <summary>Id do modelo (EtiquetaTemplateSistema.Id ou EtiquetaTemplate.Id).</summary>
        public Guid TemplateId { get; set; }

        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
    }
}
