using System.Security.Cryptography;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Domain.Entities.Acervo;

namespace EasyStock.Application.UseCases.Acervo;

public sealed record DadosImagem(string Titulo, string Origem, string Formato, Guid[] Itens,
    string Prompt = "", string Referencias = "", Guid? OriginalId = null, Guid? SolicitacaoId = null,
    string? CanvaUrl = null, string? CanvaAssetId = null, string Natureza = "Produto", int Largura = 0, int Altura = 0);
public sealed record PublicarImagem(Guid OperacaoId, Guid ItemId, bool Galeria, string? CapaEsperada);
public sealed record SolicitarImagem(Guid OperacaoId, Guid ItemId, string Formato, string Brief, Guid[] Referencias);

public sealed partial class AcervoUseCase(IAcervoRepository repo, IFileStorage storage, IImageProcessor processor, IUnitOfWork uow)
{
    public const int LimiteArquivo = 6 * 1024 * 1024;
    public Task<IReadOnlyList<AcervoImagem>> ListarAsync(Guid empresa, int pagina, CancellationToken ct) =>
        repo.ListarAsync(Empresa(empresa), Math.Clamp(pagina, 0, 10000), ct);
    public async Task<AcervoImagem> ObterAsync(Guid empresa, Guid id, CancellationToken ct) =>
        await repo.ObterAsync(Empresa(empresa), id, ct) ?? throw new KeyNotFoundException("Imagem não encontrada.");
    public Task<IReadOnlyList<AcervoPublicacao>> HistoricoAsync(Guid empresa, Guid id, CancellationToken ct) => repo.HistoricoAsync(Empresa(empresa), id, ct);
    public Task<IReadOnlyList<AcervoSolicitacao>> SolicitacoesAsync(Guid empresa, CancellationToken ct) => repo.SolicitacoesAsync(Empresa(empresa), ct);

    public async Task<(AcervoImagem Imagem, byte[] Bytes)> ArquivoAsync(Guid empresa, Guid id, CancellationToken ct)
    {
        var imagem = await ObterAsync(empresa, id, ct);
        return (imagem, await storage.DownloadAsync(imagem.StorageKey, ct));
    }

    public async Task<AcervoImagem> AdicionarAsync(Guid empresa, Guid autor, DadosImagem dados, string contentType, byte[] bytes, CancellationToken ct)
    {
        Empresa(empresa);
        if (autor == Guid.Empty) throw new UseCaseValidationException("Usuário não identificado.");
        if (bytes.Length == 0 || bytes.Length > LimiteArquivo) throw new UseCaseValidationException("Envie uma imagem de até 6 MB.");
        var extensao = contentType switch { "image/png" => ".png", "image/jpeg" => ".jpg", "image/webp" => ".webp", _ => throw new UseCaseValidationException("Use PNG, JPEG ou WebP.") };
        try { UploadSecurityValidator.EnsureContentMatchesDeclaredType(bytes, contentType); }
        catch (InvalidOperationException e) { throw new UseCaseValidationException(e.Message); }
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        StoredFileResult? arquivo = null;
        string? chaveTentativa = null;
        try
        {
            return await uow.ExecuteInTransactionSemRetryAsync(async token =>
            {
                if (dados.SolicitacaoId is { } solicitacaoId)
                    await repo.BloquearSolicitacaoAsync(empresa, solicitacaoId, token);
                await ValidarDadosAsync(empresa, dados, token);
                var existente = await repo.PorHashAsync(empresa, hash, token);
                if (existente is not null) return ConferirRepeticao(existente, dados);
                var pedido = dados.SolicitacaoId is { } pedidoId ? await repo.SolicitacaoAsync(empresa, pedidoId, token) : null;
                if (pedido?.ConcluidoEm is not null)
                    throw new UseCaseValidationException("Esta solicitação já foi concluída. Abra outra para gerar uma nova versão.");
                var imagem = new AcervoImagem { EmpresaId = empresa, CriadoPorId = autor, Titulo = dados.Titulo.Trim(),
                    Origem = dados.Origem, Formato = dados.Formato, Natureza = dados.Natureza, Largura = dados.Largura, Altura = dados.Altura,
                    Sha256 = hash, ContentType = contentType, Tamanho = bytes.Length,
                    Prompt = dados.Prompt, Referencias = dados.Referencias, ItensJson = JsonSerializer.Serialize(dados.Itens.Distinct()),
                    OriginalId = dados.OriginalId, SolicitacaoId = dados.SolicitacaoId, CanvaUrl = ValidarCanva(dados.CanvaUrl), CanvaAssetId = dados.CanvaAssetId };
                var pasta = $"acervo/{empresa}/{imagem.Id}";
                var nome = "original" + extensao;
                chaveTentativa = $"{pasta}/{nome}";
                arquivo = await storage.UploadAsync(new FileUploadRequest(pasta, nome, contentType, bytes, IsPublic: false), token);
                imagem.StorageKey = arquivo.StorageKey;
                repo.Adicionar(imagem);
                if (pedido is not null) pedido.ConcluidoEm = DateTime.UtcNow;
                await uow.CommitAsync();
                return imagem;
            }, ct);
        }
        catch (Exception ex)
        {
            var chave = arquivo?.StorageKey ?? chaveTentativa;
            if (chave is null) throw;
            uow.DescartarAlteracoesPendentes();
            AcervoImagem? persistida = null;
            try
            {
                // Após rollback, só remover a chave que comprovadamente não venceu.
                // A confirmação do commit pode ter se perdido após a gravação real.
                persistida = await repo.PorHashAsync(empresa, hash, CancellationToken.None);
                if (persistida?.StorageKey != chave)
                    await storage.DeleteAsync(chave, CancellationToken.None);
            }
            catch { /* Na dúvida sobre banco ou storage, preservar o arquivo e o erro original. */ }
            if (uow.EhViolacaoDeUnicidade(ex) && persistida is not null)
                return ConferirRepeticao(persistida, dados);
            throw;
        }
    }

    public async Task<AcervoImagem> VincularCanvaAsync(Guid empresa, Guid id, string url, string? assetId, CancellationToken ct)
    {
        ValidarTextos(url, assetId);
        var imagem = await ObterAsync(empresa, id, ct);
        imagem.CanvaUrl = ValidarCanva(url) ?? throw new UseCaseValidationException("Informe o link do design.");
        if (assetId?.Length > 100) throw new UseCaseValidationException("Identificador do Canva inválido.");
        imagem.CanvaAssetId = assetId;
        await uow.CommitAsync();
        return imagem;
    }

    public async Task<AcervoImagem> AprovarAsync(Guid empresa, Guid autor, Guid id, CancellationToken ct)
    {
        var imagem = await ObterAsync(empresa, id, ct);
        imagem.AprovadoEm ??= DateTime.UtcNow;
        imagem.AprovadoPorId ??= autor;
        await uow.CommitAsync();
        return imagem;
    }

    public Task<AcervoPublicacao> PublicarAsync(Guid empresa, Guid autor, Guid id, PublicarImagem dados, CancellationToken ct)
    {
        Empresa(empresa);
        if (dados.OperacaoId == Guid.Empty) throw new UseCaseValidationException("Operação não identificada.");
        return uow.ExecuteInTransactionSemRetryAsync(async token =>
        {
            await repo.BloquearItemAsync(empresa, dados.ItemId, token);
            var anterior = await repo.PublicacaoAsync(empresa, dados.OperacaoId, token);
            if (anterior is not null)
            {
                if (anterior.ImagemId != id || anterior.ItemId != dados.ItemId || anterior.Galeria != dados.Galeria)
                    throw new UseCaseValidationException("Esta operação já foi usada para outra publicação.");
                return anterior;
            }
            var imagem = await ObterAsync(empresa, id, token);
            if (imagem.AprovadoEm is null) throw new UseCaseValidationException("Aprove esta versão antes de publicar.");
            if (!JsonSerializer.Deserialize<Guid[]>(imagem.ItensJson)!.Contains(dados.ItemId))
                throw new UseCaseValidationException("Esta imagem não está vinculada ao item escolhido.");
            var item = await repo.ItemAsync(empresa, dados.ItemId, token) ?? throw new KeyNotFoundException("Item não encontrado.");
            if (item.FotoUrl != dados.CapaEsperada) throw new UseCaseValidationException("A capa mudou. Atualize a prévia antes de publicar.");
            if (dados.Galeria && item.ObterFotosGaleria().Count >= 5) throw new UseCaseValidationException("A galeria já tem cinco imagens.");
            if (!dados.Galeria && item.GaleriaSemVagaParaNovaCapa) throw new UseCaseValidationException("A galeria está cheia. Confira as fotos atuais.");
            var bytes = await storage.DownloadAsync(imagem.StorageKey, token);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != imagem.Sha256) throw new UseCaseValidationException("O original não passou na verificação de integridade.");
            var (conteudo, tipo, ext) = processor.Optimize(bytes, imagem.ContentType);
            if (ReferenceEquals(conteudo, bytes)) throw new UseCaseValidationException("Não foi possível preparar a imagem para publicação.");
            var arquivo = await storage.UploadAsync(new FileUploadRequest($"cardapios/{empresa}/{item.StorefrontId}/{item.Id}", $"acervo-{dados.OperacaoId}{ext}", tipo, conteudo), token);
            var publicacao = new AcervoPublicacao { Id = dados.OperacaoId, EmpresaId = empresa, ImagemId = id, ItemId = item.Id,
                ProdutoId = item.ProdutoId, UsuarioId = autor, Galeria = dados.Galeria, Url = arquivo.Url, CapaAnterior = item.FotoUrl };
            if (dados.Galeria) item.AdicionarFotoGaleria(arquivo.Url, preservarCapa: true);
            else item.SubstituirCapaGaleria(arquivo.Url);
            // Originais e arquivos anteriores permanecem para recuperação e rastreabilidade.
            repo.Adicionar(publicacao);
            await uow.CommitAsync();
            return publicacao;
        }, ct);
    }

    public async Task<AcervoSolicitacao> SolicitarAsync(Guid empresa, Guid autor, SolicitarImagem dados, CancellationToken ct)
    {
        Empresa(empresa);
        ValidarTextos(dados.Brief);
        if (dados.OperacaoId == Guid.Empty) throw new UseCaseValidationException("Operação não identificada.");
        if (await repo.ItemAsync(empresa, dados.ItemId, ct) is null) throw new UseCaseValidationException("Item não encontrado.");
        Formato(dados.Formato);
        if (string.IsNullOrWhiteSpace(dados.Brief) || dados.Brief.Length > 12000) throw new UseCaseValidationException("Descreva a imagem em até 12.000 caracteres.");
        if (dados.Referencias is null || dados.Referencias.Length > 10) throw new UseCaseValidationException("Use até dez referências do acervo.");
        foreach (var referencia in dados.Referencias) await ObterAsync(empresa, referencia, ct);
        var existente = await repo.SolicitacaoAsync(empresa, dados.OperacaoId, ct);
        if (existente is not null) return ConferirSolicitacao(existente, dados);
        var pedido = new AcervoSolicitacao { Id = dados.OperacaoId, EmpresaId = empresa, UsuarioId = autor, ItemId = dados.ItemId, Formato = dados.Formato,
            Brief = dados.Brief.Trim(), ReferenciasJson = JsonSerializer.Serialize(dados.Referencias.Distinct()) };
        repo.Adicionar(pedido);
        try { await uow.CommitAsync(); }
        catch (Exception ex) when (uow.EhViolacaoDeUnicidade(ex))
        {
            uow.DescartarAlteracoesPendentes();
            var repetido = await repo.SolicitacaoAsync(empresa, dados.OperacaoId, ct) ?? throw new UseCaseValidationException("Operação indisponível. Abra uma nova solicitação.");
            return ConferirSolicitacao(repetido, dados);
        }
        return pedido;
    }

    private async Task ValidarDadosAsync(Guid empresa, DadosImagem d, CancellationToken ct)
    {
        ValidarTextos(d.Titulo, d.Prompt, d.Referencias, d.CanvaUrl, d.CanvaAssetId);
        if (string.IsNullOrWhiteSpace(d.Titulo) || d.Titulo.Length > 160) throw new UseCaseValidationException("Informe um título de até 160 caracteres.");
        if (d.Origem is not ("IA" or "Fotografia" or "Canva")) throw new UseCaseValidationException("Origem inválida.");
        if (d.Natureza is not ("Produto" or "Comunicacao")) throw new UseCaseValidationException("Natureza inválida.");
        Formato(d.Formato);
        if (d.Prompt is null || d.Referencias is null || d.Prompt.Length > 12000 || d.Referencias.Length > 6000) throw new UseCaseValidationException("Prompt ou referências muito longos.");
        if (d.Origem == "IA" && string.IsNullOrWhiteSpace(d.Prompt)) throw new UseCaseValidationException("Registre o prompt da geração.");
        if (d.Itens is null || d.Itens.Length > 50) throw new UseCaseValidationException("Vincule até cinquenta itens.");
        foreach (var item in d.Itens)
            if (await repo.ItemAsync(empresa, item, ct) is null) throw new UseCaseValidationException("Item do cardápio não encontrado nesta empresa.");
        if (d.OriginalId is { } original) await ObterAsync(empresa, original, ct);
        if (d.Origem == "Canva" && (d.OriginalId is null || ValidarCanva(d.CanvaUrl) is null)) throw new UseCaseValidationException("A exportação precisa do original e do link do design Canva.");
        if (d.SolicitacaoId is { } pedido)
        {
            var solicitacao = await repo.SolicitacaoAsync(empresa, pedido, ct) ?? throw new UseCaseValidationException("Solicitação não encontrada.");
            if (!d.Itens.Contains(solicitacao.ItemId) || d.Formato != solicitacao.Formato)
                throw new UseCaseValidationException("A imagem precisa atender ao item e ao formato da solicitação.");
            if (solicitacao.BriefingId is not null)
            {
                if (d.Origem != "IA" || d.Prompt != solicitacao.Brief || !ProporcaoConfere(d.Formato, d.Largura, d.Altura))
                    throw new UseCaseValidationException("O resultado deve preservar o prompt registrado e o formato real do briefing.");
                Guid[] referencias;
                try { referencias = JsonSerializer.Deserialize<Guid[]>(d.Referencias) ?? []; }
                catch (JsonException) { throw new UseCaseValidationException("Registre os IDs das referências do briefing."); }
                if (!referencias.ToHashSet().SetEquals(JsonSerializer.Deserialize<Guid[]>(solicitacao.ReferenciasJson)!))
                    throw new UseCaseValidationException("As referências do resultado precisam corresponder ao briefing.");
            }
        }
        if (d.CanvaAssetId?.Length > 100) throw new UseCaseValidationException("Identificador do Canva inválido.");
    }

    private static void ValidarTextos(params string?[] textos)
    {
        if (textos.Any(texto => texto?.Contains('\0') == true))
            throw new UseCaseValidationException("O texto contém um caractere inválido.");
    }

    private static AcervoImagem ConferirRepeticao(AcervoImagem imagem, DadosImagem d)
    {
        if (imagem.Titulo != d.Titulo.Trim() || imagem.Origem != d.Origem || imagem.Formato != d.Formato ||
            (imagem.Natureza != "NaoClassificada" && imagem.Natureza != d.Natureza) ||
            imagem.Prompt != d.Prompt || imagem.Referencias != d.Referencias || imagem.OriginalId != d.OriginalId ||
            imagem.SolicitacaoId != d.SolicitacaoId || imagem.CanvaUrl != ValidarCanva(d.CanvaUrl) || imagem.CanvaAssetId != d.CanvaAssetId ||
            !JsonSerializer.Deserialize<Guid[]>(imagem.ItensJson)!.ToHashSet().SetEquals(d.Itens))
            throw new UseCaseValidationException($"Este arquivo já existe como '{imagem.Titulo}' com outros metadados. Nenhum vínculo foi alterado.");
        return imagem;
    }
    private static AcervoSolicitacao ConferirSolicitacao(AcervoSolicitacao pedido, SolicitarImagem d)
    {
        if (pedido.ItemId != d.ItemId || pedido.Formato != d.Formato || pedido.Brief != d.Brief.Trim() ||
            !JsonSerializer.Deserialize<Guid[]>(pedido.ReferenciasJson)!.ToHashSet().SetEquals(d.Referencias))
            throw new UseCaseValidationException("Esta operação já foi usada para outra solicitação. Abra uma nova solicitação.");
        return pedido;
    }

    public static string? ValidarCanva(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.Length > 1000 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Host != "www.canva.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || !(uri.AbsolutePath.StartsWith("/design/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/d/", StringComparison.Ordinal)))
            throw new UseCaseValidationException("Use o link HTTPS de um design em www.canva.com.");
        return uri.GetLeftPart(UriPartial.Path);
    }
    private static Guid Empresa(Guid empresa) => empresa != Guid.Empty ? empresa : throw new UseCaseValidationException("Empresa não identificada.");
    private static void Formato(string formato)
    {
        if (formato is not ("1:1" or "4:5" or "16:9" or "9:16" or "3:2" or "livre")) throw new UseCaseValidationException("Formato inválido.");
    }
}
