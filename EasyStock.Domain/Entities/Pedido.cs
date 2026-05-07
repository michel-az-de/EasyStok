using System;
using System.Collections.Generic;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Pedido (encomenda em curso) do ERP — Onda P2. Representa a fase
    /// PRÉ-<see cref="Venda"/>: status Aguardando → Preparando → Pronto →
    /// Entregue. Quando entregue + cobrado, pode gerar uma Venda no ERP
    /// (Onda 3 do mobile cuida desse pipeline).
    ///
    /// Estrutura **expansível** semelhante a <see cref="Cliente"/> e
    /// <see cref="Produto"/>: campos primários na raiz + tabelas auxiliares
    /// 1:N (itens, eventos de status, pagamentos).
    ///
    /// <para>
    /// Snapshot do cliente: <see cref="ClienteNome"/>, <see cref="ClienteApt"/>,
    /// <see cref="ClienteTelefone"/> são copiados do <see cref="Cliente"/>
    /// no momento da criação. Se o cliente editar dados depois, o pedido
    /// preserva como era na hora — importante pra histórico/audit.
    /// </para>
    ///
    /// <para>
    /// Status: <see cref="Status"/> (string) é o formato legado preservado
    /// pra compat com DB, PWA, mobile e MAUI. Use <see cref="StatusEnum"/>
    /// pra leitura tipada e <see cref="MudarStatus"/> pra mudanças validadas
    /// pela <see cref="PedidoStateMachine"/>.
    /// </para>
    /// </summary>
    public class Pedido
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid? LojaId { get; set; }

        /// <summary>Cliente vinculado (opcional pra pedidos balcao/anônimos).</summary>
        public Guid? ClienteId { get; set; }
        public Cliente? Cliente { get; set; }

        // ── Snapshot do cliente no momento ────────────────────────────
        public string? ClienteNome { get; set; }
        public string? ClienteApt { get; set; }
        public string? ClienteTelefone { get; set; }

        /// <summary>
        /// Status legado em string lowercase: "aguardando" | "preparando" |
        /// "pronto" | "entregue" | "cancelado". Mantido público e mutable
        /// pra compat com Sync mobile, EF, testes existentes e SeedData.
        ///
        /// Pra mudanças seguras com validação de transição use
        /// <see cref="MudarStatus"/>. Pra leitura tipada use
        /// <see cref="StatusEnum"/>.
        /// </summary>
        public string Status { get; set; } = StatusPedidoMapper.Aguardando;

        /// <summary>
        /// Leitura tipada do <see cref="Status"/>. Lança
        /// <see cref="ArgumentException"/> se a string atual não for um
        /// status conhecido (sinaliza dado corrompido em DB ou bug de
        /// caller que escreveu valor inválido).
        /// </summary>
        public StatusPedido StatusEnum => StatusPedidoMapper.Parse(Status);

        /// <summary>
        /// Total agregado do pedido (soma dos <see cref="PedidoItem.Subtotal"/>).
        /// Tipado como <see cref="Dinheiro"/> (value object, BRL, 2 decimais,
        /// imutável, não-negativo). Persistido em DB como <c>numeric(14,2)</c>
        /// via EF value converter — schema inalterado.
        ///
        /// <para>
        /// Implicit operator <c>Dinheiro → decimal</c> garante compat com logs
        /// e DTOs que esperam decimal. Atribuição <c>pedido.Total = 100m</c>
        /// não funciona mais — use <c>Dinheiro.FromDecimal(100m)</c> ou
        /// <see cref="RecalcularTotal"/>.
        /// </para>
        /// </summary>
        public Dinheiro Total { get; set; } = Dinheiro.Zero;

        public string? Observacoes { get; set; }

        /// <summary>"web" | "mobile" | "api". Útil pra filtrar origem.</summary>
        public string? Origem { get; set; }

        /// <summary>
        /// ID do pedido no app mobile (mobile_orders.id) quando o pedido foi
        /// originado lá. Permite linkar reverso e evitar duplicação na sync.
        /// </summary>
        public string? MobileOrderId { get; set; }

        /// <summary>
        /// ID da <see cref="Venda"/> gerada quando o pedido foi
        /// entregue+cobrado. Null = ainda não consolidado.
        /// </summary>
        public Guid? VendaId { get; set; }
        public Venda? Venda { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }
        public DateTime? EntreguEm { get; set; }
        public DateTime? CanceladoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Loja? Loja { get; set; }

        // ── Coleções 1:N ─────────────────────────────────────────────
        public ICollection<PedidoItem> Itens { get; set; } = new List<PedidoItem>();
        public ICollection<PedidoEvento> Eventos { get; set; } = new List<PedidoEvento>();
        public ICollection<PedidoPagamento> Pagamentos { get; set; } = new List<PedidoPagamento>();

        public static Pedido Criar(Guid empresaId, Cliente? cliente = null, Guid? lojaId = null, string? origem = "web")
        {
            var agora = DateTime.UtcNow;
            return new Pedido
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                LojaId = lojaId,
                ClienteId = cliente?.Id,
                ClienteNome = cliente?.Nome,
                ClienteApt = cliente?.Apt,
                ClienteTelefone = cliente?.Telefone,
                Status = StatusPedidoMapper.Aguardando,
                Total = Dinheiro.Zero,
                Origem = origem,
                CriadoEm = agora,
                AlteradoEm = agora
            };
        }

        public void RecalcularTotal()
        {
            decimal soma = 0m;
            foreach (var i in Itens) soma += i.Subtotal;
            // Subtotais teoricamente nunca são negativos (Quantidade*PrecoUnitario
            // de itens válidos). Se vier soma < 0 por bug em PedidoItem,
            // FromDecimal lança ArgumentOutOfRangeException — sinaliza
            // dado corrompido em vez de propagar silenciosamente.
            Total = Dinheiro.FromDecimal(soma);
            AlteradoEm = DateTime.UtcNow;
        }

        public bool EstaFinalizado => PedidoStateMachine.EstaFinalizado(StatusEnum);

        /// <summary>
        /// Aplica transição de status validada pela
        /// <see cref="PedidoStateMachine"/>. Idempotente: se já estiver no
        /// status alvo, é no-op silenciosa.
        ///
        /// <para>
        /// Side effects automáticos: <see cref="EntreguEm"/> setado em
        /// transição para Entregue, <see cref="CanceladoEm"/> em transição
        /// para Cancelado. <see cref="AlteradoEm"/> sempre atualiza.
        /// </para>
        ///
        /// <exception cref="TransicaoInvalidaException">
        /// Se a transição do status atual pra <paramref name="novo"/> não
        /// for permitida pela máquina de estados.
        /// </exception>
        /// </summary>
        public void MudarStatus(StatusPedido novo)
        {
            var atual = StatusEnum;
            if (atual == novo) return; // idempotência

            PedidoStateMachine.EnsureTransicaoValida(atual, novo);

            var agora = DateTime.UtcNow;
            Status = StatusPedidoMapper.Format(novo);
            AlteradoEm = agora;

            if (novo == StatusPedido.Entregue) EntreguEm = agora;
            else if (novo == StatusPedido.Cancelado) CanceladoEm = agora;
        }

        public void MarcarEntregue() => MudarStatus(StatusPedido.Entregue);

        public void Cancelar() => MudarStatus(StatusPedido.Cancelado);

        public decimal TotalPago
        {
            get
            {
                decimal s = 0m;
                foreach (var p in Pagamentos) s += p.Valor;
                return s;
            }
        }
    }

    /// <summary>Item do pedido. Snapshot de nome/preço pra preservar histórico.</summary>
    public class PedidoItem
    {
        public Guid Id { get; set; }
        public Guid PedidoId { get; set; }

        /// <summary>Pode ser null pra item ad-hoc (não está no catálogo).</summary>
        public Guid? ProdutoId { get; set; }

        /// <summary>Snapshot do nome no momento (preserva mesmo se Produto for renomeado).</summary>
        public string Nome { get; set; } = null!;
        public string? Emoji { get; set; }
        public string? Unidade { get; set; }

        public decimal Quantidade { get; set; }
        public decimal PrecoUnitario { get; set; }
        public decimal Subtotal { get; set; }

        public string? Observacao { get; set; }
        public DateTime CriadoEm { get; set; }

        public Pedido? Pedido { get; set; }
        public Produto? Produto { get; set; }

        public void RecalcularSubtotal()
        {
            Subtotal = Quantidade * PrecoUnitario;
        }
    }

    /// <summary>
    /// Evento no ciclo de vida do pedido: criação, mudança de status,
    /// cancelamento, etc. Trail completo pra audit.
    /// </summary>
    public class PedidoEvento
    {
        public Guid Id { get; set; }
        public Guid PedidoId { get; set; }

        /// <summary>"criado" | "status_changed" | "item_added" | "item_removed" | "pagamento" | "cancelado".</summary>
        public string Tipo { get; set; } = null!;

        /// <summary>Status anterior (em mudanças de status).</summary>
        public string? StatusAntigo { get; set; }
        public string? StatusNovo { get; set; }

        public string? Detalhes { get; set; }
        public Guid? UsuarioId { get; set; }
        public string? UsuarioNome { get; set; }
        public string? Origem { get; set; }
        public DateTime OcorridoEm { get; set; }

        public Pedido? Pedido { get; set; }
    }

    /// <summary>
    /// Pagamento (parcial ou total) do pedido. Casa da Baba às vezes recebe
    /// metade na entrega + metade no PIX depois — múltiplos pagamentos por pedido.
    /// </summary>
    public class PedidoPagamento
    {
        public Guid Id { get; set; }
        public Guid PedidoId { get; set; }

        /// <summary>"pix" | "dinheiro" | "credito" | "debito" | "transferencia" | "outro".</summary>
        public string Metodo { get; set; } = "outro";
        public decimal Valor { get; set; }

        /// <summary>Identificador externo (txid PIX, NSU cartão, etc).</summary>
        public string? Referencia { get; set; }
        public string? Observacao { get; set; }

        public DateTime PagoEm { get; set; }
        public Guid? RegistradoPorUserId { get; set; }
        public string? RegistradoPorNome { get; set; }

        public Pedido? Pedido { get; set; }
    }
}
