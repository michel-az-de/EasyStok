using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Acervo;
using EasyStock.Domain.Entities.Acervo;
using EasyStock.Infra.Async.Storage;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace EasyStock.Api.IntegrationTests.Storefront.Uploads;

public partial class CardapioItemFotoUploadE2ETests
{
    [SkippableFact]
    public async Task Acervo_solicitacao_concluida_e_conclusoes_concorrentes_preservam_um_unico_resultado()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-job-disputa");
        using var client = ClienteAcervoNegativo(factory, empresa);
        var solicitacao = new SolicitarImagem(Guid.NewGuid(), item, "1:1", "Uma foto do prato", []);
        using var pedido = await client.PostAsJsonAsync("/api/acervo/solicitacoes", solicitacao);
        pedido.StatusCode.Should().Be(HttpStatusCode.OK);
        var dados = new DadosImagem("Resultado concorrente", "Fotografia", "1:1", [item], SolicitacaoId: solicitacao.OperacaoId);
        var candidatos = new[] { PngAcervoNegativo(SKColors.Orange), PngAcervoNegativo(SKColors.Blue) };

        var respostas = await Task.WhenAll(candidatos.Select(bytes => EnviarBytesAcervoNegativoAsync(client, dados, bytes)));
        try
        {
            respostas.Count(x => x.StatusCode == HttpStatusCode.OK).Should().Be(1);
            respostas.Count(x => x.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict).Should().Be(1);
            var indiceVencedor = Array.FindIndex(respostas, x => x.StatusCode == HttpStatusCode.OK);
            var imagem = (await respostas[indiceVencedor].Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
            DateTime? concluidoEm;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                using var bypass = db.UseRowLevelSecurityBypass();
                (await db.Set<AcervoImagem>().IgnoreQueryFilters().CountAsync(x => x.SolicitacaoId == solicitacao.OperacaoId)).Should().Be(1);
                concluidoEm = (await db.Set<AcervoSolicitacao>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == solicitacao.OperacaoId)).ConcluidoEm;
            }

            using var replay = await EnviarBytesAcervoNegativoAsync(client, dados, candidatos[indiceVencedor]);
            replay.StatusCode.Should().Be(HttpStatusCode.OK);
            (await replay.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data.Id.Should().Be(imagem.Id);
            using var outroResultado = await EnviarBytesAcervoNegativoAsync(client, dados, candidatos[1 - indiceVencedor]);
            outroResultado.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
            using var leituraScope = factory.Services.CreateScope();
            var leitura = leituraScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypassLeitura = leitura.UseRowLevelSecurityBypass();
            var resultados = await leitura.Set<AcervoImagem>().IgnoreQueryFilters().Where(x => x.SolicitacaoId == solicitacao.OperacaoId).ToListAsync();
            resultados.Should().ContainSingle().Which.Id.Should().Be(imagem.Id);
            (await leitura.Set<AcervoSolicitacao>().IgnoreQueryFilters().SingleAsync(x => x.Id == solicitacao.OperacaoId)).ConcluidoEm.Should().Be(concluidoEm);
        }
        finally
        {
            foreach (var resposta in respostas) resposta.Dispose();
        }
    }

    [SkippableFact]
    public async Task Acervo_historico_continua_legivel_apos_troca_de_capa_pelo_upload_convencional()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-historico-retido");
        using var client = ClienteAcervoNegativo(factory, empresa);
        var imagem = await PrepararImagemAcervoNegativoAsync(client, item);
        var operacao = new PublicarImagem(Guid.NewGuid(), item, false, null);
        using var publicar = await client.PostAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/publicar", operacao);
        publicar.StatusCode.Should().Be(HttpStatusCode.OK, await publicar.Content.ReadAsStringAsync());
        var registro = (await publicar.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoPublicacao>>())!.Data;
        var bytesPublicados = await client.GetByteArrayAsync(new Uri(registro.Url).PathAndQuery);

        using var trocar = await EnviarFotoConsoleAsync(client, item, conteudo: PngAcervoNegativo(SKColors.Blue));
        trocar.StatusCode.Should().Be(HttpStatusCode.OK, await trocar.Content.ReadAsStringAsync());
        using var retomar = await client.PostAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/publicar", operacao);
        retomar.StatusCode.Should().Be(HttpStatusCode.OK);
        (await retomar.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoPublicacao>>())!.Data.Url.Should().Be(registro.Url);
        using var arquivoHistorico = await client.GetAsync(new Uri(registro.Url).PathAndQuery);
        arquivoHistorico.StatusCode.Should().Be(HttpStatusCode.OK, "o histórico e o replay continuam apontando para este arquivo");
        (await arquivoHistorico.Content.ReadAsByteArrayAsync()).Should().Equal(bytesPublicados);
        (await client.GetByteArrayAsync($"/api/acervo/imagens/{imagem.Id}/arquivo")).Should().Equal(PngValido);
    }

    [SkippableFact]
    public async Task Acervo_upload_com_rejeicao_confirmada_do_banco_nao_acumula_originais_orfaos_no_retry()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var raiz = Path.Combine(Path.GetTempPath(), "easystok-acervo-negativo", Guid.NewGuid().ToString("N"));
        await using var factory = CriarFactory().WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.PostConfigure<FileStorageOptions>(o => o.LocalRootPath = raiz)));
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-upload-banco-falha");
        using var client = ClienteAcervoNegativo(factory, empresa);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION falha_acervo_negativo() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Rejeicao controlada antes de gravar a imagem'; END $$;
                CREATE TRIGGER falha_acervo_negativo BEFORE INSERT ON acervo_imagens
                FOR EACH ROW EXECUTE FUNCTION falha_acervo_negativo();
                """);
        }
        using var falhou = await EnviarAcervoAsync(client, item);
        falhou.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data.Should().BeEmpty();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER falha_acervo_negativo ON acervo_imagens; DROP FUNCTION falha_acervo_negativo();");
        }

        using var retomar = await EnviarAcervoAsync(client, item);
        retomar.StatusCode.Should().Be(HttpStatusCode.OK, await retomar.Content.ReadAsStringAsync());
        var imagem = (await retomar.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        (await client.GetByteArrayAsync($"/api/acervo/imagens/{imagem.Id}/arquivo")).Should().Equal(PngValido);
        Directory.GetFiles(raiz, "*", SearchOption.AllDirectories).Should().ContainSingle("a tentativa rejeitada pelo banco não deve deixar um original sem registro");
    }

    [SkippableFact]
    public async Task Acervo_retry_apos_confirmacao_perdida_nao_apaga_original_ja_persistido()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var injetar = 1;
        await using var factory = CriarFactory().WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddScoped<IUnitOfWork>(sp => new CommitAmbiguoAcervoNegativo(
                sp.GetRequiredService<EasyStockDbContext>(), () => Interlocked.Exchange(ref injetar, 0) == 1))));
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-commit-ambiguo");
        using var client = ClienteAcervoNegativo(factory, empresa);
        using var primeira = await EnviarAcervoAsync(client, item);
        primeira.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.InternalServerError, HttpStatusCode.Conflict);

        using var replay = await EnviarAcervoAsync(client, item);
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        var imagem = (await replay.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var bypass = db.UseRowLevelSecurityBypass();
        (await db.Set<AcervoImagem>().IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresa)).Should().Be(1);
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        (await storage.ExistsAsync(imagem.StorageKey, CancellationToken.None)).Should().BeTrue("o banco confirmou o primeiro INSERT antes da falha de confirmação e da violação única da retentativa");
        (await storage.DownloadAsync(imagem.StorageKey)).Should().Equal(PngValido);
    }

    [SkippableFact]
    public async Task Acervo_resposta_perdida_do_storage_nao_acumula_original_sem_registro_no_retry()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var raiz = Path.Combine(Path.GetTempPath(), "easystok-acervo-negativo", Guid.NewGuid().ToString("N"));
        var injetar = 1;
        await using var factory = CriarFactory().WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.PostConfigure<FileStorageOptions>(o => o.LocalRootPath = raiz);
            s.AddSingleton<IFileStorage>(sp => new StorageRespostaPerdidaAcervoNegativo(
                ActivatorUtilities.CreateInstance<LocalFileStorage>(sp), () => Interlocked.Exchange(ref injetar, 0) == 1));
        }));
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-storage-resposta-perdida");
        using var client = ClienteAcervoNegativo(factory, empresa);
        using var primeira = await EnviarAcervoAsync(client, item);
        primeira.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data.Should().BeEmpty();

        using var replay = await EnviarAcervoAsync(client, item);
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        var imagem = (await replay.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        (await client.GetByteArrayAsync($"/api/acervo/imagens/{imagem.Id}/arquivo")).Should().Equal(PngValido);
        Directory.GetFiles(raiz, "*", SearchOption.AllDirectories).Should().ContainSingle("a primeira gravação ocorreu, mas a resposta do storage se perdeu antes de chegar ao use case");
    }

    [SkippableFact]
    public async Task Acervo_falha_de_gravacao_publica_preserva_item_e_permite_retomar_a_mesma_operacao()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var raiz = Path.Combine(Path.GetTempPath(), "easystok-acervo-negativo", Guid.NewGuid().ToString("N"));
        await using var factory = CriarFactory().WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.PostConfigure<FileStorageOptions>(o => o.LocalRootPath = raiz)));
        var empresa = Guid.NewGuid();
        var (storefront, item) = await SeedAsync(factory, empresa, "acervo-storage-retomada");
        using var client = ClienteAcervoNegativo(factory, empresa);
        var imagem = await PrepararImagemAcervoNegativoAsync(client, item);
        var operacao = new PublicarImagem(Guid.NewGuid(), item, false, null);
        string extensao;
        using (var scope = factory.Services.CreateScope())
            extensao = scope.ServiceProvider.GetRequiredService<IImageProcessor>().Optimize(PngValido, "image/png").Extension;
        var destino = Path.Combine(raiz, "cardapios", empresa.ToString(), storefront.Id.ToString(), item.ToString(), $"acervo-{operacao.OperacaoId}{extensao}");
        Directory.CreateDirectory(destino);
        using var falhou = await client.PostAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/publicar", operacao);
        falhou.IsSuccessStatusCode.Should().BeFalse("o destino é um diretório e não permite gravar o arquivo");
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoPublicacao[]>>($"/api/acervo/imagens/{imagem.Id}/historico"))!.Data.Should().BeEmpty();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            var antes = await db.CardapioItens.AsNoTracking().SingleAsync(x => x.Id == item);
            antes.FotoUrl.Should().BeNull();
            antes.ObterFotosGaleria().Should().BeEmpty();
        }
        Directory.Delete(destino);

        using var retomada = await client.PostAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/publicar", operacao);
        retomada.StatusCode.Should().Be(HttpStatusCode.OK, await retomada.Content.ReadAsStringAsync());
        var registro = (await retomada.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoPublicacao>>())!.Data;
        registro.Id.Should().Be(operacao.OperacaoId);
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoPublicacao[]>>($"/api/acervo/imagens/{imagem.Id}/historico"))!.Data.Should().ContainSingle();
        using var scopeFinal = factory.Services.CreateScope();
        var storage = scopeFinal.ServiceProvider.GetRequiredService<IFileStorage>();
        (await storage.DownloadAsync($"cardapios/{empresa}/{storefront.Id}/{item}/acervo-{operacao.OperacaoId}{extensao}")).Should().NotBeEmpty();
    }

    [SkippableFact]
    public async Task Acervo_original_adulterado_e_disputa_da_ultima_vaga_nao_perdem_integridade_da_galeria()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (storefront, item) = await SeedAsync(factory, empresa, "acervo-integridade-vaga");
        using var client = ClienteAcervoNegativo(factory, empresa);
        var imagem = await PrepararImagemAcervoNegativoAsync(client, item);
        var separador = imagem.StorageKey.LastIndexOf('/');
        var pastaOriginal = imagem.StorageKey[..separador];
        var arquivoOriginal = imagem.StorageKey[(separador + 1)..];
        using (var scope = factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            await storage.UploadAsync(new FileUploadRequest(pastaOriginal, arquivoOriginal, "image/png", PngAcervoNegativo(SKColors.Blue), IsPublic: false));
        }
        using var adulterada = await client.PostAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/publicar", new PublicarImagem(Guid.NewGuid(), item, false, null));
        adulterada.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoPublicacao[]>>($"/api/acervo/imagens/{imagem.Id}/historico"))!.Data.Should().BeEmpty();
        const string capa = "/files/angulo-0.png";
        using (var scope = factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            await storage.UploadAsync(new FileUploadRequest(pastaOriginal, arquivoOriginal, "image/png", PngValido, IsPublic: false));
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            var entidade = await db.CardapioItens.SingleAsync(x => x.Id == item);
            entidade.FotoUrl.Should().BeNull();
            entidade.ObterFotosGaleria().Should().BeEmpty();
            for (var i = 0; i < 4; i++) entidade.AdicionarFotoGaleria($"/files/angulo-{i}.png");
            await db.CommitAsync();
        }

        var respostas = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(
            $"/api/acervo/imagens/{imagem.Id}/publicar", new PublicarImagem(Guid.NewGuid(), item, true, capa))));
        try
        {
            respostas.Select(x => x.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest });
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            var persistido = await db.CardapioItens.AsNoTracking().SingleAsync(x => x.Id == item);
            persistido.FotoUrl.Should().Be(capa);
            persistido.ObterFotosGaleria().Should().HaveCount(5);
            persistido.ObterFotosGaleria().Take(4).Should().Equal(Enumerable.Range(0, 4).Select(i => $"/files/angulo-{i}.png"));
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            (await storage.ListAsync($"cardapios/{empresa}/{storefront.Id}/{item}")).Should().ContainSingle();
        }
        finally
        {
            foreach (var resposta in respostas) resposta.Dispose();
        }
    }

    private static HttpClient ClienteAcervoNegativo(WebApplicationFactory<Program> factory, Guid empresa)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        return client;
    }

    private static async Task<AcervoImagem> PrepararImagemAcervoNegativoAsync(HttpClient client, Guid item)
    {
        using var upload = await EnviarAcervoAsync(client, item);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        using var aprovar = await client.PostAsync($"/api/acervo/imagens/{imagem.Id}/aprovar", null);
        aprovar.StatusCode.Should().Be(HttpStatusCode.OK);
        return imagem;
    }

    private static async Task<HttpResponseMessage> EnviarBytesAcervoNegativoAsync(HttpClient client, DadosImagem dados, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "resultado.png");
        form.Add(new StringContent(JsonSerializer.Serialize(dados)), "dados");
        return await client.PostAsync("/api/acervo/imagens", form);
    }

    private static byte[] PngAcervoNegativo(SKColor cor)
    {
        using var bitmap = new SKBitmap(16, 16);
        bitmap.Erase(cor);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private sealed class RetentativaJaPersistidaAcervoException : Exception { }

    private sealed class StorageRespostaPerdidaAcervoNegativo(IFileStorage inner, Func<bool> injetar) : IFileStorage
    {
        public async Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken ct = default)
        {
            var resultado = await inner.UploadAsync(request, ct);
            if (request.BucketPath.StartsWith("acervo/", StringComparison.Ordinal) && injetar())
                throw new IOException("Confirmação de gravação do storage perdida depois de persistir os bytes.");
            return resultado;
        }
        public Task DeleteAsync(string key, CancellationToken ct = default) => inner.DeleteAsync(key, ct);
        public Task<IReadOnlyList<StoredFileInfo>> ListAsync(string path, CancellationToken ct = default) => inner.ListAsync(path, ct);
        public Task<byte[]> DownloadAsync(string key, CancellationToken ct = default) => inner.DownloadAsync(key, ct);
        public Task<Stream> OpenUploadStreamAsync(string key, string contentType, CancellationToken ct) => inner.OpenUploadStreamAsync(key, contentType, ct);
        public Task<Stream> DownloadStreamAsync(string key, CancellationToken ct) => inner.DownloadStreamAsync(key, ct);
        public Task<Uri> CreatePreSignedDownloadUrlAsync(string key, TimeSpan ttl, string fileName, CancellationToken ct) => inner.CreatePreSignedDownloadUrlAsync(key, ttl, fileName, ct);
        public Task<bool> ExistsAsync(string key, CancellationToken ct) => inner.ExistsAsync(key, ct);
    }

    private sealed class CommitAmbiguoAcervoNegativo(EasyStockDbContext db, Func<bool> injetar) : IUnitOfWork
    {
        private bool _imagemPersistida;

        public async Task<int> CommitAsync()
        {
            var novaImagem = db.ChangeTracker.Entries<AcervoImagem>().Any(x => x.State == EntityState.Added);
            var resultado = await db.CommitAsync();
            _imagemPersistida |= novaImagem;
            if (db.Database.CurrentTransaction is null) SimularRetentativa();
            return resultado;
        }

        private void SimularRetentativa()
        {
            // Modela a violação única da repetição após o primeiro commit confirmado no banco.
            if (_imagemPersistida && injetar()) throw new RetentativaJaPersistidaAcervoException();
        }

        public bool EhViolacaoDeUnicidade(Exception ex) => ex is RetentativaJaPersistidaAcervoException || db.EhViolacaoDeUnicidade(ex);
        public void DescartarAlteracoesPendentes() => db.DescartarAlteracoesPendentes();
        public Task<IDbTransactionScope> BeginTransactionAsync(CancellationToken ct = default) => db.BeginTransactionAsync(ct);
        public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
        {
            await db.ExecuteInTransactionAsync(action, ct);
            SimularRetentativa();
        }
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
        {
            var resultado = await db.ExecuteInTransactionAsync(action, ct);
            SimularRetentativa();
            return resultado;
        }
        public async Task<T> ExecuteInTransactionSemRetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
        {
            var resultado = await db.ExecuteInTransactionSemRetryAsync(action, ct);
            SimularRetentativa();
            return resultado;
        }
    }
}
