using System;

namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Modelo de etiqueta personalizado por empresa.
    /// Pode ser baseado em um modelo de sistema (BaseSistemaId) ou criado do zero.
    /// RowVersion para controle de concorrência optimistic (409 em conflito).
    /// </summary>
    public class EtiquetaTemplate
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }

        public string Nome { get; set; } = null!;

        /// <summary>Modelo de sistema do qual foi duplicado, se aplicável.</summary>
        public Guid? BaseSistemaId { get; set; }

        /// <summary>JSON v=1 do layout.</summary>
        public string LayoutJson { get; set; } = null!;

        /// <summary>Se true, é o modelo padrão da empresa para impressão. Constraint unique parcial no banco.</summary>
        public bool IsDefault { get; set; }

        /// <summary>Ignorado pelo EF — xmin do Postgres é shadow property na configuração.</summary>
        public uint RowVersion { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public EtiquetaTemplateSistema? BaseSistema { get; set; }
    }
}
