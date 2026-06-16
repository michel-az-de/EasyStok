namespace EasyStock.Web.Models.ViewModels.Assinatura;

/// <summary>
/// Estado da landing de assinatura bloqueada (takeover full-screen). Cobre os motivos
/// emitidos pelo SubscriptionGate da Api (trial vencido, suspenso, cancelado, sem assinatura).
/// O texto muda por motivo; o caminho de conversão (contato/WhatsApp) é o mesmo.
/// </summary>
public class AssinaturaBloqueadaViewModel
{
    public string MotivoCode { get; set; } = "TRIAL_EXPIRED";
    public string ChipTexto { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Subtitulo { get; set; } = "";
    public string SeloTexto { get; set; } = "";
    public string CtaTexto { get; set; } = "";

    public DateTime? TrialFim { get; set; }
    public List<PlanoInfo> Planos { get; set; } = [];

    public string WhatsAppNumero { get; set; } = string.Empty;
    public string LinkWhatsApp { get; set; } = "#";
    public string EmailContato { get; set; } = string.Empty;

    /// <summary>Plano em destaque no card principal: o pago mais barato, senão o primeiro.</summary>
    public PlanoInfo? PlanoDestaque =>
        Planos.Where(p => p.PrecoMensal > 0).OrderBy(p => p.PrecoMensal).FirstOrDefault()
        ?? Planos.FirstOrDefault();

    /// <summary>
    /// Monta o VM a partir do sub-code do gate (já sem o prefixo ASSINATURA_BLOQUEADA:).
    /// Default seguro = TRIAL_EXPIRED (o caso mais comum e o do incidente #619).
    /// </summary>
    public static AssinaturaBloqueadaViewModel ParaMotivo(string? subCode)
    {
        var code = (subCode ?? "TRIAL_EXPIRED").Trim().ToUpperInvariant();
        return code switch
        {
            "SUBSCRIPTION_SUSPENDED" => new AssinaturaBloqueadaViewModel
            {
                MotivoCode = code,
                ChipTexto = "Pagamento pendente",
                Titulo = "Faltou só o pagamento pra destravar tudo.",
                Subtitulo = "Sua assinatura está pausada por um pagamento em aberto. Regularize em 1 minuto e tudo volta no mesmo lugar, sem perder nada.",
                SeloTexto = "Seus dados continuam salvos e seguros.",
                CtaTexto = "Regularizar pelo WhatsApp",
            },
            "SUBSCRIPTION_CANCELLED" or "SUBSCRIPTION_EXPIRED" => new AssinaturaBloqueadaViewModel
            {
                MotivoCode = code,
                ChipTexto = "Assinatura encerrada",
                Titulo = "Vamos reativar sua conta?",
                Subtitulo = "Sua assinatura foi encerrada, mas seus dados continuam aqui, intactos. Escolha um plano pra voltar a operar de onde parou.",
                SeloTexto = "Nada foi apagado. Sua operação está guardada.",
                CtaTexto = "Reativar pelo WhatsApp",
            },
            "NO_SUBSCRIPTION" => new AssinaturaBloqueadaViewModel
            {
                MotivoCode = code,
                ChipTexto = "Sem assinatura ativa",
                Titulo = "Vamos ativar sua conta?",
                Subtitulo = "Sua conta ainda não tem uma assinatura ativa. Escolha um plano pra liberar o catálogo, o estoque, os pedidos e o caixa.",
                SeloTexto = "Seus dados ficam guardados e seguros.",
                CtaTexto = "Assinar pelo WhatsApp",
            },
            _ => new AssinaturaBloqueadaViewModel
            {
                MotivoCode = "TRIAL_EXPIRED",
                ChipTexto = "Seu teste de 14 dias chegou ao fim",
                Titulo = "Sua operação já está montada. Bora manter no ar?",
                Subtitulo = "Nesses 14 dias o EasyStok rodou com os seus dados de verdade: catálogo, estoque, pedidos e caixa. Está tudo salvo. Escolha um plano e continue de onde parou.",
                SeloTexto = "Seus dados ficam guardados. Nada some por não assinar hoje.",
                CtaTexto = "Quero assinar pelo WhatsApp",
            },
        };
    }
}
