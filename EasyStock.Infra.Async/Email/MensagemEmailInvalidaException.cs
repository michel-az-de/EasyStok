namespace EasyStock.Infra.Async.Email;

/// <summary>
/// A mensagem nunca vai poder ser montada (destinatario ou anexo malformado). Antes de conectar, e permanente: nenhuma
/// tentativa futura conserta. Fica de proposito um tipo proprio: um <see cref="FormatException"/> ou
/// <see cref="ArgumentException"/> solto (do host, do cliente) nao e "destinatario invalido".
/// </summary>
internal sealed class MensagemEmailInvalidaException(string mensagem, Exception? interna = null)
    : Exception(mensagem, interna);
