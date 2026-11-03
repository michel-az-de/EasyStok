using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace EasyStock.Api.Hosting;

// Aplica a restrição no caminho físico resolvido, inclusive com barras duplicadas.
internal sealed class PublicUploadFileProvider(string root) : IFileProvider
{
    private readonly PhysicalFileProvider inner = new(root);
    public IFileInfo GetFileInfo(string subpath)
    {
        var file = inner.GetFileInfo(subpath);
        if (file.PhysicalPath is { } path)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var pasta = relative.Split('/')[0];
            // Só imagens/áudios gerados para envio usam Guid no formato D. Recebidos WPP/Meta e
            // anexos de e-mail ficam acessíveis pelo endpoint autenticado da conversa.
            if (pasta.Equals("acervo", StringComparison.OrdinalIgnoreCase) ||
                pasta.Equals("clientes", StringComparison.OrdinalIgnoreCase) ||
                pasta.Equals("atendimento", StringComparison.OrdinalIgnoreCase) &&
                !Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "D", out _))
                return new NotFoundFileInfo(subpath);
        }
        return file;
    }
    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
    public IChangeToken Watch(string filter) => inner.Watch(filter);
}
