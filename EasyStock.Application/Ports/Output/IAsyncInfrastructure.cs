namespace EasyStock.Application.Ports.Output;

/// <summary>
/// Servi�o de cache distribu�do para armazenamento tempor�rio de dados.
/// Suporte a serializa��o JSON autom�tica e TTL configur�vel.
/// </summary>
public interface ICacheService
{
    /// <summary>Armazena um valor no cache com TTL opcional.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null);

    /// <summary>Recupera um valor do cache.</summary>
    Task<T?> GetAsync<T>(string key);

    /// <summary>Remove um valor do cache.</summary>
    Task RemoveAsync(string key);

    /// <summary>Verifica se uma chave existe no cache.</summary>
    Task<bool> ExistsAsync(string key);

    /// <summary>Incrementa um valor num�rico no cache.</summary>
    Task<long> IncrementAsync(string key, long value = 1);

    /// <summary>Define TTL para uma chave existente.</summary>
    Task SetExpiryAsync(string key, TimeSpan ttl);

    /// <summary>Remove m�ltiplas chaves do cache.</summary>
    Task RemoveAsync(IEnumerable<string> keys);
}

/// <summary>
/// Servi�o de fila para processamento ass�ncrono de mensagens/jobs.
/// Suporte a enfileiramento e processamento em background.
/// </summary>
public interface IQueueService
{
    /// <summary>Enfileira uma mensagem para processamento ass�ncrono.</summary>
    Task EnqueueAsync<T>(string queueName, T message);

    /// <summary>Processa mensagens de uma fila espec�fica.</summary>
    Task ProcessQueueAsync<T>(string queueName, Func<T, Task> processor, CancellationToken cancellationToken);

    /// <summary>Obt�m o tamanho da fila.</summary>
    Task<int> GetQueueLengthAsync(string queueName);

    /// <summary>Limpa uma fila espec�fica.</summary>
    Task ClearQueueAsync(string queueName);
}

/// <summary>
/// Servi�o para envio de emails transacionais.
/// Suporte a templates e anexos.
/// </summary>
public interface IEmailService
{
    /// <summary>Envia um email simples.</summary>
    Task SendAsync(string to, string subject, string body, bool isHtml = false);

    /// <summary>Envia um email com anexos.</summary>
    Task SendAsync(string to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml = false);

    /// <summary>Envia email para m�ltiplos destinat�rios.</summary>
    Task SendAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false);

    /// <summary>Envia email usando template.</summary>
    Task SendTemplateAsync(string to, string subject, string templateName, object model, bool isHtml = true);
}

/// <summary>Anexo de email.</summary>
public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType);

/// <summary>
/// Servi�o de storage para upload/download de arquivos.
/// Suporte a S3, R2, Azure Blob, etc.
/// </summary>
public interface IStorageService
{
    /// <summary>Faz upload de um arquivo.</summary>
    Task<string> UploadAsync(string container, string fileName, Stream content, string contentType);

    /// <summary>Faz upload de um arquivo com metadados.</summary>
    Task<string> UploadAsync(string container, string fileName, Stream content, string contentType, Dictionary<string, string> metadata);

    /// <summary>Faz download de um arquivo.</summary>
    Task<Stream> DownloadAsync(string container, string fileName);

    /// <summary>Obt�m URL p�blica de um arquivo.</summary>
    Task<string> GetPublicUrlAsync(string container, string fileName, TimeSpan? expiry = null);

    /// <summary>Remove um arquivo.</summary>
    Task DeleteAsync(string container, string fileName);

    /// <summary>Verifica se um arquivo existe.</summary>
    Task<bool> ExistsAsync(string container, string fileName);

    /// <summary>Lista arquivos em um container.</summary>
    Task<IEnumerable<string>> ListFilesAsync(string container, string prefix = "");
}
