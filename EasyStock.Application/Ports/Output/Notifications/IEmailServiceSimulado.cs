namespace EasyStock.Application.Ports.Output.Notifications;

/// <summary>
/// Marca um <see cref="IEmailService"/> que não envia nada: o <c>ConsoleEmailService</c> do desenvolvimento, que o
/// Worker usa quando não há <c>Smtp__*</c> (N2). O canal de e-mail do outbox reconhece o simulador por esta
/// interface e devolve <see cref="DesfechoEnvio.Simulado"/> com o provider real, em vez de gravar <c>smtp</c> num
/// e-mail que nunca saiu.
/// </summary>
/// <remarks>
/// A marca é a interface, nunca o nome da classe: o nome <c>ConsoleEmailService</c> é contrato do diagnóstico
/// (<c>DiagnosticoController</c> e <c>DiagnosticoEmailReportJob</c> decidem "SMTP configurado?" por ele) e não pode
/// mudar.
/// </remarks>
public interface IEmailServiceSimulado
{
    /// <summary>Provider que o outbox e o log de envio gravam no lugar de <c>smtp</c> (ex.: <c>console</c>).</summary>
    string Provider { get; }
}
