using System.Globalization;
using EasyStock.Application.Ports.Output.Notifications;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2875: o motivo gravado em <c>Mensagem.Erro</c> quando um aviso da fila não chega ao cliente. Começa com
/// "Não enviada: " para o Console mostrá-lo como veio (<c>falhaDeEnvio.js</c>); o texto cru da Meta, em inglês, fica só
/// no log do outbox.
/// </summary>
public static class MotivoAvisoNaoEntregue
{
    private const string Prefixo = "Não enviada: ";
    private const string Generico = Prefixo + "o WhatsApp não aceitou o aviso.";

    /// <summary>
    /// Motivo da falha definitiva do envio, ou nulo quando a recusa é do próprio cliente (bloqueio, revogação): o pedido
    /// dele de não receber não vira falha a reenviar.
    /// </summary>
    /// <param name="codigoErro">Código do canal: 132xxx é o modelo recusado pela Meta; negativo é recusa nossa.</param>
    /// <param name="statusHttp">Com status, o texto veio da resposta da Meta; sem ele, foi escrito por nós antes da rede.</param>
    /// <param name="erro">Erro detalhado do provider.</param>
    public static string? DaFalha(int? codigoErro, int? statusHttp, string? erro)
    {
        var texto = erro?.Trim();
        if (RecusasEnvioWhatsApp.PorConsentimento(texto)) return null;

        return codigoErro switch
        {
            >= 132000 and <= 132999 => $"{Prefixo}a Meta recusou o modelo da mensagem (código Meta {codigoErro}).",
            > 0 => $"{Prefixo}o WhatsApp recusou o aviso (código Meta {codigoErro}).",
            not null when statusHttp is null && !string.IsNullOrEmpty(texto) => Prefixo + texto,
            _ when texto == RecusasEnvioWhatsApp.ForaDaJanelaSemModelo =>
                $"{Prefixo}fora da janela de 24 h e sem modelo aprovado.",
            _ => Generico,
        };
    }

    /// <summary>O aviso passou do prazo de validade do tipo sem sair.</summary>
    public static string Expirado(TimeSpan prazo)
    {
        var duracao = prazo.TotalMinutes >= 60 && prazo.TotalMinutes % 60 == 0
            ? $"{prazo.TotalHours.ToString("0", CultureInfo.InvariantCulture)} h"
            : $"{prazo.TotalMinutes.ToString("0", CultureInfo.InvariantCulture)} min";
        return $"{Prefixo}o aviso ficou {duracao} na fila sem sair e expirou.";
    }
}
