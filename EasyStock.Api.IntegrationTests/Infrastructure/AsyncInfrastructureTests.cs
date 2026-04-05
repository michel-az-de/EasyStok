using EasyStock.Application.Ports.Output;
using EasyStock.Infra.Async;
using FluentAssertions;
using Xunit;

namespace EasyStock.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Testes de integração para a infraestrutura assíncrona.
/// Testa cache, fila, email e storage em conjunto.
/// </summary>
public class AsyncInfrastructureTests : IClassFixture<AsyncInfrastructureFixture>
{
    private readonly AsyncInfrastructureFixture _fixture;

    public AsyncInfrastructureTests(AsyncInfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CacheService_DeveArmazenarERecuperarValor()
    {
        // Arrange
        var cache = _fixture.CacheService;
        var key = "test-key";
        var value = new TestData { Id = 1, Name = "Test" };

        // Act
        await cache.SetAsync(key, value, TimeSpan.FromMinutes(5));
        var retrieved = await cache.GetAsync<TestData>(key);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(1);
        retrieved.Name.Should().Be("Test");
    }

    [Fact]
    public async Task CacheService_DeveRetornarNull_QuandoChaveNaoExiste()
    {
        // Arrange
        var cache = _fixture.CacheService;
        var key = "non-existent-key";

        // Act
        var result = await cache.GetAsync<string>(key);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task QueueService_DeveEnfileirarEProcessarMensagem()
    {
        // Arrange
        var queue = _fixture.QueueService;
        var queueName = "test-queue";
        var message = new TestMessage { Content = "Hello World" };
        var processedMessages = new List<TestMessage>();

        // Act
        await queue.EnqueueAsync(queueName, message);

        // Processar a mensagem
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await queue.ProcessQueueAsync(queueName, async (TestMessage msg) =>
        {
            processedMessages.Add(msg);
        }, cts.Token);

        // Assert
        processedMessages.Should().ContainSingle();
        processedMessages[0].Content.Should().Be("Hello World");
    }

    [Fact]
    public async Task EmailService_DeveEnviarEmail_SemErro()
    {
        // Arrange
        var emailService = _fixture.EmailService;

        // Act & Assert - Não deve lançar exceção
        await emailService.SendAsync(
            "test@example.com",
            "Test Subject",
            "Test Body",
            isHtml: false);
    }

    [Fact]
    public async Task StorageService_DeveFazerUploadEDownload()
    {
        // Arrange
        var storage = _fixture.StorageService;
        var container = "test-container";
        var fileName = $"test-file-{Guid.NewGuid()}.txt";
        var content = "Hello Storage World!";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        // Act
        var uploadPath = await storage.UploadAsync(container, fileName, stream, "text/plain");
        var downloadStream = await storage.DownloadAsync(container, fileName);
        using var reader = new StreamReader(downloadStream);
        var downloadedContent = await reader.ReadToEndAsync();

        // Assert
        uploadPath.Should().NotBeNullOrEmpty();
        downloadedContent.Should().Be(content);

        // Cleanup
        await storage.DeleteAsync(container, fileName);
    }

    [Fact]
    public async Task StorageService_DeveVerificarExistenciaArquivo()
    {
        // Arrange
        var storage = _fixture.StorageService;
        var container = "test-container";
        var fileName = $"test-file-{Guid.NewGuid()}.txt";
        var content = "Test content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        // Act
        await storage.UploadAsync(container, fileName, stream, "text/plain");
        var exists = await storage.ExistsAsync(container, fileName);

        // Assert
        exists.Should().BeTrue();

        // Cleanup
        await storage.DeleteAsync(container, fileName);
    }
}

/// <summary>Fixture para compartilhar instâncias de infraestrutura entre testes.</summary>
public sealed class AsyncInfrastructureFixture : IDisposable
{
    public ICacheService CacheService { get; }
    public IQueueService QueueService { get; }
    public IEmailService EmailService { get; }
    public IStorageService StorageService { get; }

    public AsyncInfrastructureFixture()
    {
        // Em testes, usar implementações em memória/simuladas
        CacheService = new InMemoryCacheService();
        QueueService = new BackgroundQueueService();
        EmailService = new ConsoleEmailService();
        StorageService = new InMemoryStorageService();
    }

    public void Dispose()
    {
        if (QueueService is IDisposable disposableQueue)
            disposableQueue.Dispose();
    }
}

/// <summary>Implementação em memória do cache para testes.</summary>
public sealed class InMemoryCacheService : ICacheService
{
    private readonly Dictionary<string, (object Value, DateTime Expiry)> _cache = new();

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
    {
        var expiry = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : DateTime.MaxValue;
        _cache[key] = (value!, expiry);
        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (DateTime.UtcNow < entry.Expiry)
            {
                return Task.FromResult((T?)entry.Value);
            }
            _cache.Remove(key);
        }
        return Task.FromResult<T?>(default);
    }

    public Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key) =>
        Task.FromResult(_cache.ContainsKey(key) && DateTime.UtcNow < _cache[key].Expiry);

    public async Task<long> IncrementAsync(string key, long value = 1)
    {
        var current = await GetAsync<long>(key) ?? 0;
        var newValue = current + value;
        await SetAsync(key, newValue);
        return newValue;
    }

    public Task SetExpiryAsync(string key, TimeSpan ttl)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            _cache[key] = (entry.Value, DateTime.UtcNow.Add(ttl));
        }
        return Task.CompletedTask;
    }

    public Task RemoveAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
            _cache.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>Implementação em memória do storage para testes.</summary>
public sealed class InMemoryStorageService : IStorageService
{
    private readonly Dictionary<string, byte[]> _files = new();

    public Task<string> UploadAsync(string container, string fileName, Stream content, string contentType)
    {
        return UploadAsync(container, fileName, content, contentType, new Dictionary<string, string>());
    }

    public async Task<string> UploadAsync(string container, string fileName, Stream content, string contentType, Dictionary<string, string> metadata)
    {
        var key = $"{container}/{fileName}";
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        _files[key] = ms.ToArray();
        return key;
    }

    public Task<Stream> DownloadAsync(string container, string fileName)
    {
        var key = $"{container}/{fileName}";
        if (_files.TryGetValue(key, out var data))
        {
            return Task.FromResult<Stream>(new MemoryStream(data));
        }
        throw new FileNotFoundException();
    }

    public Task<string> GetPublicUrlAsync(string container, string fileName, TimeSpan? expiry = null)
    {
        var key = $"{container}/{fileName}";
        return Task.FromResult($"http://localhost/files/{key}");
    }

    public Task DeleteAsync(string container, string fileName)
    {
        var key = $"{container}/{fileName}";
        _files.Remove(key);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string container, string fileName)
    {
        var key = $"{container}/{fileName}";
        return Task.FromResult(_files.ContainsKey(key));
    }

    public Task<IEnumerable<string>> ListFilesAsync(string container, string prefix = null)
    {
        var files = _files.Keys
            .Where(k => k.StartsWith($"{container}/"))
            .Select(k => k.Substring(container.Length + 1))
            .Where(f => prefix == null || f.StartsWith(prefix));

        return Task.FromResult(files);
    }
}

/// <summary>Classes de teste.</summary>
public sealed record TestData(int Id, string Name);
public sealed record TestMessage(string Content);