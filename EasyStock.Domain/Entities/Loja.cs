using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.ValueObjects.Pix;

namespace EasyStock.Domain.Entities
{
    public class Loja
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public string Nome { get; set; } = null!;
        public string? Descricao { get; set; }
        public string? Documento { get; set; }
        public string? Endereco { get; set; }
        public string? Telefone { get; set; }
        public string? LogoUrl { get; set; }
        public bool Ativa { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        /// <summary>#2233: chave Pix da loja para o Pix direto. As quatro colunas vêm juntas ou ficam todas nulas.</summary>
        public TipoChavePix? PixTipo { get; private set; }
        public string? PixChave { get; private set; }
        public string? PixNomeRecebedor { get; private set; }
        public string? PixCidade { get; private set; }

        public Empresa? Empresa { get; set; }
        public ICollection<ItemEstoque> Itens { get; set; } = new List<ItemEstoque>();
        public ICollection<Venda> Vendas { get; set; } = new List<Venda>();

        public static Loja Criar(Guid empresaId, string nome)
        {
            var agora = DateTime.UtcNow;
            return new Loja
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                Nome = (nome ?? string.Empty).Trim(), // consistencia com Cliente.Criar/Produto (sem espaco nas pontas)
                Ativa = true,
                CriadoEm = agora,
                AlteradoEm = agora
            };
        }

        /// <summary>Soft-delete. Mantém histórico de vendas/itens; loja não aparece em listagens ativas.</summary>
        public void Desativar()
        {
            if (!Ativa) return;
            Ativa = false;
            AlteradoEm = DateTime.UtcNow;
        }

        /// <summary>#2233: grava ou remove (null) a chave Pix da loja.</summary>
        public void DefinirRecebedorPix(RecebedorPix? recebedor)
        {
            PixTipo = recebedor?.Chave.Tipo;
            PixChave = recebedor?.Chave.Valor;
            PixNomeRecebedor = recebedor?.Nome;
            PixCidade = recebedor?.Cidade;
            AlteradoEm = DateTime.UtcNow;
        }

        /// <summary>#2233: recebedor do Pix direto, ou null quando a loja não cadastrou a chave.</summary>
        public RecebedorPix? ObterRecebedorPix() =>
            PixTipo is { } tipo && PixChave is not null && PixNomeRecebedor is not null && PixCidade is not null
                ? RecebedorPix.Criar(ChavePix.Criar(tipo, PixChave), PixNomeRecebedor, PixCidade)
                : null;

        public void Reativar()
        {
            if (Ativa) return;
            Ativa = true;
            AlteradoEm = DateTime.UtcNow;
        }
    }
}
