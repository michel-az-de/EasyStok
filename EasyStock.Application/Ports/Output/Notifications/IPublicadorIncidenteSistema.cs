using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

/// <summary>
/// Porta única dos avisos de problema no sistema (N10, ADR-0057). <b>Não existe parâmetro de texto livre</b>: componente,
/// estado e gravidade são enums fechados, então mensagem de exceção, query string e dado de cliente não têm por onde entrar
/// no aviso. Quem detecta o problema (monitor de endpoint, snapshot de saúde, coletor de 5xx e, depois, o vigia da F16)
/// só chama esta porta.
/// </summary>
public interface IPublicadorIncidenteSistema
{
    /// <summary>
    /// Enfileira o evento <c>IncidenteSistema</c> na empresa padrão. Não publica com o interruptor desligado nem sem empresa
    /// padrão resolvida (e, neste caso, loga aviso nomeando a chave de configuração). A dedupe é pela chave de idempotência
    /// do outbox: a mesma falha na mesma janela vira um aviso só.
    /// </summary>
    /// <param name="desdeUtc">Quando o incidente começou; a duração do aviso conta dele até agora.</param>
    Task PublicarAsync(
        ComponenteIncidente componente,
        EstadoIncidente estado,
        SeveridadeIncidente severidade,
        DateTime desdeUtc,
        CancellationToken ct = default);
}
