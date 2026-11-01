using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.GerenciarUploads;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.UseCases.Cliente.Foto;

public sealed record FotoClienteResult(string? FotoUrl);

public sealed class ClienteFotoUseCase(
    IClienteRepository clientes, IFileStorage storage, IImageProcessor images, IUnitOfWork uow)
{
    public const int TamanhoMaximo = 6 * 1024 * 1024;

    public static string? Url(ClienteEntity cliente) => Url(cliente.EmpresaId, cliente.Id, cliente.FotoChave);

    public static string? Url(Guid empresaId, Guid clienteId, string? chave) =>
        ChaveDoCliente(empresaId, clienteId, chave)
            ? $"/api/clientes/{clienteId}/foto?v={Path.GetFileNameWithoutExtension(chave)}" : null;

    private static bool ChaveDoCliente(Guid empresaId, Guid clienteId, string? chave)
    {
        var prefixo = $"clientes/{empresaId}/{clienteId}/";
        return chave is not null && chave.StartsWith(prefixo, StringComparison.Ordinal)
            && chave.Length == prefixo.Length + 36 && chave.EndsWith(".jpg", StringComparison.Ordinal)
            && Guid.TryParseExact(chave[prefixo.Length..^4], "N", out _);
    }

    public async Task<FotoClienteResult?> EnviarAsync(Guid empresaId, Guid clienteId, Guid? usuarioId,
        string nomeArquivo, string contentType, byte[] conteudo, CancellationToken ct = default, string origem = "console")
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var cliente = await clientes.GetByIdAsync(empresaId, clienteId);
        if (cliente is null) return null;
        if (conteudo.Length is 0 or > TamanhoMaximo)
            throw new UseCaseValidationException("Escolha uma foto de até 6 MB.");
        if (contentType is not ("image/jpeg" or "image/png" or "image/webp"))
            throw new UseCaseValidationException("Escolha uma foto JPG, PNG ou WebP.");
        try
        {
            UploadSecurityValidator.SanitizeFileName(nomeArquivo);
            UploadSecurityValidator.EnsureContentMatchesDeclaredType(conteudo, contentType);
        }
        catch (InvalidOperationException ex) { throw new UseCaseValidationException(ex.Message); }

        var (foto, mime, _) = images.Optimize(conteudo, contentType, 512, 85, jpeg: true);
        // O processador retorna o original se não conseguir decodificar. Não publica esse fallback.
        if (ReferenceEquals(foto, conteudo) || mime != "image/jpeg" || foto.Length == 0 || foto.Length > TamanhoMaximo)
            throw new UseCaseValidationException("Não foi possível ler a foto. Escolha outra imagem.");
        var anterior = cliente.FotoChave;
        var salvo = await storage.UploadAsync(new FileUploadRequest(
            $"clientes/{empresaId}/{clienteId}", $"{Guid.NewGuid():N}.jpg", "image/jpeg", foto, IsPublic: false), ct);
        try
        {
            // GetByIdAsync devolve a entidade rastreada: somente foto/data mudam, sem sobrescrever cadastro concorrente.
            cliente.FotoChave = salvo.StorageKey;
            cliente.AlteradoEm = DateTime.UtcNow;
            await RegistrarAsync(cliente, usuarioId, anterior, salvo.StorageKey, origem);
            await uow.CommitAsync();
        }
        catch
        {
            await ApagarAsync(empresaId, clienteId, salvo.StorageKey);
            throw;
        }
        await ApagarAsync(empresaId, clienteId, anterior);
        return new(Url(cliente));
    }

    public async Task<byte[]?> ObterAsync(Guid empresaId, Guid clienteId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var cliente = await clientes.GetByIdAsync(empresaId, clienteId);
        if (cliente is null || !ChaveDoCliente(empresaId, clienteId, cliente.FotoChave)) return null;
        if (!await storage.ExistsAsync(cliente.FotoChave!, ct)) return null;
        return await storage.DownloadAsync(cliente.FotoChave!, ct);
    }

    public async Task<FotoClienteResult?> RemoverAsync(Guid empresaId, Guid clienteId, Guid? usuarioId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var cliente = await clientes.GetByIdAsync(empresaId, clienteId);
        if (cliente is null) return null;
        var anterior = cliente.FotoChave;
        if (anterior is null) return new(null);
        cliente.FotoChave = null;
        cliente.AlteradoEm = DateTime.UtcNow;
        await RegistrarAsync(cliente, usuarioId, anterior, null);
        await uow.CommitAsync();
        await ApagarAsync(empresaId, clienteId, anterior);
        return new(null);
    }

    private Task RegistrarAsync(ClienteEntity cliente, Guid? usuarioId, string? anterior, string? atual, string origem = "console") =>
        clientes.AddAlteracaoAsync(new ClienteAlteracao
        {
            Id = Guid.NewGuid(), EmpresaId = cliente.EmpresaId, ClienteId = cliente.Id,
            AlteradoPorUserId = usuarioId, Campo = "Foto", ValorAntigo = anterior, ValorNovo = atual,
            AlteradoEm = cliente.AlteradoEm, Origem = origem
        });

    private async Task ApagarAsync(Guid empresaId, Guid clienteId, string? chave)
    {
        if (!ChaveDoCliente(empresaId, clienteId, chave)) return;
        try { await storage.DeleteAsync(chave!, CancellationToken.None); }
        catch { /* Arquivo privado órfão pode ser limpo depois; não desfaz o cadastro confirmado. */ }
    }
}
