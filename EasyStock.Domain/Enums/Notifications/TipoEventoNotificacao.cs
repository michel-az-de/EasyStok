namespace EasyStock.Domain.Enums.Notifications;

public enum TipoEventoNotificacao
{
    ProdutoVencendo = 1,
    ProdutoVencido = 2,
    TarefaPendente = 3,
    ResetSenha = 4,
    AssinaturaExpirando = 5,
    AssinaturaExpirada = 6,
    BroadcastSuperAdmin = 7,
    ConfirmacaoEmail = 8,
    AlertaEstoqueCritico = 9
}
