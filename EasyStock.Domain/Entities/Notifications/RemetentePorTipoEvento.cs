using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Domain.Entities.Notifications;

/// <summary>
/// Remetente de cada tipo de evento (N6). <b>Loja</b> só quando quem recebe é o cliente final; <b>Plataforma</b> para
/// os demais, cujo destinatário é usuário interno. Sem valor padrão: tipo novo no enum sem classificação aqui lança, e
/// o teste <c>TodoValorDoEnumTemRemetenteDeclarado</c> acusa antes de o esquecimento chegar à produção.
/// </summary>
public static class RemetentePorTipoEvento
{
    public static OrigemRemetente De(TipoEventoNotificacao tipo) => tipo switch
    {
        // Cliente final: sai pelo número da empresa (S13, S26, S27, S30).
        TipoEventoNotificacao.PedidoPagoConfirmado
            or TipoEventoNotificacao.PedidoEmPreparo
            or TipoEventoNotificacao.PedidoSaiuParaEntrega
            or TipoEventoNotificacao.PedidoEntregue
            or TipoEventoNotificacao.AvaliacaoSolicitada
            or TipoEventoNotificacao.ReembolsoEfetuado
            or TipoEventoNotificacao.CampanhaMarketing
            or TipoEventoNotificacao.CampanhaLembreteEncerramento => OrigemRemetente.Loja,

        // Usuário interno (dona, equipe, superadmin): sai pela plataforma.
        TipoEventoNotificacao.ProdutoVencendo
            or TipoEventoNotificacao.ProdutoVencido
            or TipoEventoNotificacao.TarefaPendente
            or TipoEventoNotificacao.ResetSenha
            or TipoEventoNotificacao.AssinaturaExpirando
            or TipoEventoNotificacao.AssinaturaExpirada
            or TipoEventoNotificacao.BroadcastSuperAdmin
            or TipoEventoNotificacao.ConfirmacaoEmail
            or TipoEventoNotificacao.AlertaEstoqueCritico
            or TipoEventoNotificacao.TicketCriado
            or TipoEventoNotificacao.TicketRespondidoCliente
            or TipoEventoNotificacao.TicketRespondidoAdmin
            or TipoEventoNotificacao.TicketStatusAlterado
            or TipoEventoNotificacao.TicketAtribuido
            or TipoEventoNotificacao.TicketEncaminhadoNivel
            or TipoEventoNotificacao.SlaProximoVencer
            or TipoEventoNotificacao.SlaViolado
            or TipoEventoNotificacao.BugFixCriado
            or TipoEventoNotificacao.FaturaCriada
            or TipoEventoNotificacao.FaturaVencendo
            or TipoEventoNotificacao.FaturaPaga
            or TipoEventoNotificacao.FaturaVencida
            or TipoEventoNotificacao.PagamentoConfirmado
            or TipoEventoNotificacao.PagamentoFalhou
            or TipoEventoNotificacao.ConviteCsat
            or TipoEventoNotificacao.PedidoAgendadoHoje
            or TipoEventoNotificacao.PedidoAgendadoEm1Hora
            or TipoEventoNotificacao.PedidoAgendadoEm10Minutos
            or TipoEventoNotificacao.RelatorioPronto
            or TipoEventoNotificacao.RelatorioFalhou
            or TipoEventoNotificacao.RelatorioExpirado
            or TipoEventoNotificacao.ContaPagarVencendo
            or TipoEventoNotificacao.ContaPagarVencida
            or TipoEventoNotificacao.ContaReceberVencendo
            or TipoEventoNotificacao.ContaReceberVencida
            or TipoEventoNotificacao.ParcelaRecebida
            or TipoEventoNotificacao.CaixaAbertoEsquecido
            or TipoEventoNotificacao.ConversaEscalada
            or TipoEventoNotificacao.LembreteVencido
            or TipoEventoNotificacao.ConviteAcesso
            or TipoEventoNotificacao.IncidenteSistema
            or TipoEventoNotificacao.PrazoEstourado
            or TipoEventoNotificacao.ResumoDiario
            or TipoEventoNotificacao.ContatoAlterado
            or TipoEventoNotificacao.SenhaAlterada => OrigemRemetente.Plataforma,

        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo,
            "Tipo de evento sem remetente declarado: classifique-o em RemetentePorTipoEvento (Loja ou Plataforma).")
    };
}
