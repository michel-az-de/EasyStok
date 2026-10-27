using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>avisar_gestao(tipo, resumo)</c> (#2096): o agente percebeu reclamação ou algo suspeito e avisa a dona e o gerente
/// por Push, e-mail e sino. Não encerra o turno nem tira a conversa do automático: quando precisa de pessoa, o agente
/// usa também <c>escalar_para_dona</c>. Um aviso por conversa, tipo e dia. O commit é do turno do agente.
/// </summary>
public sealed class AvisarGestaoFerramenta(
    INotificadorService notificador,
    IEventoNotificacaoRepository eventos,
    IOptions<PrazosOptions> prazos) : IFerramentaAgente
{
    public const int ResumoTamanhoMaximo = 300;

    // O mesmo aviso duas vezes no turno ainda não está no banco: a trava do turno evita a chave repetida no commit.
    private readonly HashSet<string> avisadosNoTurno = [];

    public string Nome => "avisar_gestao";

    public string Descricao =>
        "Avisa a dona e o gerente, sem o cliente saber, quando o cliente reclama (atraso, produto errado ou ruim, " +
        "atendimento) ou quando algo parece suspeito (golpe, comprovante estranho, pedido fora do padrão, ameaça). " +
        "Continue atendendo normalmente; se o caso precisa de pessoa, use também escalar_para_dona. Nunca conte ao cliente que avisou.";

    public string SchemaJson =>
        $$$"""{"type":"object","properties":{"tipo":{"type":"string","enum":["reclamacao","suspeita"]},"resumo":{"type":"string","maxLength":{{{ResumoTamanhoMaximo}}},"description":"O que aconteceu, em uma ou duas frases, sem telefone nem dado de cartão"}},"required":["tipo","resumo"],"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        var codigo = FerramentaJson.LerTexto(entrada, "tipo") switch
        {
            "reclamacao" => CodigosAviso.ReclamacaoCliente,
            "suspeita" => CodigosAviso.SuspeitaAtendimento,
            _ => null,
        };
        if (codigo is null) return FerramentaJson.Serializar(new { erro = "tipo_invalido" });

        var resumo = SemDigitosLongos(FerramentaJson.LerTexto(entrada, "resumo"));
        if (string.IsNullOrWhiteSpace(resumo)) return FerramentaJson.Serializar(new { erro = "resumo_vazio" });

        var conversa = contexto.Conversa;
        var sufixo = HorarioBrasil.DataOperacional(contexto.Agora).ToString("yyMMdd", CultureInfo.InvariantCulture);
        var correlacao = AlertaOperacaoEvento.CorrelationId(codigo, conversa.Id, sufixo);
        if (!avisadosNoTurno.Add(correlacao)) return FerramentaJson.Serializar(new { situacao = "ja_avisado" });

        var nome = NomesDeContato.PrimeiroNome(conversa.ContatoNome, AvisoEquipeNoAparelho.NomeSemContato);
        var reclamacao = codigo == CodigosAviso.ReclamacaoCliente;
        var enfileirado = await AlertaOperacaoEvento.EnfileirarAsync(notificador, eventos, prazos.Value,
            codigo, contexto.EmpresaId, conversa.Id,
            titulo: reclamacao ? "Cliente reclamando" : "Algo suspeito no atendimento",
            resumoCurto: reclamacao ? $"{nome} está reclamando no Balcão" : $"Confira a conversa de {nome} no Balcão",
            texto: $"{(reclamacao ? "Reclamação" : "Suspeita")} na conversa de {nome}: {resumo}",
            ct, sufixo, prazos.Value.LinkBalcao());

        return FerramentaJson.Serializar(new { situacao = enfileirado ? "avisado" : "ja_avisado" });
    }

    /// <summary>Telefone, cartão e documento não vão no aviso: sequência de 6 ou mais dígitos vira reticências.</summary>
    public static string SemDigitosLongos(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        var limpo = System.Text.RegularExpressions.Regex.Replace(texto.Trim(), @"\d(?:[\s.\-]?\d){5,}", "…");
        return limpo.Length <= ResumoTamanhoMaximo ? limpo : limpo[..ResumoTamanhoMaximo];
    }
}
