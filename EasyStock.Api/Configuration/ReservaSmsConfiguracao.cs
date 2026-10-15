using EasyStock.Application.Services.Atendimento;

namespace EasyStock.Api.Configuration;

/// <summary>
/// S60 (#1391): a reserva por SMS só liga com <c>Atendimento:ReenvioMensagens:SmsReserva=true</c> E um provedor real em
/// <c>Notifications:Sms:Provider</c>. O stub responde "enviado" sem enviar, então com ele a reserva fica desligada.
/// </summary>
public static class ReservaSmsConfiguracao
{
    public const string ChaveLigada = "Atendimento:ReenvioMensagens:SmsReserva";
    public const string ChaveProvedor = "Notifications:Sms:Provider";

    public static ReservaSmsOpcoes Ler(IConfiguration configuration)
    {
        var provedor = configuration[ChaveProvedor];
        var provedorReal = !string.IsNullOrWhiteSpace(provedor) && !provedor.Equals("stub", StringComparison.OrdinalIgnoreCase);
        return new ReservaSmsOpcoes(configuration.GetValue(ChaveLigada, defaultValue: false) && provedorReal);
    }
}
