namespace EasyStock.Domain.Entities.Notifications;

/// <summary>
/// Onda 2.2 — assinatura de Web Push (PWA). Browser pede permission e cria
/// uma PushSubscription com Endpoint (URL do push service), P256dh (chave publica
/// ECDH) e Auth (chave secreta de autenticacao). Backend usa esses 3 para enviar
/// mensagens via Web Push protocol (RFC 8030) assinadas com VAPID.
///
/// <para>
/// Multi-tenant: <see cref="EmpresaId"/> e <see cref="UsuarioId"/> opcionais.
/// Quando ambos null = subscription anonima (raro, mas valido). Quando
/// UsuarioId setado, msg pode ser dirigida ao usuario especifico mesmo em
/// outro dispositivo (cada device = uma subscription).
/// </para>
/// </summary>
public class WebPushSubscription
{
    public Guid Id { get; set; }
    public Guid? EmpresaId { get; set; }
    public Guid? UsuarioId { get; set; }

    /// <summary>URL do push service do browser (Mozilla/Google/Apple). Imutavel apos criacao.</summary>
    public string Endpoint { get; set; } = null!;

    /// <summary>Chave publica ECDH p256dh em base64url. Usada para criptografar o payload.</summary>
    public string P256dh { get; set; } = null!;

    /// <summary>Secret auth em base64url. Adicionado ao HKDF para derivar chaves.</summary>
    public string Auth { get; set; } = null!;

    public string? UserAgent { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime UltimoUso { get; set; }

    /// <summary>Subscription pode ser desativada quando o push service retorna 410 Gone (subscription expirou).</summary>
    public bool Ativo { get; set; } = true;

    public Empresa? Empresa { get; set; }
    public Usuario? Usuario { get; set; }

    public static WebPushSubscription Criar(string endpoint, string p256dh, string auth, Guid? empresaId = null, Guid? usuarioId = null, string? userAgent = null)
    {
        var agora = DateTime.UtcNow;
        return new WebPushSubscription
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            UsuarioId = usuarioId,
            Endpoint = endpoint,
            P256dh = p256dh,
            Auth = auth,
            UserAgent = userAgent,
            CriadoEm = agora,
            UltimoUso = agora,
            Ativo = true
        };
    }

    public void MarcarUso() => UltimoUso = DateTime.UtcNow;
    public void Desativar() { Ativo = false; }
}
