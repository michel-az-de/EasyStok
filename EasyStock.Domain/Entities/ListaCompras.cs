using System;
using System.Collections.Generic;
using System.Linq;

namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Lista de compras (Onda P5.B). Paridade com a lista do app +
    /// expansão pra ERP: agrupada por dia, com items que viram done quando
    /// alguém marca, e pode ser arquivada quando todos itens compraram.
    /// </summary>
    public class ListaCompras
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid? LojaId { get; set; }

        public string Nome { get; set; } = null!;
        /// <summary>"aberta" | "arquivada".</summary>
        public string Status { get; set; } = "aberta";
        public string? Observacoes { get; set; }

        public Guid? CriadaPorUserId { get; set; }
        public string? CriadaPorNome { get; set; }
        public string? Origem { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }
        public DateTime? ArquivadoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Loja? Loja { get; set; }

        public ICollection<ItemListaCompras> Itens { get; set; } = new List<ItemListaCompras>();

        public static ListaCompras Criar(Guid empresaId, string nome, Guid? lojaId = null, string? origem = "web")
        {
            var agora = DateTime.UtcNow;
            return new ListaCompras
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                LojaId = lojaId,
                Nome = nome.Trim(),
                Status = "aberta",
                Origem = origem,
                CriadoEm = agora,
                AlteradoEm = agora
            };
        }

        public int TotalItens => Itens.Count;
        public int ItensFeitos => Itens.Count(i => i.Done);
        public int ItensPendentes => Itens.Count(i => !i.Done);
        public bool EstaArquivada => Status == "arquivada";

        public void Arquivar()
        {
            Status = "arquivada";
            ArquivadoEm = DateTime.UtcNow;
            AlteradoEm = DateTime.UtcNow;
        }

        public void Reabrir()
        {
            Status = "aberta";
            ArquivadoEm = null;
            AlteradoEm = DateTime.UtcNow;
        }
    }

    public class ItemListaCompras
    {
        public Guid Id { get; set; }
        public Guid ListaComprasId { get; set; }
        /// <summary>Produto de origem quando o item veio da geração automática; null em itens manuais (texto livre).</summary>
        public Guid? ProdutoId { get; set; }

        public string Texto { get; set; } = null!;
        public decimal? Quantidade { get; set; }
        public string? Unidade { get; set; }
        public string? Observacao { get; set; }
        public string? Categoria { get; set; }

        public bool Done { get; set; }
        public DateTime? DoneEm { get; set; }
        public Guid? DonePorUserId { get; set; }
        public string? DonePorNome { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public ListaCompras? ListaCompras { get; set; }

        public void MarcarDone(Guid? userId, string? userNome)
        {
            Done = true;
            DoneEm = DateTime.UtcNow;
            DonePorUserId = userId;
            DonePorNome = userNome;
            AlteradoEm = DateTime.UtcNow;
        }

        public void Desmarcar()
        {
            Done = false;
            DoneEm = null;
            DonePorUserId = null;
            DonePorNome = null;
            AlteradoEm = DateTime.UtcNow;
        }
    }
}
