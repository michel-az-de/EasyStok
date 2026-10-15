using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.UseCases.Acervo;
using EasyStock.Domain.Entities.Acervo;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Storefront.Uploads;

public partial class CardapioItemFotoUploadE2ETests
{
    [SkippableFact]
    public async Task Acervo_primeira_publicacao_simultanea_persiste_uma_operacao_e_uma_foto()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, itemId) = await SeedAsync(factory, empresa, "acervo-primeira-concorrente");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        using var upload = await EnviarAcervoAsync(client, itemId);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        using var aprovacao = await client.PostAsync($"/api/acervo/imagens/{imagem.Id}/aprovar", null);
        aprovacao.StatusCode.Should().Be(HttpStatusCode.OK);
        var operacao = new PublicarImagem(Guid.NewGuid(), itemId, false, null);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            client.PostAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/publicar", operacao)));
        var registros = new List<AcervoPublicacao>();
        foreach (var resposta in respostas)
        {
            using (resposta)
            {
                resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
                registros.Add((await resposta.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoPublicacao>>())!.Data);
            }
        }

        registros.Select(x => x.Id).Should().OnlyContain(id => id == operacao.OperacaoId);
        registros.Select(x => x.Url).Distinct().Should().ContainSingle();
        var historico = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoPublicacao[]>>($"/api/acervo/imagens/{imagem.Id}/historico"))!.Data;
        historico.Should().ContainSingle().Which.Id.Should().Be(operacao.OperacaoId);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var bypass = db.UseRowLevelSecurityBypass();
        var item = await db.CardapioItens.AsNoTracking().SingleAsync(x => x.Id == itemId);
        item.FotoUrl.Should().Be(registros[0].Url);
        item.ObterFotosGaleria().Should().Equal(registros[0].Url);
    }

    [SkippableFact]
    public async Task Acervo_publicacoes_concorrentes_recusam_a_previa_que_perdeu_a_disputa()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, itemId) = await SeedAsync(factory, empresa, "acervo-disputa-previa");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        using var upload = await EnviarAcervoAsync(client, itemId);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        using var aprovacao = await client.PostAsync($"/api/acervo/imagens/{imagem.Id}/aprovar", null);
        aprovacao.StatusCode.Should().Be(HttpStatusCode.OK);
        var operacoes = new[] { new PublicarImagem(Guid.NewGuid(), itemId, false, null), new PublicarImagem(Guid.NewGuid(), itemId, false, null) };

        var respostas = await Task.WhenAll(operacoes.Select(operacao =>
            client.PostAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/publicar", operacao)));
        try
        {
            respostas.Select(x => x.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest });
            var vencedora = (await respostas.Single(x => x.IsSuccessStatusCode).Content.ReadFromJsonAsync<AcervoEnvelope<AcervoPublicacao>>())!.Data;
            var historico = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoPublicacao[]>>($"/api/acervo/imagens/{imagem.Id}/historico"))!.Data;
            historico.Should().ContainSingle().Which.Id.Should().Be(vencedora.Id);
            var detalhe = (await client.GetFromJsonAsync<JsonElement>($"/api/atendimento/comanda/cardapio/{itemId}")).GetProperty("data");
            detalhe.GetProperty("fotoUrl").GetString().Should().Be(vencedora.Url);
            detalhe.GetProperty("fotos").EnumerateArray().Select(x => x.GetString()).Should().Equal(vencedora.Url);
        }
        finally
        {
            foreach (var resposta in respostas) resposta.Dispose();
        }
    }

    [SkippableFact]
    public async Task Acervo_solicitacao_simultanea_e_retomada_nao_duplicam_e_recusam_outro_conteudo()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, itemId) = await SeedAsync(factory, empresa, "acervo-solicitacao-idempotente");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        var pedido = new SolicitarImagem(Guid.NewGuid(), itemId, "4:5", "Foto do prato servido", []);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => client.PostAsJsonAsync("/api/acervo/solicitacoes", pedido)));
        foreach (var resposta in respostas)
        {
            using (resposta)
            {
                resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
                (await resposta.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoSolicitacao>>())!.Data.Id.Should().Be(pedido.OperacaoId);
            }
        }
        using var retomada = await client.PostAsJsonAsync("/api/acervo/solicitacoes", pedido);
        retomada.StatusCode.Should().Be(HttpStatusCode.OK);
        using var divergente = await client.PostAsJsonAsync("/api/acervo/solicitacoes", pedido with { Brief = "Outra fotografia" });
        divergente.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var fila = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>("/api/acervo/solicitacoes"))!.Data;
        fila.Should().ContainSingle().Which.Id.Should().Be(pedido.OperacaoId);
        fila[0].Brief.Should().Be(pedido.Brief);
    }

    [SkippableFact]
    public async Task Acervo_arquivo_repetido_recusa_metadados_vinculo_e_proveniencia_divergentes()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (storefront, itemId) = await SeedAsync(factory, empresa, "acervo-dedup-divergente");
        var outroItem = await AdicionarOutroItemAcervoAsync(factory, storefront.Id);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        var dados = new DadosImagem("Foto original", "Fotografia", "1:1", [itemId]);
        using var upload = await EnviarDadosAcervoAsync(client, dados);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        using var repetido = await EnviarDadosAcervoAsync(client, dados);
        repetido.StatusCode.Should().Be(HttpStatusCode.OK);
        (await repetido.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data.Id.Should().Be(imagem.Id);

        foreach (var divergente in new[]
        {
            dados with { Titulo = "Outro título" },
            dados with { Itens = [outroItem] },
            dados with { Origem = "Canva", OriginalId = imagem.Id, CanvaUrl = "https://www.canva.com/design/TESTE/edit" }
        })
        {
            using var resposta = await EnviarDadosAcervoAsync(client, divergente);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, await resposta.Content.ReadAsStringAsync());
        }

        var lista = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data;
        lista.Should().ContainSingle().Which.Id.Should().Be(imagem.Id);
        lista[0].Titulo.Should().Be(dados.Titulo);
        JsonSerializer.Deserialize<Guid[]>(lista[0].ItensJson).Should().Equal(itemId);
        lista[0].Origem.Should().Be("Fotografia");
        lista[0].OriginalId.Should().BeNull();
        lista[0].CanvaUrl.Should().BeNull();
    }

    [SkippableFact]
    public async Task Acervo_so_conclui_solicitacao_com_item_e_formato_corretos_e_retoma_o_upload()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (storefront, itemId) = await SeedAsync(factory, empresa, "acervo-conclusao-validada");
        var outroItem = await AdicionarOutroItemAcervoAsync(factory, storefront.Id);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        var pedido = new SolicitarImagem(Guid.NewGuid(), itemId, "1:1", "Prato quadrado", []);
        using var solicitacao = await client.PostAsJsonAsync("/api/acervo/solicitacoes", pedido);
        solicitacao.StatusCode.Should().Be(HttpStatusCode.OK, await solicitacao.Content.ReadAsStringAsync());
        var dados = new DadosImagem("Foto solicitada", "Fotografia", "1:1", [itemId], SolicitacaoId: pedido.OperacaoId);

        foreach (var invalido in new[] { dados with { Itens = [outroItem] }, dados with { Itens = [] }, dados with { Formato = "4:5" } })
        {
            using var resposta = await EnviarDadosAcervoAsync(client, invalido);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, await resposta.Content.ReadAsStringAsync());
        }
        var pendentes = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>("/api/acervo/solicitacoes"))!.Data;
        pendentes.Should().ContainSingle().Which.ConcluidoEm.Should().BeNull();
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data.Should().BeEmpty();

        using var upload = await EnviarDadosAcervoAsync(client, dados);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        imagem.SolicitacaoId.Should().Be(pedido.OperacaoId);
        using var repetido = await EnviarDadosAcervoAsync(client, dados);
        repetido.StatusCode.Should().Be(HttpStatusCode.OK, await repetido.Content.ReadAsStringAsync());
        (await repetido.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data.Id.Should().Be(imagem.Id);
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>("/api/acervo/solicitacoes"))!.Data.Should().BeEmpty();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var bypass = db.UseRowLevelSecurityBypass();
        var concluida = await db.Set<AcervoSolicitacao>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == pedido.OperacaoId);
        concluida.ConcluidoEm.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task Acervo_xmin_recusa_contexto_desatualizado_e_reverte_historico_sem_perder_a_foto_vencedora()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, itemId) = await SeedAsync(factory, empresa, "acervo-cas-contextos");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        using var upload = await EnviarAcervoAsync(client, itemId);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        using var primeiroScope = factory.Services.CreateScope();
        using var segundoScope = factory.Services.CreateScope();
        var primeiro = primeiroScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var segundo = segundoScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var bypassPrimeiro = primeiro.UseRowLevelSecurityBypass();
        using var bypassSegundo = segundo.UseRowLevelSecurityBypass();
        var vencedor = await primeiro.CardapioItens.SingleAsync(x => x.Id == itemId);
        var desatualizado = await segundo.CardapioItens.SingleAsync(x => x.Id == itemId);
        vencedor.AdicionarFotoGaleria("/files/foto-vencedora.png", preservarCapa: true);
        desatualizado.SubstituirCapaGaleria("/files/foto-perdedora.png");
        var publicacao = new AcervoPublicacao
        {
            Id = Guid.NewGuid(), EmpresaId = empresa, ImagemId = imagem.Id, ItemId = itemId,
            UsuarioId = UsuarioTesteId, Url = "/files/foto-perdedora.png"
        };
        segundo.Set<AcervoPublicacao>().Add(publicacao);
        await primeiro.CommitAsync();

        Func<Task> salvarDesatualizado = () => segundo.CommitAsync();
        await salvarDesatualizado.Should().ThrowAsync<DbUpdateConcurrencyException>();

        using var leituraScope = factory.Services.CreateScope();
        var leitura = leituraScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var bypassLeitura = leitura.UseRowLevelSecurityBypass();
        var persistido = await leitura.CardapioItens.AsNoTracking().SingleAsync(x => x.Id == itemId);
        persistido.FotoUrl.Should().Be("/files/foto-vencedora.png");
        persistido.ObterFotosGaleria().Should().Equal("/files/foto-vencedora.png");
        (await leitura.Set<AcervoPublicacao>().IgnoreQueryFilters().AnyAsync(x => x.Id == publicacao.Id)).Should().BeFalse();
    }

    private static async Task<HttpResponseMessage> EnviarDadosAcervoAsync(HttpClient client, DadosImagem dados)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(PngValido);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "original.png");
        form.Add(new StringContent(JsonSerializer.Serialize(dados)), "dados");
        return await client.PostAsync("/api/acervo/imagens", form);
    }

    private static async Task<Guid> AdicionarOutroItemAcervoAsync(WebApplicationFactory<Program> factory, Guid storefrontId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var bypass = db.UseRowLevelSecurityBypass();
        var item = CardapioItem.CriarAvulso(storefrontId, "Outro prato", 24m, "Pratos");
        db.CardapioItens.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }
}
