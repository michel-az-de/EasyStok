using System;
using System.Collections.Generic;
using System.Linq;

namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Lote de produção do ERP — Onda P5. Espelha <see cref="Mobile.Batch"/>
    /// do app: cada turno de produção vira um Lote com os itens produzidos.
    ///
    /// Estrutura expansível:
    ///   - Raiz: código sequencial do dia (LOT-YYMMDD-001), data, operador, status
    ///   - Itens 1:N: produto, qty, peso/un, validade em dias, expira em
    ///   - Etiquetas: cada unidade do item gera uma etiqueta com sequencial
    ///     (1/N, 2/N, ...) — modeladas como <see cref="LoteEtiqueta"/>
    ///
    /// Status:
    ///   em_producao  — recém-criado, pode receber/remover itens
    ///   finalizado   — congelado, etiquetas geradas, conferência possível
    ///   expirado     — todas etiquetas vencidas (auto via job futuro)
    /// </summary>
    public class Lote
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid? LojaId { get; set; }

        /// <summary>Código do lote (ex: "LOT-260430-001").</summary>
        public string Codigo { get; set; } = null!;

        public string Status { get; set; } = "em_producao";

        public DateTime DataProducao { get; set; }

        public Guid? OperadorUserId { get; set; }
        public string? OperadorNome { get; set; }
        public string? Observacoes { get; set; }
        public string? FotoUrl { get; set; }

        /// <summary>Origem (web/mobile/api).</summary>
        public string? Origem { get; set; }
        /// <summary>Id do batch no app (mobile_batches.id) quando promovido de lá.</summary>
        public string? MobileBatchId { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }
        public DateTime? FinalizadoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Loja? Loja { get; set; }

        public ICollection<LoteItem> Itens { get; set; } = new List<LoteItem>();
        public ICollection<LoteEtiqueta> Etiquetas { get; set; } = new List<LoteEtiqueta>();

        public bool EstaFinalizado => Status == "finalizado" || Status == "expirado";

        public static Lote Criar(Guid empresaId, string codigo, DateTime? dataProducao = null, Guid? lojaId = null)
        {
            var agora = DateTime.UtcNow;
            return new Lote
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                LojaId = lojaId,
                Codigo = codigo,
                Status = "em_producao",
                DataProducao = dataProducao ?? agora,
                CriadoEm = agora,
                AlteradoEm = agora
            };
        }

        public void Finalizar()
        {
            Status = "finalizado";
            FinalizadoEm = DateTime.UtcNow;
            AlteradoEm = DateTime.UtcNow;
        }

        public int TotalUnidades => Itens.Sum(i => i.Quantidade);
    }

    /// <summary>Item produzido no lote. Cada unidade vira uma <see cref="LoteEtiqueta"/>.</summary>
    public class LoteItem
    {
        public Guid Id { get; set; }
        public Guid LoteId { get; set; }
        public Guid? ProdutoId { get; set; }

        public string Nome { get; set; } = null!;
        public string? Emoji { get; set; }
        public string? Unidade { get; set; }

        public int Quantidade { get; set; }
        /// <summary>Peso unitário em gramas (vai na etiqueta).</summary>
        public int? PesoG { get; set; }
        /// <summary>Validade em dias a partir de DataProducao do lote.</summary>
        public int? ValidadeDias { get; set; }
        /// <summary>Calculado no save: DataProducao + ValidadeDias.</summary>
        public DateTime? ExpiraEm { get; set; }

        public string? FotoUrl { get; set; }
        public DateTime CriadoEm { get; set; }

        public Lote? Lote { get; set; }
        public Produto? Produto { get; set; }
    }

    /// <summary>
    /// Etiqueta individual (uma por unidade produzida). Permite conferência
    /// granular: scanner do app marca cada etiqueta conferida, identifica
    /// divergências (faltou, sobrou, errado).
    /// </summary>
    public class LoteEtiqueta
    {
        public Guid Id { get; set; }
        public Guid LoteId { get; set; }
        public Guid LoteItemId { get; set; }

        /// <summary>Sequencial dentro do lote (1, 2, 3...).</summary>
        public int Sequencial { get; set; }

        /// <summary>Código pra barcode/QR (ex: "LOT-260430-001-0042").</summary>
        public string Codigo { get; set; } = null!;

        // "pendente" | "enviada_impressao" | "impressa" | "conferida" | "divergente" | "consumida"
        public string Status { get; set; } = LoteEtiquetaStatus.Pendente;
        public DateTime? ConferidaEm { get; set; }
        public Guid? ConferidaPorUserId { get; set; }
        public string? ConferidaPorNome { get; set; }
        public string? ObservacaoConferencia { get; set; }

        /// <summary>Snapshot completo do LayoutJson no momento da impressão. Imutável após gravado.</summary>
        public string? LayoutSnapshotJson { get; set; }

        /// <summary>Metadados do snapshot: { origem, id, nome, snapshotAt }.</summary>
        public string? LayoutSnapshotMeta { get; set; }

        public DateTime CriadoEm { get; set; }

        public Lote? Lote { get; set; }
        public LoteItem? LoteItem { get; set; }
    }

    /// <summary>Constantes de status da etiqueta — evita strings mágicas.</summary>
    public static class LoteEtiquetaStatus
    {
        public const string Pendente          = "pendente";
        public const string EnviadaImpressao  = "enviada_impressao";
        public const string Impressa          = "impressa";
        public const string Conferida         = "conferida";
        public const string Divergente        = "divergente";
        public const string Consumida         = "consumida";

        public static readonly IReadOnlyList<string> Todos =
            [Pendente, EnviadaImpressao, Impressa, Conferida, Divergente, Consumida];
    }
}
