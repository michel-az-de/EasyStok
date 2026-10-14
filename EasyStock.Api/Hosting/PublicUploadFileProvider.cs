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
            if (relative.Split('/')[0].Equals("acervo", StringComparison.OrdinalIgnoreCase))
                return new NotFoundFileInfo(subpath);
        }
        return file;
    }
    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
    public IChangeToken Watch(string filter) => inner.Watch(filter);
}
