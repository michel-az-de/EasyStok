namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Não há credencial do Mercado Pago (I3.1, #1667). É <see cref="HttpRequestException"/> para quem já
/// trata falha do provedor continuar tratando, mas diz o motivo: credencial ausente nunca equivale a
/// "sem pagamento" nem a "provedor fora do ar".
/// </summary>
public sealed class MercadoPagoNaoConfiguradoException()
    : HttpRequestException(MensagemPadrao)
{
    public const string MensagemPadrao =
        "O pagamento online pelo Mercado Pago ainda não está configurado. Use outra forma de pagamento ou fale com o suporte.";
}