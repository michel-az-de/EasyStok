using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Acervo;
using EasyStock.Domain.Entities.Acervo;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace EasyStock.Api.IntegrationTests.Storefront.Uploads;

public partial class CardapioItemFotoUploadE2ETests
{
    [SkippableFact]
    public async Task Acervo_incremento_busca_e_filtros_preservam_vinculos_e_distinguem_peca_de_comunicacao()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (storefront, item) = await SeedAsync(factory, empresa, "acervo-filtros-reais");
        var outroItem = await AdicionarOutroItemAcervoAsync(factory, storefront.Id);
        using var client = ClienteAcervoNegativo(factory, empresa);
        var produto = await CriarImagemCoberturaFuncionalAsync(client, item, 1200, 1200, "1:1", SKColors.Blue);
        var arte = new DadosImagem("Peça de comunicação vertical", "IA", "4:5", [outroItem], "Peça ilustrativa com o produto de referência", Natureza: "Comunicacao");
        using var enviar = await EnviarBytesAcervoNegativoAsync(client, arte, PngCoberturaFuncional(1200, 1500, SKColors.Orange));
        enviar.StatusCode.Should().Be(HttpStatusCode.OK, await enviar.Content.ReadAsStringAsync());
        var comunicacao = (await enviar.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            await db.Set<AcervoImagem>().IgnoreQueryFilters().Where(x => x.Id == produto.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Natureza, "NaoClassificada"));
        }
        using var classificacaoDesatualizada = await client.PutAsJsonAsync($"/api/acervo/imagens/{produto.Id}/natureza", new { natureza = "Produto", naturezaEsperada = "Comunicacao" });
        classificacaoDesatualizada.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        for (var tentativa = 0; tentativa < 2; tentativa++)
        {
            using var classificar = await client.PutAsJsonAsync($"/api/acervo/imagens/{produto.Id}/natureza", new { natureza = "Produto", naturezaEsperada = "NaoClassificada" });
            classificar.StatusCode.Should().Be(HttpStatusCode.OK, await classificar.Content.ReadAsStringAsync());
            var classificada = (await classificar.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
            classificada.Natureza.Should().Be("Produto");
            classificada.Origem.Should().Be(produto.Origem);
            classificada.OriginalId.Should().Be(produto.OriginalId);
            classificada.Sha256.Should().Be(produto.Sha256);
        }
        using var atraso = await client.PutAsJsonAsync($"/api/acervo/imagens/{produto.Id}/natureza", new { natureza = "Comunicacao", naturezaEsperada = "NaoClassificada" });
        atraso.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var invalida = await client.PutAsJsonAsync($"/api/acervo/imagens/{produto.Id}/natureza", new { natureza = "Inventada", naturezaEsperada = "Produto" });
        invalida.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetByteArrayAsync($"/api/acervo/imagens/{produto.Id}/arquivo")).Should().Equal(PngCoberturaFuncional(1200, 1200, SKColors.Blue));
        using var aprovar = await client.PostAsync($"/api/acervo/imagens/{produto.Id}/aprovar", null);
        aprovar.StatusCode.Should().Be(HttpStatusCode.OK);
        using var uso = await client.PutAsJsonAsync($"/api/acervo/imagens/{produto.Id}/uso", new DadosUso("site"));
        uso.StatusCode.Should().Be(HttpStatusCode.OK);
        using var filtrosVazios = await client.GetAsync("/api/acervo/imagens?busca=&origem=&formato=&natureza=&canal=");
        filtrosVazios.StatusCode.Should().Be(HttpStatusCode.OK, await filtrosVazios.Content.ReadAsStringAsync());
        (await filtrosVazios.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>())!.Data.Should().HaveCount(2);
        foreach (var consulta in new[] { $"itemId={item}", "busca=Produto&origem=Fotografia&formato=1%3A1&natureza=Produto&canal=site" })
        {
            using var resposta = await client.GetAsync("/api/acervo/imagens?" + consulta);
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
            (await resposta.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>())!.Data.Should().ContainSingle().Which.Id.Should().Be(produto.Id);
        }
        foreach (var consulta in new[] { $"itemId={outroItem}", "origem=IA&natureza=Comunicacao&formato=4%3A5" })
        {
            using var resposta = await client.GetAsync("/api/acervo/imagens?" + consulta);
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
            (await resposta.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>())!.Data.Should().ContainSingle().Which.Id.Should().Be(comunicacao.Id);
        }
        var vazio = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>($"/api/acervo/imagens?itemId={item}&natureza=Comunicacao"))!.Data;
        vazio.Should().BeEmpty();
        var fotos = await LerCoberturaFuncionalAsync(client, item);
        fotos.Should().OnlyContain(x => x.Total == 1 && x.Fotografias == 1 && x.Geradas == 0 && x.Comunicacoes == 0);
        var pecas = await LerCoberturaFuncionalAsync(client, outroItem);
        pecas.Should().OnlyContain(x => x.Total == 1 && x.Fotografias == 0 && x.Geradas == 0 && x.Comunicacoes == 1);
        var empresaAlheia = Guid.NewGuid();
        var (_, itemAlheio) = await SeedAsync(factory, empresaAlheia, "acervo-fila-alheia");
        var agora = DateTime.UtcNow;
        var antiga = new AcervoSolicitacao { EmpresaId = empresa, UsuarioId = UsuarioTesteId, ItemId = item, Formato = "1:1", Brief = "Pendência antiga preservada", CriadoEm = agora.AddYears(-1) };
        var concluidas = Enumerable.Range(0, 200).Select(n => new AcervoSolicitacao {
            EmpresaId = empresa, UsuarioId = UsuarioTesteId, ItemId = item, Formato = "1:1", Brief = "Produção concluída", CriadoEm = agora.AddMinutes(-n), ConcluidoEm = agora }).ToArray();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            db.Set<AcervoSolicitacao>().Add(antiga);
            db.Set<AcervoSolicitacao>().AddRange(concluidas);
            db.Set<AcervoSolicitacao>().AddRange(
                new AcervoSolicitacao { EmpresaId = empresaAlheia, UsuarioId = UsuarioTesteId, ItemId = itemAlheio, Formato = "1:1", Brief = "Pendência de outra empresa", CriadoEm = agora.AddYears(-1) },
                new AcervoSolicitacao { EmpresaId = empresaAlheia, UsuarioId = UsuarioTesteId, ItemId = itemAlheio, Formato = "1:1", Brief = "Concluída de outra empresa", CriadoEm = agora.AddMinutes(1), ConcluidoEm = agora });
            await db.SaveChangesAsync();
        }
        using var consultarFila = await client.GetAsync("/api/acervo/solicitacoes?incluirConcluidas=true");
        consultarFila.StatusCode.Should().Be(HttpStatusCode.OK, await consultarFila.Content.ReadAsStringAsync());
        var historico = (await consultarFila.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>())!.Data;
        historico.Should().HaveCount(201);
        historico.Should().OnlyContain(x => x.EmpresaId == empresa);
        historico.Where(x => x.ConcluidoEm is null).Should().ContainSingle().Which.Id.Should().Be(antiga.Id);
        historico.Select(x => x.Id).Should().BeEquivalentTo(concluidas.Select(x => x.Id).Append(antiga.Id));
    }

    [SkippableFact]
    public async Task Acervo_incremento_cobertura_exige_aprovacao_e_uso_explicito_por_canal()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-cobertura-canais");
        using var client = ClienteAcervoNegativo(factory, empresa);
        var imagem = await CriarImagemCoberturaFuncionalAsync(client, item, 1200, 1200, "1:1", SKColors.Red);
        imagem.Largura.Should().Be(1200);
        imagem.Altura.Should().Be(1200);
        using var semAprovacao = await client.PutAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/uso", new { canal = "whatsapp" });
        semAprovacao.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var antes = await LerCoberturaFuncionalAsync(client, item);
        antes.Should().HaveCount(4);
        antes.Should().OnlyContain(x => x.Total == 1 && x.Adequadas == 0);
        using var aprovar = await client.PostAsync($"/api/acervo/imagens/{imagem.Id}/aprovar", null);
        aprovar.StatusCode.Should().Be(HttpStatusCode.OK);
        for (var tentativa = 0; tentativa < 2; tentativa++)
        {
            using var confirmar = await client.PutAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/uso", new { canal = "whatsapp" });
            confirmar.StatusCode.Should().Be(HttpStatusCode.OK, await confirmar.Content.ReadAsStringAsync());
        }
        var depois = await LerCoberturaFuncionalAsync(client, item);
        depois.Single(x => x.Canal == "whatsapp").Adequadas.Should().Be(1);
        depois.Where(x => x.Canal != "whatsapp").Should().OnlyContain(x => x.Adequadas == 0);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var bypass = db.UseRowLevelSecurityBypass();
        (await db.Set<AcervoUso>().IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresa)).Should().Be(1);
        (await db.CardapioItens.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == item)).FotoUrl.Should().BeNull();
    }

    [SkippableFact]
    public async Task Acervo_incremento_uso_recusa_proporcao_falsa_baixa_resolucao_e_impressao_incompativel()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-cobertura-geometria");
        using var client = ClienteAcervoNegativo(factory, empresa);
        var pequena = await CriarImagemCoberturaFuncionalAsync(client, item, 32, 32, "1:1", SKColors.Blue);
        pequena.Largura.Should().Be(32);
        pequena.Altura.Should().Be(32);
        var proporcaoFalsa = await CriarImagemCoberturaFuncionalAsync(client, item, 1200, 1200, "4:5", SKColors.Green);
        var quadrada = await CriarImagemCoberturaFuncionalAsync(client, item, 1200, 1200, "1:1", SKColors.Orange);
        foreach (var imagem in new[] { pequena, proporcaoFalsa, quadrada })
        {
            using var aprovar = await client.PostAsync($"/api/acervo/imagens/{imagem.Id}/aprovar", null);
            aprovar.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        foreach (var canal in new[] { "site", "instagram", "whatsapp" })
        {
            using var insuficiente = await client.PutAsJsonAsync($"/api/acervo/imagens/{pequena.Id}/uso", new { canal });
            insuficiente.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        using var falsa = await client.PutAsJsonAsync($"/api/acervo/imagens/{proporcaoFalsa.Id}/uso", new { canal = "instagram" });
        falsa.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        foreach (var medida in new[] { (Largura: (decimal?)null, Altura: (decimal?)null), (0m, 100m), (-1m, 100m), (2001m, 2001m), (102m, 102m), (100m, 50m) })
        {
            using var invalida = await client.PutAsJsonAsync($"/api/acervo/imagens/{quadrada.Id}/uso", new { canal = "impressao", larguraMm = medida.Largura, alturaMm = medida.Altura });
            invalida.StatusCode.Should().Be(HttpStatusCode.BadRequest, await invalida.Content.ReadAsStringAsync());
        }
        using var correta = await client.PutAsJsonAsync($"/api/acervo/imagens/{quadrada.Id}/uso", new { canal = "impressao", larguraMm = 100m, alturaMm = 100m });
        correta.StatusCode.Should().Be(HttpStatusCode.OK, await correta.Content.ReadAsStringAsync());
        var cobertura = await LerCoberturaFuncionalAsync(client, item);
        cobertura.Single(x => x.Canal == "impressao").Adequadas.Should().Be(1);
        cobertura.Where(x => x.Canal != "impressao").Should().OnlyContain(x => x.Adequadas == 0);
    }

    [SkippableFact]
    public async Task Acervo_incremento_briefing_preserva_versoes_e_recusa_reexecucao_divergente()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-briefing-versoes");
        using var client = ClienteAcervoNegativo(factory, empresa);
        var referencia = await PrepararImagemAcervoNegativoAsync(client, item);
        var dados = BriefingFuncional(item, referencia.Id);
        var original = await CriarBriefingFuncionalAsync(client, dados);
        var replay = await CriarBriefingFuncionalAsync(client, dados);
        replay.Id.Should().Be(original.Id);
        var mesmoConteudo = await CriarBriefingFuncionalAsync(client, dados with { OperacaoId = Guid.NewGuid() });
        mesmoConteudo.Id.Should().Be(original.Id);
        using var divergente = await client.PostAsJsonAsync("/api/acervo/briefings", dados with { Prompt = "Outro prompt" });
        divergente.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var versao = await CriarBriefingFuncionalAsync(client, dados with { OperacaoId = Guid.NewGuid(), Prompt = "Mesmo produto real, novo enquadramento", AnteriorId = original.Id });
        versao.Id.Should().NotBe(original.Id);
        versao.AnteriorId.Should().Be(original.Id);
        versao.RaizId.Should().Be(original.RaizId);
        versao.Versao.Should().Be(original.Versao + 1);
        using var obsoleto = await client.PostAsJsonAsync("/api/acervo/briefings", dados with { OperacaoId = Guid.NewGuid(), Prompt = "Edição de uma versão desatualizada", AnteriorId = original.Id });
        obsoleto.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var lista = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoBriefing[]>>("/api/acervo/briefings"))!.Data;
        lista.Should().HaveCount(2);
        lista.Single(x => x.Id == original.Id).Prompt.Should().Be(dados.Prompt);
        lista.Single(x => x.Id == original.Id).AnteriorId.Should().BeNull();
        lista.Single(x => x.Id == versao.Id).Prompt.Should().Be(versao.Prompt);
    }

    [SkippableFact]
    public async Task Acervo_incremento_fila_deduplica_o_briefing_sob_concorrencia_e_retorno_perdido()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-briefing-fila");
        using var client = ClienteAcervoNegativo(factory, empresa);
        var referencia = await PrepararImagemAcervoNegativoAsync(client, item);
        var briefing = await CriarBriefingFuncionalAsync(client, BriefingFuncional(item, referencia.Id));
        var operacoes = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var respostas = await Task.WhenAll(operacoes.Select(operacaoId => client.PostAsJsonAsync($"/api/acervo/briefings/{briefing.Id}/solicitacoes", new { operacaoId })));
        Guid resultado;
        try
        {
            foreach (var resposta in respostas) resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
            var pedidos = await Task.WhenAll(respostas.Select(x => x.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoSolicitacao>>()));
            pedidos.Select(x => x!.Data.Id).Distinct().Should().ContainSingle();
            resultado = pedidos[0]!.Data.Id;
        }
        finally { foreach (var resposta in respostas) resposta.Dispose(); }
        using var retomada = await client.PostAsJsonAsync($"/api/acervo/briefings/{briefing.Id}/solicitacoes", new { operacaoId = operacoes[0] });
        retomada.StatusCode.Should().Be(HttpStatusCode.OK);
        (await retomada.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoSolicitacao>>())!.Data.Id.Should().Be(resultado);
        var fila = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>("/api/acervo/solicitacoes"))!.Data;
        fila.Should().ContainSingle().Which.BriefingId.Should().Be(briefing.Id);
        fila[0].Formato.Should().Be(briefing.Formato);
        var cobertura = await LerCoberturaFuncionalAsync(client, item);
        cobertura.Single(x => x.Canal == briefing.Canal).Pendentes.Should().Be(1);
        cobertura.Where(x => x.Canal != briefing.Canal).Should().OnlyContain(x => x.Pendentes == 0);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Acervo_incremento_briefing_retoma_interrupcao_e_confirmacao_de_commit_perdida(bool depoisDoCommit)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var injetar = 1;
        await using var factory = CriarFactory().WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddScoped<IUnitOfWork>(sp => new CommitBriefingFuncional(sp.GetRequiredService<EasyStockDbContext>(), depoisDoCommit, () => Interlocked.Exchange(ref injetar, 0) == 1))));
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-briefing-interrupcao");
        using var client = ClienteAcervoNegativo(factory, empresa);
        var referencia = await PrepararImagemAcervoNegativoAsync(client, item);
        var dados = BriefingFuncional(item, referencia.Id);
        using var interrompida = await client.PostAsJsonAsync("/api/acervo/briefings", dados);
        interrompida.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var antes = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoBriefing[]>>("/api/acervo/briefings"))!.Data;
        antes.Length.Should().Be(depoisDoCommit ? 1 : 0);
        var retomada = await CriarBriefingFuncionalAsync(client, dados);
        retomada.Id.Should().Be(dados.OperacaoId);
        var lista = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoBriefing[]>>("/api/acervo/briefings"))!.Data;
        lista.Should().ContainSingle().Which.Id.Should().Be(dados.OperacaoId);
    }

    [SkippableFact]
    public async Task Acervo_incremento_resultado_atrasado_conserva_snapshot_original_e_exige_aprovacao_da_nova_versao()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-briefing-resposta-atrasada");
        using var client = ClienteAcervoNegativo(factory, empresa);
        var referencia = await CriarImagemCoberturaFuncionalAsync(client, item, 1200, 1200, "1:1", SKColors.Blue);
        using var aprovar = await client.PostAsync($"/api/acervo/imagens/{referencia.Id}/aprovar", null);
        aprovar.StatusCode.Should().Be(HttpStatusCode.OK);
        var entrada1 = BriefingFuncional(item, referencia.Id);
        var briefing1 = await CriarBriefingFuncionalAsync(client, entrada1);
        var briefing2 = await CriarBriefingFuncionalAsync(client, entrada1 with { OperacaoId = Guid.NewGuid(), AnteriorId = briefing1.Id, Formato = "4:5", Prompt = "Novo enquadramento vertical do mesmo produto real" });
        var pedido1 = await EnfileirarBriefingFuncionalAsync(client, briefing1.Id);
        var pedido2 = await EnfileirarBriefingFuncionalAsync(client, briefing2.Id);
        var errada = new DadosImagem("Prompt da versão errada", "IA", "1:1", [item], pedido2.Brief, pedido1.ReferenciasJson, referencia.Id, pedido1.Id);
        using var recusar = await EnviarBytesAcervoNegativoAsync(client, errada, PngCoberturaFuncional(1200, 1200, SKColors.Red));
        recusar.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var referenciasDivergentes = await EnviarBytesAcervoNegativoAsync(client, errada with { Prompt = pedido1.Brief, Referencias = JsonSerializer.Serialize(new[] { Guid.NewGuid() }) }, PngCoberturaFuncional(1200, 1200, SKColors.Red));
        referenciasDivergentes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var dados2 = new DadosImagem("Resultado da segunda versão", "IA", "4:5", [item], pedido2.Brief, pedido2.ReferenciasJson, referencia.Id, pedido2.Id);
        using var dimensoesDivergentes = await EnviarBytesAcervoNegativoAsync(client, dados2, PngCoberturaFuncional(1200, 1200, SKColors.Orange));
        dimensoesDivergentes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var resultado2 = await EnviarBytesAcervoNegativoAsync(client, dados2, PngCoberturaFuncional(1200, 1500, SKColors.Orange));
        resultado2.StatusCode.Should().Be(HttpStatusCode.OK, await resultado2.Content.ReadAsStringAsync());
        var imagem2 = (await resultado2.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        var dados1 = new DadosImagem("Resultado atrasado da primeira versão", "IA", "1:1", [item], pedido1.Brief, pedido1.ReferenciasJson, referencia.Id, pedido1.Id);
        var bytes1 = PngCoberturaFuncional(1200, 1200, SKColors.Red);
        using var resultado1 = await EnviarBytesAcervoNegativoAsync(client, dados1, bytes1);
        resultado1.StatusCode.Should().Be(HttpStatusCode.OK, await resultado1.Content.ReadAsStringAsync());
        var imagem1 = (await resultado1.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        imagem1.SolicitacaoId.Should().Be(pedido1.Id);
        imagem2.SolicitacaoId.Should().Be(pedido2.Id);
        imagem1.Prompt.Should().Be(pedido1.Brief);
        imagem2.Prompt.Should().Be(pedido2.Brief);
        imagem1.AprovadoEm.Should().BeNull();
        imagem2.AprovadoEm.Should().BeNull();
        (await client.GetByteArrayAsync($"/api/acervo/imagens/{referencia.Id}/arquivo")).Should().Equal(PngCoberturaFuncional(1200, 1200, SKColors.Blue));
        (await client.GetByteArrayAsync($"/api/acervo/imagens/{imagem1.Id}/arquivo")).Should().Equal(bytes1);
        (await EnfileirarBriefingFuncionalAsync(client, briefing1.Id)).Id.Should().Be(pedido1.Id);
        (await EnfileirarBriefingFuncionalAsync(client, briefing2.Id)).Id.Should().Be(pedido2.Id);
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>("/api/acervo/solicitacoes"))!.Data.Should().BeEmpty();
        using var publicar = await client.PostAsJsonAsync($"/api/acervo/imagens/{imagem2.Id}/publicar", new PublicarImagem(Guid.NewGuid(), item, false, null));
        publicar.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static DadosBriefing BriefingFuncional(Guid item, Guid referencia) => new(Guid.NewGuid(), item, "instagram", "Apresentar o produto", "1:1", [referencia], "Não inventar ingredientes, porções ou embalagem", "Fotografar o produto real no enquadramento aprovado");
    private sealed record CoberturaFuncionalLinha(Guid ItemId, string Canal, int Total, int Fotografias, int Geradas, int Comunicacoes, int Adequadas, int Pendentes);

    private static async Task<CoberturaFuncionalLinha[]> LerCoberturaFuncionalAsync(HttpClient client, Guid item) =>
        (await client.GetFromJsonAsync<AcervoEnvelope<CoberturaFuncionalLinha[]>>("/api/acervo/cobertura"))!.Data.Where(x => x.ItemId == item).ToArray();

    private static async Task<AcervoBriefing> CriarBriefingFuncionalAsync(HttpClient client, DadosBriefing dados)
    {
        using var resposta = await client.PostAsJsonAsync("/api/acervo/briefings", dados);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        return (await resposta.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoBriefing>>())!.Data;
    }

    private static async Task<AcervoSolicitacao> EnfileirarBriefingFuncionalAsync(HttpClient client, Guid briefing)
    {
        using var resposta = await client.PostAsJsonAsync($"/api/acervo/briefings/{briefing}/solicitacoes", new { operacaoId = Guid.NewGuid() });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        return (await resposta.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoSolicitacao>>())!.Data;
    }

    private static async Task<AcervoImagem> CriarImagemCoberturaFuncionalAsync(HttpClient client, Guid item, int largura, int altura, string formato, SKColor cor)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(PngCoberturaFuncional(largura, altura, cor));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "original.png");
        form.Add(new StringContent(JsonSerializer.Serialize(new { titulo = "Produto real", origem = "Fotografia", formato, itens = new[] { item }, natureza = "Produto", largura = 1, altura = 1 })), "dados");
        using var resposta = await client.PostAsync("/api/acervo/imagens", form);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        return (await resposta.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
    }

    private static byte[] PngCoberturaFuncional(int largura, int altura, SKColor cor)
    {
        using var bitmap = new SKBitmap(largura, altura);
        bitmap.Erase(cor);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private sealed class CommitBriefingFuncional(EasyStockDbContext db, bool depois, Func<bool> injetar) : IUnitOfWork
    {
        private bool _gravouBriefing;
        public async Task<int> CommitAsync()
        {
            var alvo = db.ChangeTracker.Entries<AcervoBriefing>().Any(x => x.State == EntityState.Added);
            if (alvo && !depois && injetar()) throw new IOException("Interrupção controlada antes de gravar o briefing.");
            var resultado = await db.CommitAsync();
            _gravouBriefing |= alvo;
            if (db.Database.CurrentTransaction is null) ConfirmacaoPerdida();
            return resultado;
        }
        private void ConfirmacaoPerdida()
        {
            if (_gravouBriefing && depois && injetar()) throw new IOException("Resposta controlada perdida depois do commit confirmado.");
        }
        public bool EhViolacaoDeUnicidade(Exception ex) => db.EhViolacaoDeUnicidade(ex);
        public void DescartarAlteracoesPendentes() => db.DescartarAlteracoesPendentes();
        public Task<IDbTransactionScope> BeginTransactionAsync(CancellationToken ct = default) => db.BeginTransactionAsync(ct);
        public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default) { await db.ExecuteInTransactionAsync(action, ct); ConfirmacaoPerdida(); }
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default) { var resultado = await db.ExecuteInTransactionAsync(action, ct); ConfirmacaoPerdida(); return resultado; }
        public async Task<T> ExecuteInTransactionSemRetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default) { var resultado = await db.ExecuteInTransactionSemRetryAsync(action, ct); ConfirmacaoPerdida(); return resultado; }
    }
}
