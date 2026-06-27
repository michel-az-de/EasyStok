using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Sessao de contagem fisica de estoque (ADR Inventario). Conta por LOTE
    /// (= <see cref="ItemEstoque"/>). O baseline de cada lote
    /// (<see cref="ItemContagem.QtdSistemaNoMomento"/>) e capturado POR ITEM no
    /// ContadoEm, nao no start — modelo de concorrencia por DELTA que preserva venda
    /// durante a contagem. No start materializa-se so a membership (um ItemContagem
    /// por lote do escopo) para congelar o escopo (edge: categoria muda no meio) e dar
    /// progresso.
    /// </summary>
    public class Contagem
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }

        public EscopoContagem Escopo { get; set; }

        /// <summary>Id da Categoria (Escopo=Categoria) ou Loja (Escopo=Loja); null se Todos.</summary>
        public Guid? EscopoRefId { get; set; }

        public ModoContagem Modo { get; set; }
        public EstrategiaLoteContagem EstrategiaLote { get; set; }

        public StatusContagem Status { get; set; }

        public Guid CriadoPorUsuarioId { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime? IniciadoEm { get; set; }
        public DateTime? FinalizadoEm { get; set; }
        public Guid? AplicadoPorUsuarioId { get; set; }
        public DateTime? AplicadoEm { get; set; }

        public string? Observacao { get; set; }

        public Empresa? Empresa { get; set; }
        public ICollection<ItemContagem> Itens { get; set; } = new List<ItemContagem>();

        public bool EstaTerminal => Status is StatusContagem.Aplicada or StatusContagem.Cancelada;

        public static Contagem Criar(
            Guid empresaId,
            EscopoContagem escopo,
            Guid? escopoRefId,
            ModoContagem modo,
            EstrategiaLoteContagem estrategiaLote,
            Guid criadoPorUsuarioId,
            string? observacao = null)
        {
            if (escopo == EscopoContagem.Todos && escopoRefId is not null)
                throw new RegraDeDominioVioladaException("Escopo 'Todos' nao aceita EscopoRefId.");
            if (escopo != EscopoContagem.Todos && escopoRefId is null)
                throw new RegraDeDominioVioladaException($"Escopo '{escopo}' exige EscopoRefId.");

            var agora = DateTime.UtcNow;
            return new Contagem
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                Escopo = escopo,
                EscopoRefId = escopoRefId,
                Modo = modo,
                EstrategiaLote = estrategiaLote,
                Status = StatusContagem.Rascunho,
                CriadoPorUsuarioId = criadoPorUsuarioId,
                CriadoEm = agora,
                Observacao = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim()
            };
        }

        // ── Maquina de estados (ADR Inventario) ──────────────────────────────

        /// <summary>Rascunho -> EmAndamento. Gatilho do start (materializa membership no use case).</summary>
        public void Iniciar(DateTime quando)
        {
            ExigirStatus(StatusContagem.Rascunho, "iniciar");
            Status = StatusContagem.EmAndamento;
            IniciadoEm = quando;
        }

        public void Pausar()
        {
            ExigirStatus(StatusContagem.EmAndamento, "pausar");
            Status = StatusContagem.Pausada;
        }

        public void Retomar()
        {
            ExigirStatus(StatusContagem.Pausada, "retomar");
            Status = StatusContagem.EmAndamento;
        }

        public void Finalizar(DateTime quando)
        {
            ExigirStatus(StatusContagem.EmAndamento, "finalizar");
            Status = StatusContagem.Finalizada;
            FinalizadoEm = quando;
        }

        public void Reabrir()
        {
            ExigirStatus(StatusContagem.Finalizada, "reabrir");
            Status = StatusContagem.EmAndamento;
            FinalizadoEm = null;
        }

        /// <summary>Finalizada -> Aplicada (terminal). O apply em si vive no use case (Fatia 3).</summary>
        public void Aplicar(Guid aplicadoPorUsuarioId, DateTime quando)
        {
            ExigirStatus(StatusContagem.Finalizada, "aplicar");
            Status = StatusContagem.Aplicada;
            AplicadoPorUsuarioId = aplicadoPorUsuarioId;
            AplicadoEm = quando;
        }

        public void Cancelar()
        {
            if (EstaTerminal)
                throw new RegraDeDominioVioladaException(
                    $"Contagem em '{Status}' e terminal e nao pode ser cancelada.");
            Status = StatusContagem.Cancelada;
        }

        private void ExigirStatus(StatusContagem esperado, string acao)
        {
            if (Status != esperado)
                throw new RegraDeDominioVioladaException(
                    $"Nao e possivel {acao} uma contagem em '{Status}' (exige '{esperado}').");
        }
    }

    /// <summary>
    /// Uma linha por LOTE contado. <see cref="ItemEstoqueId"/> null = lote descoberto
    /// (so na estrategia Descoberta/V2; o apply cria o ItemEstoque). QtdSistemaNoMomento
    /// e CustoUnitarioSnapshot sao lidos NO <see cref="ContadoEm"/> (instante da contagem),
    /// NAO no start. Divergencia e derivada, nunca persistida como tabela.
    /// </summary>
    public class ItemContagem
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid ContagemId { get; set; }
        public Guid ProdutoId { get; set; }

        /// <summary>Null = lote descoberto (sera criado no apply).</summary>
        public Guid? ItemEstoqueId { get; set; }

        /// <summary>Validade lida na embalagem (lote descoberto).</summary>
        public Validade? Validade { get; set; }

        /// <summary>Qtd do sistema lida no ContadoEm = baseline da divergencia. Null ate contar.</summary>
        public Quantidade? QtdSistemaNoMomento { get; set; }

        /// <summary>Custo unitario do lote no ContadoEm (so lote existente). Null ate contar / descoberto.</summary>
        public Dinheiro? CustoUnitarioSnapshot { get; set; }

        public Quantidade? QtdContada { get; set; }
        public bool Conferido { get; set; }
        public Guid? ContadoPorUsuarioId { get; set; }
        public DateTime? ContadoEm { get; set; }

        public Contagem? Contagem { get; set; }
        public Produto? Produto { get; set; }
        public ItemEstoque? ItemEstoque { get; set; }

        /// <summary>
        /// Divergencia (shrinkage no instante da contagem) = contado - sistema. Null ate
        /// contar. Sinal (decimal): negativo = falta; positivo = sobra. Derivada, nao persistida.
        /// </summary>
        public decimal? Divergencia =>
            QtdContada is not null && QtdSistemaNoMomento is not null
                ? QtdContada.Value - QtdSistemaNoMomento.Value
                : null;

        /// <summary>
        /// Registra a contagem de um lote: captura o baseline do sistema NO MOMENTO,
        /// o custo, marca conferido. As quantidades sao nao-negativas (garantido por
        /// <see cref="Quantidade"/>). Para lote descoberto, qtdSistemaNoMomento = Zero.
        /// </summary>
        public void Contar(
            Quantidade qtdContada,
            Quantidade qtdSistemaNoMomento,
            Dinheiro? custoUnitarioSnapshot,
            Guid contadoPorUsuarioId,
            DateTime quando)
        {
            QtdContada = qtdContada;
            QtdSistemaNoMomento = qtdSistemaNoMomento;
            CustoUnitarioSnapshot = custoUnitarioSnapshot;
            ContadoPorUsuarioId = contadoPorUsuarioId;
            ContadoEm = quando;
            Conferido = true;
        }
    }
}
