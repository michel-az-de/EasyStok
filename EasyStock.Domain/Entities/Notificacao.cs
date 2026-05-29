namespace EasyStock.Domain.Entities
{
    public class Notificacao
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public TipoAlertaEstoque TipoAlerta { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string Mensagem { get; set; } = null!;
        public SeveridadeNotificacao Severidade { get; set; } = SeveridadeNotificacao.Media;
        public bool Lida { get; set; }
        public Guid? UsuarioId { get; set; }
        public Guid? ReferenciaId { get; set; }
        public DateTime CriadaEm { get; set; }
        public DateTime? LidaEm { get; set; }

        /// <summary>
        /// Correlaciona o item in-app com a mensagem do outbox que originou o envio multi-canal.
        /// Null para notificações criadas pelo gerador legado (apenas in-app de estoque).
        /// </summary>
        public Guid? OutboxMensagemId { get; set; }

        public Empresa? Empresa { get; set; }

        public static Notificacao Criar(
            Guid empresaId,
            TipoAlertaEstoque tipo,
            string titulo,
            string mensagem,
            SeveridadeNotificacao severidade,
            Guid? referenciaId = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                TipoAlerta = tipo,
                Titulo = titulo,
                Mensagem = mensagem,
                Severidade = severidade,
                Lida = false,
                ReferenciaId = referenciaId,
                CriadaEm = DateTime.UtcNow
            };

        /// <summary>Backward-compatible overload for callers that don't set titulo/severidade.</summary>
        public static Notificacao Criar(Guid empresaId, TipoAlertaEstoque tipo, string mensagem, Guid? referenciaId = null) =>
            Criar(empresaId, tipo, TituloParaTipo(tipo), mensagem, SeveridadePadrao(tipo), referenciaId);

        public void AtualizarMensagem(string mensagem)
        {
            Mensagem = mensagem;
        }

        public void MarcarComoLida()
        {
            Lida = true;
            LidaEm = DateTime.UtcNow;
        }

        private static string TituloParaTipo(TipoAlertaEstoque tipo) => tipo switch
        {
            TipoAlertaEstoque.EstoqueCritico => "Estoque Crítico",
            TipoAlertaEstoque.ProdutoParado => "Produto Parado",
            TipoAlertaEstoque.ValidadeProxima => "Validade Próxima",
            TipoAlertaEstoque.ReposicaoSugerida => "Reposição Sugerida",
            TipoAlertaEstoque.PedidoAtrasado => "Pedido Atrasado",
            TipoAlertaEstoque.PedidoRecebido => "Pedido Recebido",
            TipoAlertaEstoque.ProdutoVencido => "Produto Vencido",
            _ => "Notificação"
        };

        private static SeveridadeNotificacao SeveridadePadrao(TipoAlertaEstoque tipo) => tipo switch
        {
            TipoAlertaEstoque.EstoqueCritico => SeveridadeNotificacao.Alta,
            TipoAlertaEstoque.ProdutoVencido => SeveridadeNotificacao.Critica,
            TipoAlertaEstoque.ValidadeProxima => SeveridadeNotificacao.Media,
            TipoAlertaEstoque.ProdutoParado => SeveridadeNotificacao.Media,
            TipoAlertaEstoque.ReposicaoSugerida => SeveridadeNotificacao.Media,
            TipoAlertaEstoque.PedidoAtrasado => SeveridadeNotificacao.Alta,
            TipoAlertaEstoque.PedidoRecebido => SeveridadeNotificacao.Informativa,
            _ => SeveridadeNotificacao.Media
        };
    }
}
