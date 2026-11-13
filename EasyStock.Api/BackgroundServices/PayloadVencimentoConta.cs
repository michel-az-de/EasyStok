using System.Globalization;
using System.Text.Json;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// #2074: payload do aviso de conta a pagar ou receber vencendo ou vencida. Leva os dados crus (que já existiam) e os
/// textos prontos em pt-BR que os templates do sino e do e-mail mostram, para o template não formatar dinheiro nem data.
/// </summary>
public static class PayloadVencimentoConta
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Montar(
        TipoEventoNotificacao tipo, Guid parcelaId, Guid contaId, int numero, decimal saldo, DateTime vencimento,
        string? descricao, string variante)
    {
        var aPagar = tipo is TipoEventoNotificacao.ContaPagarVencendo or TipoEventoNotificacao.ContaPagarVencida;
        var vencida = tipo is TipoEventoNotificacao.ContaPagarVencida or TipoEventoNotificacao.ContaReceberVencida;
        var vencimentoTexto = vencimento.ToString("dd/MM/yyyy", PtBr);

        var titulo = (aPagar, vencida) switch
        {
            (true, false) => "Conta a pagar vencendo",
            (true, true) => "Conta a pagar vencida",
            (false, false) => "Conta a receber vencendo",
            (false, true) => "Conta a receber vencida",
        };
        var situacao = (vencida, variante) switch
        {
            (true, _) => $"venceu em {vencimentoTexto}",
            (false, "d1") => "vence amanhã",
            _ => "vence em 3 dias",
        };

        return JsonSerializer.Serialize(new
        {
            parcelaId,
            contaId,
            numero,
            valor = saldo,
            vencimento,
            descricao,
            variante,
            descricao_texto = string.IsNullOrWhiteSpace(descricao) ? (aPagar ? "Conta a pagar" : "Conta a receber") : descricao,
            valor_texto = saldo.ToString("C", PtBr),
            vencimento_texto = vencimentoTexto,
            titulo_texto = titulo,
            situacao_texto = situacao,
        });
    }
}
