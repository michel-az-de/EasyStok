using EasyStock.Application.Ports.Output.Observability;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>
/// #3117: webhook aceito (200) que não grava nada deixa o motivo em log e em contador. Em 21/11/2026 três POST da
/// ponte terminaram em 200 sem linha nova e sem log, e não deu para saber por quê.
/// <para>#2590: só motivo, nome do evento e origem (sessão da ponte ou número da loja). Telefone, texto e
/// identificador da mensagem do cliente nunca entram aqui.</para>
/// </summary>
public static class WebhookSemEfeito
{
    public const string EventoIgnorado = "evento_ignorado";
    public const string Grupo = "grupo";
    public const string AnteriorACaptura = "anterior_a_captura";
    public const string AckDesconhecido = "ack_desconhecido";
    public const string Duplicata = "duplicata";
    public const string Eco = "eco";
    public const string ModuloDesligado = "modulo_desligado";
    public const string ErroDominio = "erro_dominio";

    public static void Registrar(ILogger logger, IOperationalMetrics? metricas, string motivo, string? evento, string? origem)
    {
        metricas?.IncrementWebhookSemEfeito(motivo);
        logger.LogInformation("Webhook sem efeito: motivo {Motivo}, evento {Evento}, origem {Origem}.",
            motivo, Nome(evento, comecaComLetra: true), Nome(origem, comecaComLetra: false));
    }

    // O nome do evento vem do payload: só passa o que tem cara de identificador; telefone e endereço de contato não
    // passam. A origem é a sessão já validada da ponte ou o número da própria loja na Meta, nunca dado do cliente.
    private static string Nome(string? valor, bool comecaComLetra) =>
        valor is null ? "-"
        : valor.Length is > 0 and <= 64 && (!comecaComLetra || char.IsAsciiLetter(valor[0]))
            && valor.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? valor
        : "invalido";
}
