using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EasyStock.Application.UseCases.Acervo;
using EasyStock.Domain.Entities.Acervo;
using FluentAssertions;
using SkiaSharp;

namespace EasyStock.Api.IntegrationTests.Storefront.Uploads;

public sealed partial class CardapioItemFotoUploadE2ETests
{
    [SkippableFact]
    public async Task Acervo_negativos_payloads_nulos_malformados_e_campos_ausentes_retornam_400()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-neg-payload");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        foreach (var dados in new string?[]
        {
            null, "null", "[]", "\"texto\"", "{", "{}",
            "{\"titulo\":\"X\",\"origem\":\"Fotografia\",\"formato\":\"1:1\",\"itens\":null}",
            "{\"titulo\":\"X\",\"origem\":\"Fotografia\",\"formato\":\"1:1\",\"itens\":[null]}"
        })
        {
            using var form = FormularioNegativoAcervo(dados, PngValido);
            using var resposta = await client.PostAsync("/api/acervo/imagens", form);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"dados={dados ?? "ausente"} não pode causar erro 500");
        }
        var metadados = JsonSerializer.Serialize(new DadosImagem("Sem arquivo", "Fotografia", "1:1", [item]));
        foreach (var arquivo in new byte[]?[] { null, [] })
        {
            using var form = FormularioNegativoAcervo(metadados, arquivo);
            using var resposta = await client.PostAsync("/api/acervo/imagens", form);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        foreach (var (metodo, rota) in new[]
        {
            (HttpMethod.Put, $"/api/acervo/imagens/{Guid.NewGuid()}/canva"),
            (HttpMethod.Post, $"/api/acervo/imagens/{Guid.NewGuid()}/publicar"),
            (HttpMethod.Post, "/api/acervo/solicitacoes")
        })
        {
            foreach (var corpo in new[] { "null", "[]", "{", "" })
            {
                using var request = new HttpRequestMessage(metodo, rota) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
                using var resposta = await client.SendAsync(request);
                resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{metodo} {rota}, corpo={corpo}");
            }
        }
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data.Should().BeEmpty();
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>("/api/acervo/solicitacoes"))!.Data.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Acervo_negativos_nivel_modulo_e_empresa_sao_barreiras_independentes()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-neg-permissoes");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        using var upload = await EnviarAcervoAsync(client, item);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        foreach (var (token, esperado) in new (string? Token, HttpStatusCode Esperado)[]
        {
            (null, HttpStatusCode.Unauthorized),
            (GerarJwt("Operador", empresa, "AcessarModuloCardapio"), HttpStatusCode.Forbidden),
            (GerarJwt("Gerente", empresa, "AcessarModuloCaixa"), HttpStatusCode.Forbidden),
            (GerarJwt("Admin", empresa, "AcessarModuloCaixa"), HttpStatusCode.Forbidden),
            (GerarJwt("Admin"), HttpStatusCode.Forbidden)
        })
        {
            client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
            foreach (var request in PedidosNegativosAcervo(imagem.Id, item))
            {
                using (request)
                using (var resposta = await client.SendAsync(request))
                    resposta.StatusCode.Should().Be(esperado, $"{request.Method} {request.RequestUri} deve exigir nível, módulo e empresa");
            }
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Gerente", empresa, "AcessarModuloCardapio"));
        var lista = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data;
        lista.Should().ContainSingle().Which.AprovadoEm.Should().BeNull();
        lista[0].CanvaUrl.Should().BeNull();
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoPublicacao[]>>($"/api/acervo/imagens/{imagem.Id}/historico"))!.Data.Should().BeEmpty();
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>("/api/acervo/solicitacoes"))!.Data.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Acervo_negativos_empresa_na_query_ou_no_body_nao_libera_imagem_alheia()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid(); var outra = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-neg-tenant-a");
        var (_, itemOutra) = await SeedAsync(factory, outra, "acervo-neg-tenant-b");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        using var upload = await EnviarAcervoAsync(client, item);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        var pedidoId = Guid.NewGuid();
        using var pedido = await client.PostAsJsonAsync("/api/acervo/solicitacoes", new SolicitarImagem(pedidoId, item, "1:1", "Foto do prato", []));
        pedido.StatusCode.Should().Be(HttpStatusCode.OK, await pedido.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", outra));
        foreach (var caminho in new[] { "arquivo", "miniatura" })
        {
            using var resposta = await client.GetAsync($"/api/acervo/imagens/{imagem.Id}/{caminho}?empresaId={empresa}");
            resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        using (var aprovar = await client.PostAsync($"/api/acervo/imagens/{imagem.Id}/aprovar?empresaId={empresa}", null))
            aprovar.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var canva = await client.PutAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/canva", new { empresaId = empresa, url = "https://www.canva.com/design/revisao/edit" }))
            canva.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var publicar = await client.PostAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/publicar", new
        {
            empresaId = empresa, operacaoId = Guid.NewGuid(), itemId = itemOutra, galeria = false, capaEsperada = (string?)null
        }))
            publicar.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var referencia = await client.PostAsJsonAsync("/api/acervo/solicitacoes", new SolicitarImagem(Guid.NewGuid(), itemOutra, "1:1", "Referência alheia", [imagem.Id])))
            referencia.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var original = await EnviarDadosAcervoAsync(client, new DadosImagem("Alheio", "Fotografia", "1:1", [itemOutra], OriginalId: imagem.Id)))
            original.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var solicitacao = await EnviarDadosAcervoAsync(client, new DadosImagem("Alheio", "Fotografia", "1:1", [itemOutra], SolicitacaoId: pedidoId)))
            solicitacao.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>($"/api/acervo/imagens?empresaId={empresa}"))!.Data.Should().BeEmpty();
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoPublicacao[]>>($"/api/acervo/imagens/{imagem.Id}/historico"))!.Data.Should().BeEmpty();
        using var proprio = await EnviarAcervoAsync(client, itemOutra);
        proprio.StatusCode.Should().Be(HttpStatusCode.OK, await proprio.Content.ReadAsStringAsync());
        var segunda = (await proprio.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        segunda.Id.Should().NotBe(imagem.Id, "deduplicação de hash deve ficar restrita à empresa");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        var preservada = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data.Should().ContainSingle().Which;
        preservada.Id.Should().Be(imagem.Id);
        preservada.CanvaUrl.Should().BeNull();
        preservada.AprovadoEm.Should().BeNull();
    }

    [SkippableFact]
    public async Task Acervo_negativos_mime_falso_e_png_parcial_nao_entram_no_banco()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-neg-mime");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        var dados = JsonSerializer.Serialize(new DadosImagem("Imagem inválida", "Fotografia", "1:1", [item]));
        using var bitmap = new SKBitmap(64, 64);
        bitmap.Erase(SKColors.Red);
        using var imagem = SKImage.FromBitmap(bitmap);
        using var png = imagem.Encode(SKEncodedImageFormat.Png, 100);
        var parcial = png.ToArray()[..^30];
        using (var data = SKData.CreateCopy(parcial))
        using (var codec = SKCodec.Create(data))
        using (var pixels = new SKBitmap(codec.Info))
            codec.GetPixels(pixels.Info, pixels.GetPixels()).Should().Be(SKCodecResult.IncompleteInput, "a amostra precisa conter pixels incompletos");
        foreach (var (bytes, mime) in new[]
        {
            (PngValido, "image/jpeg"), (PngValido, "image/webp"), (PngValido, "text/html"),
            (Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"), "image/png"),
            (parcial, "image/png")
        })
        {
            using var form = FormularioNegativoAcervo(dados, bytes, mime);
            using var resposta = await client.PostAsync("/api/acervo/imagens", form);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"MIME={mime}, tamanho={bytes.Length}; decodificação parcial não é sucesso");
        }
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Acervo_negativos_links_canva_host_credenciais_porta_e_esquema_sao_recusados()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-neg-canva");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        using var upload = await EnviarAcervoAsync(client, item);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        foreach (var url in new[]
        {
            "javascript:alert(1)", "http://www.canva.com/design/a/edit", "//www.canva.com/design/a/edit",
            "https://www.canva.com.evil.invalid/design/a/edit", "https://www.canva.com@evil.invalid/design/a/edit",
            "https://user@www.canva.com/design/a/edit", "https://www.canva.com:444/design/a/edit",
            "https://www.canva.com/settings", "https://www.canva.com//design/a/edit",
            "https://www.canva.com/design/" + new string('a', 1000)
        })
        {
            using var resposta = await client.PutAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/canva", new { url });
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, url);
        }
        using (var resposta = await client.PutAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/canva", new { url = "https://www.canva.com/design/revisao/edit", assetId = new string('a', 101) }))
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data.Should().ContainSingle().Which.CanvaUrl.Should().BeNull();
        using var valido = await client.PutAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/canva", new { url = "https://www.canva.com/design/revisao/edit?utm_source=teste#fragmento" });
        valido.StatusCode.Should().Be(HttpStatusCode.OK, await valido.Content.ReadAsStringAsync());
        (await valido.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data.CanvaUrl.Should().Be("https://www.canva.com/design/revisao/edit");
    }

    [SkippableFact]
    public async Task Acervo_negativos_nome_do_upload_e_caminhos_codificados_nao_expoem_original()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-neg-path");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        var dados = JsonSerializer.Serialize(new DadosImagem("Arquivo privado", "Fotografia", "1:1", [item]));
        using var form = FormularioNegativoAcervo(dados, PngValido, nome: "../../cardapios/invasao.png");
        using var upload = await client.PostAsync("/api/acervo/imagens", form);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        imagem.StorageKey.Should().Be($"acervo/{empresa}/{imagem.Id}/original.png");
        (await client.GetByteArrayAsync($"/api/acervo/imagens/{imagem.Id}/arquivo")).Should().Equal(PngValido);
        client.DefaultRequestHeaders.Authorization = null;
        foreach (var caminho in new[]
        {
            $"/files/%61cervo/{empresa}/{imagem.Id}/original.png",
            $"/files/acervo./{empresa}/{imagem.Id}/original.png",
            $"/files/cardapios/../{imagem.StorageKey}",
            $"/files/cardapios/%2e%2e/{imagem.StorageKey}",
            $"/files/{imagem.StorageKey.Replace("/", "%5c")}",
            $"/files/{imagem.StorageKey.Replace("/", "%2f")}",
            $"/files/../uploaded-files/{imagem.StorageKey}"
        })
        {
            using var resposta = await client.GetAsync(caminho);
            resposta.StatusCode.Should().Be(HttpStatusCode.NotFound, caminho);
        }
    }

    [SkippableFact]
    public async Task Acervo_negativos_caractere_nulo_em_textos_retornam_400_sem_persistir()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-neg-nul");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        var dados = new DadosImagem("Prato", "Fotografia", "1:1", [item]);
        foreach (var entrada in new[]
        {
            dados with { Titulo = "Prato\0teste" },
            dados with { Prompt = "Prompt\0teste" },
            dados with { Referencias = "Referência\0teste" },
            dados with { CanvaAssetId = "asset\0teste" }
        })
        {
            using var resposta = await EnviarDadosAcervoAsync(client, entrada);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, "NUL em JSON é entrada inválida para texto PostgreSQL, não erro interno");
        }
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data.Should().BeEmpty();
        using var upload = await EnviarAcervoAsync(client, item);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        using (var canva = await client.PutAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/canva", new
        {
            url = "https://www.canva.com/design/revisao/edit", assetId = "asset\0teste"
        }))
            canva.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var pedido = await client.PostAsJsonAsync("/api/acervo/solicitacoes", new SolicitarImagem(Guid.NewGuid(), item, "1:1", "Brief\0teste", [])))
            pedido.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data.Should().ContainSingle().Which.CanvaUrl.Should().BeNull();
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>("/api/acervo/solicitacoes"))!.Data.Should().BeEmpty();
    }

    private static MultipartFormDataContent FormularioNegativoAcervo(string? dados, byte[]? bytes, string mime = "image/png", string nome = "original.png")
    {
        var form = new MultipartFormDataContent();
        if (bytes is not null)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(mime);
            form.Add(file, "file", nome);
        }
        if (dados is not null) form.Add(new StringContent(dados), "dados");
        return form;
    }

    private static IEnumerable<HttpRequestMessage> PedidosNegativosAcervo(Guid imagem, Guid item)
    {
        foreach (var rota in new[] { "/api/acervo/imagens", $"/api/acervo/imagens/{imagem}/arquivo", $"/api/acervo/imagens/{imagem}/miniatura", $"/api/acervo/imagens/{imagem}/historico", "/api/acervo/solicitacoes" })
            yield return new HttpRequestMessage(HttpMethod.Get, rota);
        yield return new HttpRequestMessage(HttpMethod.Post, $"/api/acervo/imagens/{imagem}/aprovar");
        yield return new HttpRequestMessage(HttpMethod.Put, $"/api/acervo/imagens/{imagem}/canva") { Content = JsonContent.Create(new { url = "https://www.canva.com/design/revisao/edit" }) };
        yield return new HttpRequestMessage(HttpMethod.Post, $"/api/acervo/imagens/{imagem}/publicar") { Content = JsonContent.Create(new PublicarImagem(Guid.NewGuid(), item, false, null)) };
        yield return new HttpRequestMessage(HttpMethod.Post, "/api/acervo/solicitacoes") { Content = JsonContent.Create(new SolicitarImagem(Guid.NewGuid(), item, "1:1", "Foto", [])) };
        yield return new HttpRequestMessage(HttpMethod.Post, "/api/acervo/imagens") { Content = FormularioNegativoAcervo(JsonSerializer.Serialize(new DadosImagem("Prato do teste", "Fotografia", "1:1", [item])), PngValido) };
    }
}
