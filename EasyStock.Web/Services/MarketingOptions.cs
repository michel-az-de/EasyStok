namespace EasyStock.Web.Services;

/// <summary>
/// Configuracoes da landing publica e canais de aquisicao. Lidas da secao
/// "Marketing" no appsettings.json. Injetadas via IOptions&lt;MarketingOptions&gt;.
/// </summary>
public sealed class MarketingOptions
{
    /// <summary>Numero do WhatsApp da empresa, formato internacional sem +. Ex: 5511999999999.</summary>
    public string WhatsAppNumero { get; set; } = string.Empty;

    /// <summary>Texto pre-preenchido no link wa.me — facilita primeiro contato.</summary>
    public string WhatsAppMensagemPadrao { get; set; } = "Oi! Vim do site, queria saber mais sobre o EasyStok.";

    /// <summary>Dominio publico da landing (sem protocolo). Ex: easystok.com.br.</summary>
    public string DominioPublico { get; set; } = "easystok.com.br";

    /// <summary>Dominio do app autenticado (sem protocolo). Ex: app.easystok.com.br.</summary>
    public string DominioApp { get; set; } = "app.easystok.com.br";

    /// <summary>Email institucional para fale-conosco e cco de leads.</summary>
    public string EmailContato { get; set; } = "contato@easystok.com.br";

    /// <summary>Link do canal Instagram (footer).</summary>
    public string? Instagram { get; set; }

    /// <summary>Link do canal LinkedIn (footer).</summary>
    public string? LinkedIn { get; set; }

    /// <summary>Link absoluto pra abrir o WhatsApp com a mensagem padrao.</summary>
    public string LinkWhatsApp =>
        string.IsNullOrWhiteSpace(WhatsAppNumero)
            ? "#"
            : $"https://wa.me/{WhatsAppNumero}?text={Uri.EscapeDataString(WhatsAppMensagemPadrao)}";
}
