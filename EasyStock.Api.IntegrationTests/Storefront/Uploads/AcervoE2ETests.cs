using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Domain.Entities.Acervo;
using FluentAssertions;

namespace EasyStock.Api.IntegrationTests.Storefront.Uploads;

public partial class CardapioItemFotoUploadE2ETests
{
    private static async Task<HttpResponseMessage> EnviarAcervoAsync(HttpClient client, Guid item, string? canva = null, Guid? original = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(PngValido);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "original.png");
        form.Add(new StringContent(JsonSerializer.Serialize(new { titulo = "Prato do teste", origem = "Fotografia", formato = "1:1", itens = new[] { item }, canvaUrl = canva, originalId = original })), "dados");
        return await client.PostAsync("/api/acervo/imagens", form);
    }

    [SkippableFact]
    public async Task Acervo_preserva_original_aprova_publica_e_retoma_sem_duplicar()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-fluxo");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        using var upload = await EnviarAcervoAsync(client, item);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        (await client.GetByteArrayAsync($"/api/acervo/imagens/{imagem.Id}/arquivo")).Should().Equal(PngValido);
        using var repetido = await EnviarAcervoAsync(client, item);
        (await repetido.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data.Id.Should().Be(imagem.Id);
        var corpo = new { operacaoId = Guid.NewGuid(), itemId = item, galeria = false, capaEsperada = (string?)null };
        using var negado = await client.PostAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/publicar", corpo);
        negado.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsync($"/api/acervo/imagens/{imagem.Id}/aprovar", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var publicado = await client.PostAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/publicar", corpo);
        publicado.StatusCode.Should().Be(HttpStatusCode.OK, await publicado.Content.ReadAsStringAsync());
        var registro = (await publicado.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoPublicacao>>())!.Data;
        var retentativas = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => client.PostAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/publicar", corpo)));
        foreach (var resposta in retentativas) { resposta.StatusCode.Should().Be(HttpStatusCode.OK); resposta.Dispose(); }
        var historico = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoPublicacao[]>>($"/api/acervo/imagens/{imagem.Id}/historico"))!.Data;
        historico.Should().ContainSingle(); historico[0].Url.Should().Be(registro.Url);
        using var desatualizado = await client.PostAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/publicar", corpo with { });
        desatualizado.StatusCode.Should().Be(HttpStatusCode.OK);
        using var outraOperacao = await client.PostAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/publicar", new { operacaoId = Guid.NewGuid(), itemId = item, galeria = false, capaEsperada = (string?)null });
        outraOperacao.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        client.DefaultRequestHeaders.Authorization = null;
        (await client.GetAsync($"/files/{imagem.StorageKey}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/acervo/imagens/{imagem.Id}/arquivo")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var publico = await client.GetFromJsonAsync<JsonElement>("/api/storefront/acervo-fluxo/menu");
        publico.ToString().Should().Contain("acervo-" + corpo.operacaoId);
    }

    [SkippableFact]
    public async Task Acervo_recusa_cruzamento_de_empresa_perfil_link_e_origem_invalida()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid(); var outra = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-empresa-a");
        var (_, itemOutra) = await SeedAsync(factory, outra, "acervo-empresa-b");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        (await EnviarAcervoAsync(client, itemOutra)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await EnviarAcervoAsync(client, item, "https://www.canva.com.evil.test/design/a")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var upload = await EnviarAcervoAsync(client, item);
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", outra));
        (await client.GetAsync($"/api/acervo/imagens/{imagem.Id}/arquivo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsync($"/api/acervo/imagens/{imagem.Id}/aprovar", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var lista = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data;
        lista.Should().BeEmpty();
        (await EnviarAcervoAsync(client, itemOutra, original: imagem.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Operador", empresa, "modulo:cardapio"));
        (await client.GetAsync("/api/acervo/imagens")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record AcervoEnvelope<T>(T Data);
}
