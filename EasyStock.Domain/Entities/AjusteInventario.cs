using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Provenance do ajuste de inventario, criado no APPLY de uma <see cref="Contagem"/>
    /// (decisao travada: entidade DEDICADA, NAO reusa Saida/Entrada). Uma linha por lote
    /// com divergencia. Os agregados sao DERIVADOS das linhas (nao persistidos) — a config
    /// EF (Fatia 2) deve Ignore-los, espelhando Produto.CompletudePercent.
    /// </summary>
    public class AjusteInventario
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid ContagemId { get; set; }
        public Guid CriadoPorUsuarioId { get; set; }
        public DateTime CriadoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Contagem? Contagem { get; set; }
        public ICollection<AjusteInventarioLinha> Linhas { get; set; } = new List<AjusteInventarioLinha>();

        /// <summary>Lotes existentes mutados (Falta + Sobra).</summary>
        public int TotalMutados => Linhas.Count(l => l.Tipo is TipoAjusteLinha.Falta or TipoAjusteLinha.Sobra);

        /// <summary>Lotes criados na descoberta (LoteNovo).</summary>
        public int TotalCriados => Linhas.Count(l => l.Tipo == TipoAjusteLinha.LoteNovo);

        /// <summary>Lotes zerados (LoteZerado).</summary>
        public int TotalZerados => Linhas.Count(l => l.Tipo == TipoAjusteLinha.LoteZerado);

        /// <summary>Custo total da perda = soma de |delta| * custo nas linhas de delta negativo.</summary>
        public decimal CustoTotalPerda =>
            Linhas.Where(l => l.Delta < 0).Sum(l => Math.Abs(l.Delta) * l.CustoUnitarioSnapshot.Valor);

        public static AjusteInventario Criar(Guid empresaId, Guid contagemId, Guid criadoPorUsuarioId) =>
            new()
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ContagemId = contagemId,
                CriadoPorUsuarioId = criadoPorUsuarioId,
                CriadoEm = DateTime.UtcNow
            };

        /// <summary>Cria e anexa uma linha de delta. Delta (com sinal) = qtdDepois - qtdAntes.</summary>
        public AjusteInventarioLinha AdicionarLinha(
            Guid itemEstoqueId,
            Guid produtoId,
            Quantidade qtdAntes,
            Quantidade qtdDepois,
            Dinheiro custoUnitarioSnapshot,
            TipoAjusteLinha tipo)
        {
            var linha = AjusteInventarioLinha.Criar(
                EmpresaId, Id, itemEstoqueId, produtoId, qtdAntes, qtdDepois, custoUnitarioSnapshot, tipo);
            Linhas.Add(linha);
            return linha;
        }
    }

    /// <summary>
    /// Um delta por lote mutado/criado/zerado. <see cref="Delta"/> e SINAL (decimal),
    /// pois <see cref="Quantidade"/> e nao-negativa: Delta = QtdDepois - QtdAntes.
    /// </summary>
    public class AjusteInventarioLinha
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid AjusteInventarioId { get; set; }
        public Guid ItemEstoqueId { get; set; }
        public Guid ProdutoId { get; set; }

        public Quantidade QtdAntes { get; set; } = null!;
        public Quantidade QtdDepois { get; set; } = null!;

        /// <summary>QtdDepois - QtdAntes (com sinal). Negativo = falta; positivo = sobra.</summary>
        public decimal Delta { get; set; }

        public Dinheiro CustoUnitarioSnapshot { get; set; } = null!;
        public TipoAjusteLinha Tipo { get; set; }

        public AjusteInventario? AjusteInventario { get; set; }

        public static AjusteInventarioLinha Criar(
            Guid empresaId,
            Guid ajusteInventarioId,
            Guid itemEstoqueId,
            Guid produtoId,
            Quantidade qtdAntes,
            Quantidade qtdDepois,
            Dinheiro custoUnitarioSnapshot,
            TipoAjusteLinha tipo) =>
            new()
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                AjusteInventarioId = ajusteInventarioId,
                ItemEstoqueId = itemEstoqueId,
                ProdutoId = produtoId,
                QtdAntes = qtdAntes,
                QtdDepois = qtdDepois,
                Delta = qtdDepois.Value - qtdAntes.Value,
                CustoUnitarioSnapshot = custoUnitarioSnapshot,
                Tipo = tipo
            };
    }
}
