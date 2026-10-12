namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Limites e interruptor dos quatro prazos que chegam à dona e à equipe (N11), seção <c>Notifications:Prazos</c>.
/// Os valores padrão são os que o código usava como constante. Pedido atrasado e caixa esquecido não têm limite
/// numérico: usam o início previsto e o dia anterior.
/// </summary>
public sealed class PrazosOptions
{
    public const string Section = "Notifications:Prazos";

    /// <summary>Falso pula o enfileiramento do <c>PrazoEstourado</c> nos quatro adaptadores, sem deploy. SSE e Push não mudam.</summary>
    public bool Habilitado { get; set; } = true;

    /// <summary>Minutos sem resposta ao cliente até virar lembrete e aviso.</summary>
    public int ClienteSemRespostaMin { get; set; } = 10;

    /// <summary>Minutos de impressão pendente até o aviso.</summary>
    public int ImpressaoPendenteMin { get; set; } = 3;

    public TimeSpan ClienteSemResposta => TimeSpan.FromMinutes(ClienteSemRespostaMin > 0 ? ClienteSemRespostaMin : 10);

    public TimeSpan ImpressaoPendente => TimeSpan.FromMinutes(ImpressaoPendenteMin > 0 ? ImpressaoPendenteMin : 3);
}
