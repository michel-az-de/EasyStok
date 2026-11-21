namespace EasyStock.Application.Services.Notifications.Anexos;

/// <summary>Formato do arquivo anexado ao aviso (#2211).</summary>
public enum FormatoAnexo
{
    /// <summary>Imagem: sai no WhatsApp como imagem (metadado <c>imagem</c>).</summary>
    Png,

    /// <summary>Documento: gravado no metadado <c>documento</c>, para o canal que souber enviar arquivo.</summary>
    Pdf
}

/// <summary>Arquivo pronto para subir ao armazenamento.</summary>
public sealed record AnexoGerado(byte[] Conteudo, string ContentType, string Extensao);

/// <summary>
/// Um modelo de anexo (Strategy, #2211): gera o arquivo do aviso a partir das variáveis do evento. Layout novo é classe
/// nova registrada na DI; o template de texto o escolhe pelo metadado <c>anexo</c>, sem tocar no motor.
/// </summary>
public interface IModeloAnexoNotificacao
{
    /// <summary>Código citado no metadado <c>anexo</c> do template (ex.: <c>canhoto</c>).</summary>
    string Codigo { get; }

    /// <summary>O arquivo, ou <c>null</c> quando o evento não tem o que anexar (pedido sumiu, payload sem id).</summary>
    Task<AnexoGerado?> GerarAsync(
        Guid empresaId, IDictionary<string, object?> variaveis, FormatoAnexo formato, CancellationToken ct = default);
}

/// <summary>Resolve o anexo declarado nos metadados do template e devolve os metadados com a URL dele (#2211).</summary>
public interface IAnexadorMensagem
{
    /// <summary>
    /// Lê <c>anexo</c> e <c>anexoFormato</c> dos metadados já renderizados, gera o arquivo, sobe e troca as duas chaves
    /// por <c>imagem</c> (PNG) ou <c>documento</c> (PDF). Sem <c>anexo</c>, devolve os metadados como vieram. Falha
    /// na geração não derruba o aviso: o texto sai sem o anexo.
    /// </summary>
    Task<string?> AnexarAsync(
        Domain.Entities.Notifications.EventoNotificacao evento,
        string? metadadosJson,
        IDictionary<string, object?> variaveis,
        CancellationToken ct = default);
}
