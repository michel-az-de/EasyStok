using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EasyStock.Domain.Entities.Acervo;
using FluentAssertions;
using Npgsql;
using SkiaSharp;

namespace EasyStock.Api.IntegrationTests.Storefront.Uploads;

public sealed partial class CardapioItemFotoUploadE2ETests
{
    [SkippableFact]
    public async Task Acervo_cobertura_seguranca_exige_nivel_modulo_e_empresa_em_todas_as_rotas()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "cobertura-permissoes");
        using var client = factory.CreateClient();
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
            foreach (var request in PedidosCoberturaSeguranca(item))
            {
                using (request)
                using (var resposta = await client.SendAsync(request))
                    resposta.StatusCode.Should().Be(esperado, $"{request.Method} {request.RequestUri} exige nível, módulo e empresa");
            }
        }
    }

    [SkippableFact]
    public async Task Acervo_cobertura_seguranca_query_body_e_ids_alheios_nao_contornam_tenant()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid(); var outra = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "cobertura-tenant-a");
        var (_, itemOutra) = await SeedAsync(factory, outra, "cobertura-tenant-b");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        var imagem = await AprovarReferenciaCoberturaAsync(client, item);
        using var criado = await client.PostAsJsonAsync("/api/acervo/briefings", BriefingCoberturaSeguranca(item, imagem.Id));
        criado.StatusCode.Should().Be(HttpStatusCode.OK, await criado.Content.ReadAsStringAsync());
        var briefing = (await criado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", outra));
        var referenciaOutra = await AprovarReferenciaCoberturaAsync(client, itemOutra);
        using (var cobertura = await client.GetAsync($"/api/acervo/cobertura?empresaId={empresa}"))
        {
            cobertura.StatusCode.Should().Be(HttpStatusCode.OK);
            var texto = await cobertura.Content.ReadAsStringAsync();
            texto.Should().Contain(itemOutra.ToString()).And.NotContain(item.ToString()).And.NotContain(imagem.Id.ToString());
        }
        using (var lista = await client.GetAsync($"/api/acervo/briefings?empresaId={empresa}"))
        {
            lista.StatusCode.Should().Be(HttpStatusCode.OK);
            (await lista.Content.ReadAsStringAsync()).Should().NotContain(briefing.ToString()).And.NotContain(imagem.Id.ToString());
        }
        using (var fila = await client.PostAsJsonAsync($"/api/acervo/briefings/{briefing}/solicitacoes?empresaId={empresa}", new { operacaoId = Guid.NewGuid(), empresaId = empresa }))
            fila.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var uso = await client.PutAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/uso?empresaId={empresa}", new { canal = "site", empresaId = empresa }))
            uso.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreach (var (dados, esperado) in new[]
        {
            (BriefingCoberturaSeguranca(item, referenciaOutra.Id), HttpStatusCode.BadRequest),
            (BriefingCoberturaSeguranca(itemOutra, imagem.Id), HttpStatusCode.NotFound),
            (BriefingCoberturaSeguranca(itemOutra, referenciaOutra.Id, briefing), HttpStatusCode.NotFound)
        })
        {
            dados["empresaId"] = empresa;
            using var resposta = await client.PostAsJsonAsync($"/api/acervo/briefings?empresaId={empresa}", dados);
            resposta.StatusCode.Should().Be(esperado, "empresa deve vir do JWT em referências, produto e versão anterior");
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        var preservados = await client.GetFromJsonAsync<JsonElement>("/api/acervo/briefings");
        preservados.GetProperty("data").GetArrayLength().Should().Be(1);
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>("/api/acervo/solicitacoes"))!.Data.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Acervo_cobertura_seguranca_payload_invalido_e_referencia_sem_aprovacao_nao_persistem()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "cobertura-payload");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        using var upload = await EnviarAcervoAsync(client, item);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        using (var referencia = await client.PostAsJsonAsync("/api/acervo/briefings", BriefingCoberturaSeguranca(item, imagem.Id)))
            referencia.StatusCode.Should().Be(HttpStatusCode.BadRequest, "referências de produção exigem aprovação explícita");
        using (var aprovar = await client.PostAsync($"/api/acervo/imagens/{imagem.Id}/aprovar", null))
            aprovar.StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var (campo, valor) in new (string, object?)[]
        {
            ("operacaoId", Guid.Empty), ("itemId", Guid.Empty), ("canal", "sms"),
            ("finalidade", ""), ("formato", "formato-inexistente"), ("prompt", ""),
            ("referencias", null), ("referencias", new[] { Guid.Empty }),
            ("prompt", "Preserve\0o produto"), ("restricoes", "Nao altere\0porcao")
        })
        {
            var dados = BriefingCoberturaSeguranca(item, imagem.Id);
            dados[campo] = valor;
            using var resposta = await client.PostAsJsonAsync("/api/acervo/briefings", dados);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"campo inválido: {campo}");
        }
        foreach (var (metodo, rota) in new[]
        {
            (HttpMethod.Post, "/api/acervo/briefings"),
            (HttpMethod.Post, $"/api/acervo/briefings/{Guid.NewGuid()}/solicitacoes"),
            (HttpMethod.Put, $"/api/acervo/imagens/{imagem.Id}/uso")
        })
        foreach (var corpo in new[] { "null", "[]", "{", "" })
        {
            using var request = new HttpRequestMessage(metodo, rota) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
            using var resposta = await client.SendAsync(request);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{metodo} {rota} não deve produzir 500 para {corpo}");
        }
        var lista = await client.GetFromJsonAsync<JsonElement>("/api/acervo/briefings");
        lista.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [SkippableFact]
    public async Task Acervo_cobertura_seguranca_operacao_alheia_colidente_nao_expoe_nem_modifica_conteudo()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid(); var outra = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "cobertura-colisao-a");
        var (_, itemOutra) = await SeedAsync(factory, outra, "cobertura-colisao-b");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        var imagem = await AprovarReferenciaCoberturaAsync(client, item);
        var dados = BriefingCoberturaSeguranca(item, imagem.Id);
        var marcador = "Conteudo privado da empresa A " + Guid.NewGuid();
        dados["prompt"] = marcador;
        var operacaoBriefing = (Guid)dados["operacaoId"]!;
        using (var criado = await client.PostAsJsonAsync("/api/acervo/briefings", dados))
            criado.StatusCode.Should().Be(HttpStatusCode.OK, await criado.Content.ReadAsStringAsync());
        var operacaoFila = Guid.NewGuid();
        using (var fila = await client.PostAsJsonAsync($"/api/acervo/briefings/{operacaoBriefing}/solicitacoes", new { operacaoId = operacaoFila }))
            fila.StatusCode.Should().Be(HttpStatusCode.OK, await fila.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", outra));
        var referenciaOutra = await AprovarReferenciaCoberturaAsync(client, itemOutra);
        var tentativa = BriefingCoberturaSeguranca(itemOutra, referenciaOutra.Id);
        tentativa["operacaoId"] = operacaoBriefing;
        using (var colidente = await client.PostAsJsonAsync("/api/acervo/briefings", tentativa))
        {
            colidente.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
            (await colidente.Content.ReadAsStringAsync()).Should().NotContain(marcador).And.NotContain(imagem.Id.ToString()).And.NotContain(empresa.ToString());
        }
        tentativa["operacaoId"] = Guid.NewGuid();
        using var proprio = await client.PostAsJsonAsync("/api/acervo/briefings", tentativa);
        proprio.StatusCode.Should().Be(HttpStatusCode.OK, await proprio.Content.ReadAsStringAsync());
        var briefingProprio = (await proprio.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();
        using (var colidente = await client.PostAsJsonAsync($"/api/acervo/briefings/{briefingProprio}/solicitacoes", new { operacaoId = operacaoFila }))
        {
            colidente.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
            (await colidente.Content.ReadAsStringAsync()).Should().NotContain(marcador).And.NotContain(imagem.Id.ToString()).And.NotContain(empresa.ToString());
        }
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>("/api/acervo/solicitacoes"))!.Data.Should().BeEmpty();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        var preservado = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>("/api/acervo/solicitacoes"))!.Data.Should().ContainSingle().Which;
        preservado.Id.Should().Be(operacaoFila);
        preservado.Brief.Should().Contain(marcador);
    }

    [SkippableFact]
    public async Task Acervo_cobertura_seguranca_classificacao_exige_permissao_tenant_e_payload_valido_sem_aprovar()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid(); var outra = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "classificacao-seguranca-a");
        await SeedAsync(factory, outra, "classificacao-seguranca-b");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        using var upload = await EnviarAcervoAsync(client, item);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        var rota = $"/api/acervo/imagens/{imagem.Id}/natureza";
        foreach (var (token, esperado) in new (string? Token, HttpStatusCode Esperado)[]
        {
            (null, HttpStatusCode.Unauthorized),
            (GerarJwt("Operador", empresa, "AcessarModuloCardapio"), HttpStatusCode.Forbidden),
            (GerarJwt("Gerente", empresa, "AcessarModuloCaixa"), HttpStatusCode.Forbidden),
            (GerarJwt("Admin", empresa, "AcessarModuloCaixa"), HttpStatusCode.Forbidden),
            (GerarJwt("Admin"), HttpStatusCode.Forbidden),
            (GerarJwt("Gerente", outra, "AcessarModuloCardapio"), HttpStatusCode.NotFound)
        })
        {
            client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
            using var resposta = await client.PutAsJsonAsync(rota + "?empresaId=" + empresa, new
            {
                natureza = "Comunicacao", naturezaEsperada = "Produto", empresaId = empresa
            });
            resposta.StatusCode.Should().Be(esperado, "nível, módulo e tenant são barreiras independentes na classificação");
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Gerente", empresa, "AcessarModuloCardapio"));
        foreach (var corpo in new[]
        {
            "null", "[]", "{", "", "{\"natureza\":\"Canva\",\"naturezaEsperada\":\"Produto\"}",
            "{\"natureza\":\"NaoClassificada\",\"naturezaEsperada\":\"Produto\"}",
            "{\"natureza\":null,\"naturezaEsperada\":\"Produto\"}"
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, rota) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
            using var resposta = await client.SendAsync(request);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, "classificação inválida não deve produzir erro500 ou modificar o original");
        }
        using var classificar = await client.PutAsJsonAsync(rota, new { natureza = "Comunicacao", naturezaEsperada = "Produto" });
        classificar.StatusCode.Should().Be(HttpStatusCode.OK, await classificar.Content.ReadAsStringAsync());
        var atual = (await classificar.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        atual.Natureza.Should().Be("Comunicacao");
        atual.AprovadoEm.Should().BeNull();
        atual.AprovadoPorId.Should().BeNull();
        atual.Origem.Should().Be(imagem.Origem);
        atual.OriginalId.Should().Be(imagem.OriginalId);
        atual.Sha256.Should().Be(imagem.Sha256);
        (await client.GetByteArrayAsync($"/api/acervo/imagens/{imagem.Id}/arquivo")).Should().Equal(PngValido);
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data.Should().ContainSingle();
    }

    [SkippableFact]
    public async Task Acervo_cobertura_seguranca_pixels_e_aprovacao_do_body_nao_liberam_uso_nem_impressao()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "cobertura-medidas");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        using var upload = await EnviarPixelsCoberturaSegurancaAsync(client, item);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var id = imagem.GetProperty("id").GetGuid();
        imagem.GetProperty("largura").GetInt32().Should().Be(1200, "pixels vêm do codec e ignoram os 8.000 pixels enviados no body");
        imagem.GetProperty("altura").GetInt32().Should().Be(1200);
        imagem.GetProperty("aprovadoEm").ValueKind.Should().Be(JsonValueKind.Null, "upload não aceita aprovação enviada pelo cliente");
        using (var uso = await client.PutAsJsonAsync($"/api/acervo/imagens/{id}/uso", new { canal = "impressao", larguraMm = 100, alturaMm = 100, aprovadoEm = DateTime.UtcNow }))
            uso.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var aprovar = await client.PostAsync($"/api/acervo/imagens/{id}/aprovar", null))
            aprovar.StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var medidas in new (decimal? Largura, decimal? Altura)[]
        {
            (null, null), (null, 100), (0, 100), (-1, 100), (100, -1),
            (2001, 100), (100, 2001), (101.7m, 100), (100, 101.7m)
        })
        {
            using var uso = await client.PutAsJsonAsync($"/api/acervo/imagens/{id}/uso", new
            {
                canal = "impressao", larguraMm = medidas.Largura, alturaMm = medidas.Altura,
                largura = 8000, altura = 8000, aprovadoEm = DateTime.UtcNow
            });
            uso.StatusCode.Should().Be(HttpStatusCode.BadRequest, "300 dpi são exigidos nos dois lados e medidas precisam ser válidas");
        }
        using (var canal = await client.PutAsJsonAsync($"/api/acervo/imagens/{id}/uso", new { canal = "sms" }))
            canal.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var valido = await client.PutAsJsonAsync($"/api/acervo/imagens/{id}/uso", new { canal = "impressao", larguraMm = 100, alturaMm = 100 }))
            valido.StatusCode.Should().Be(HttpStatusCode.OK, await valido.Content.ReadAsStringAsync());
        using (var publicar = await client.PostAsJsonAsync($"/api/acervo/imagens/{id}/publicar", new
        {
            operacaoId = Guid.NewGuid(), itemId = item, galeria = false, capaEsperada = (string?)null
        }))
            publicar.StatusCode.Should().Be(HttpStatusCode.OK, await publicar.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Acervo_cobertura_seguranca_conclusao_exige_snapshot_e_proporcao_sem_aprovar_o_resultado()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "cobertura-conclusao");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        var referencia = await AprovarReferenciaCoberturaAsync(client, item);
        using var novo = await client.PostAsJsonAsync("/api/acervo/briefings", BriefingCoberturaSeguranca(item, referencia.Id));
        novo.StatusCode.Should().Be(HttpStatusCode.OK, await novo.Content.ReadAsStringAsync());
        var briefing = (await novo.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();
        using var pedido = await client.PostAsJsonAsync($"/api/acervo/briefings/{briefing}/solicitacoes", new { operacaoId = Guid.NewGuid() });
        pedido.StatusCode.Should().Be(HttpStatusCode.OK, await pedido.Content.ReadAsStringAsync());
        var solicitacao = (await pedido.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoSolicitacao>>())!.Data;
        foreach (var (prompt, referencias, altura) in new[]
        {
            ("Prompt adulterado", solicitacao.ReferenciasJson, 1200),
            (solicitacao.Brief, "[]", 1200),
            (solicitacao.Brief, "referencias invalidas", 1200),
            (solicitacao.Brief, solicitacao.ReferenciasJson, 600)
        })
        {
            using var bitmap = new SKBitmap(1200, altura);
            bitmap.Erase(SKColors.Blue);
            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var form = FormularioNegativoAcervo(JsonSerializer.Serialize(new
            {
                titulo = "Resultado assistido", origem = "IA", formato = "1:1", itens = new[] { item }, natureza = "Produto",
                solicitacaoId = solicitacao.Id, prompt, referencias, largura = 1200, altura = 1200
            }), png.ToArray());
            using var resposta = await client.PostAsync("/api/acervo/imagens", form);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, "metadados adulterados não podem concluir um pedido nem substituir sua proporção real");
        }
        var pendentes = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>("/api/acervo/solicitacoes"))!.Data;
        pendentes.Should().ContainSingle().Which.Id.Should().Be(solicitacao.Id);
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data.Should().ContainSingle().Which.Id.Should().Be(referencia.Id);
        using var pixels = new SKBitmap(1200, 1200);
        pixels.Erase(SKColors.Blue);
        using var resultado = SKImage.FromBitmap(pixels);
        using var arquivo = resultado.Encode(SKEncodedImageFormat.Png, 100);
        using var valido = FormularioNegativoAcervo(JsonSerializer.Serialize(new
        {
            titulo = "Resultado assistido", origem = "IA", formato = "1:1", itens = new[] { item }, natureza = "Produto",
            solicitacaoId = solicitacao.Id, prompt = solicitacao.Brief, referencias = solicitacao.ReferenciasJson,
            aprovadoEm = DateTime.UtcNow, aprovadoPorId = UsuarioTesteId
        }), arquivo.ToArray());
        using var concluir = await client.PostAsync("/api/acervo/imagens", valido);
        concluir.StatusCode.Should().Be(HttpStatusCode.OK, await concluir.Content.ReadAsStringAsync());
        var final = (await concluir.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        final.AprovadoEm.Should().BeNull();
        final.AprovadoPorId.Should().BeNull();
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>("/api/acervo/solicitacoes"))!.Data.Should().BeEmpty();
        using (var repetir = await client.PostAsJsonAsync($"/api/acervo/briefings/{briefing}/solicitacoes", new { operacaoId = Guid.NewGuid() }))
        {
            repetir.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
            if (repetir.IsSuccessStatusCode)
                (await repetir.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoSolicitacao>>())!.Data.Id.Should().Be(solicitacao.Id, "outro ID de operação não deve produzir duas imagens para o mesmo briefing imutável");
        }
        (await client.GetFromJsonAsync<AcervoEnvelope<AcervoSolicitacao[]>>("/api/acervo/solicitacoes"))!.Data.Should().BeEmpty();
        using var publicar = await client.PostAsJsonAsync($"/api/acervo/imagens/{final.Id}/publicar", new
        {
            operacaoId = Guid.NewGuid(), itemId = item, galeria = false, capaEsperada = (string?)null
        });
        publicar.StatusCode.Should().Be(HttpStatusCode.BadRequest, "concluir produção não autoriza publicação");
    }

    [SkippableFact]
    public async Task Acervo_cobertura_seguranca_rls_briefings_e_usos_negam_leitura_e_escrita_alheias()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid(); var outra = Guid.NewGuid();
        using var client = factory.CreateClient();
        var imagensPorEmpresa = new Dictionary<Guid, Guid>();
        foreach (var tenant in new[] { empresa, outra })
        {
            var (_, item) = await SeedAsync(factory, tenant, "rls-" + tenant.ToString("N"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", tenant));
            using var upload = await EnviarPixelsCoberturaSegurancaAsync(client, item);
            upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
            var imagem = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();
            imagensPorEmpresa.Add(tenant, imagem);
            using (var aprovar = await client.PostAsync($"/api/acervo/imagens/{imagem}/aprovar", null))
                aprovar.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var briefing = await client.PostAsJsonAsync("/api/acervo/briefings", BriefingCoberturaSeguranca(item, imagem)))
                briefing.StatusCode.Should().Be(HttpStatusCode.OK, await briefing.Content.ReadAsStringAsync());
            using var uso = await client.PutAsJsonAsync($"/api/acervo/imagens/{imagem}/uso", new { canal = "impressao", larguraMm = 100, alturaMm = 100 });
            uso.StatusCode.Should().Be(HttpStatusCode.OK, await uso.Content.ReadAsStringAsync());
        }
        var usosDaOutra = (await client.GetFromJsonAsync<JsonElement>($"/api/acervo/usos?empresaId={empresa}")).GetProperty("data");
        usosDaOutra.GetArrayLength().Should().Be(1);
        usosDaOutra[0].GetProperty("empresaId").GetGuid().Should().Be(outra);
        usosDaOutra[0].GetProperty("imagemId").GetGuid().Should().Be(imagensPorEmpresa[outra]);
        var role = "cobertura_rls_" + Guid.NewGuid().ToString("N");
        var senha = Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_pg!.GetConnectionString());
        await admin.OpenAsync();
        await using (var criar = new NpgsqlCommand($"""
            CREATE ROLE {role} LOGIN PASSWORD '{senha}' NOSUPERUSER NOBYPASSRLS;
            GRANT USAGE ON SCHEMA public TO {role};
            GRANT SELECT, INSERT, UPDATE, DELETE ON acervo_briefings, acervo_usos TO {role};
            """, admin))
            await criar.ExecuteNonQueryAsync();
        var conexao = new NpgsqlConnectionStringBuilder(_pg.GetConnectionString()) { Username = role, Password = senha, Pooling = false };
        await using var restrito = new NpgsqlConnection(conexao.ConnectionString);
        await restrito.OpenAsync();
        await using (var privilegios = new NpgsqlCommand("SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user", restrito))
            (await privilegios.ExecuteScalarAsync()).Should().Be(false);
        foreach (var tenant in new[] { empresa.ToString(), outra.ToString(), "" })
        {
            await using (var definir = new NpgsqlCommand("SELECT set_config('app.empresa_id', @tenant, false), set_config('app.bypass_rls', 'false', false)", restrito))
            {
                definir.Parameters.AddWithValue("tenant", tenant);
                await definir.ExecuteNonQueryAsync();
            }
            foreach (var tabela in new[] { "acervo_briefings", "acervo_usos" })
            {
                await using var contar = new NpgsqlCommand($"SELECT count(*) FROM {tabela}", restrito);
                (await contar.ExecuteScalarAsync()).Should().Be(string.IsNullOrEmpty(tenant) ? 0L : 1L, "RLS precisa negar leitura sem tenant e isolar empresas");
                if (string.IsNullOrEmpty(tenant)) continue;
                var alheia = tenant == empresa.ToString() ? outra : empresa;
                foreach (var sql in new[]
                {
                    $"UPDATE {tabela} SET \"EmpresaId\" = \"EmpresaId\" WHERE \"EmpresaId\" = @empresa",
                    $"DELETE FROM {tabela} WHERE \"EmpresaId\" = @empresa"
                })
                {
                    await using var comando = new NpgsqlCommand(sql, restrito);
                    comando.Parameters.AddWithValue("empresa", alheia);
                    (await comando.ExecuteNonQueryAsync()).Should().Be(0);
                }
                await using var mover = new NpgsqlCommand($"UPDATE {tabela} SET \"EmpresaId\" = @outra", restrito);
                mover.Parameters.AddWithValue("outra", alheia);
                Func<Task> executar = async () => { await mover.ExecuteNonQueryAsync(); };
                (await executar.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
                await using var inserir = new NpgsqlCommand($"""
                    INSERT INTO {tabela}
                    SELECT (jsonb_populate_record(NULL::{tabela}, to_jsonb(t) ||
                        jsonb_build_object('Id', @id, 'EmpresaId', @empresa))).*
                    FROM {tabela} t LIMIT 1
                    """, restrito);
                inserir.Parameters.AddWithValue("id", Guid.NewGuid());
                inserir.Parameters.AddWithValue("empresa", alheia);
                Func<Task> inserirAlheio = async () => { await inserir.ExecuteNonQueryAsync(); };
                (await inserirAlheio.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            }
        }
    }

    private static async Task<HttpResponseMessage> EnviarPixelsCoberturaSegurancaAsync(HttpClient client, Guid item)
    {
        using var bitmap = new SKBitmap(1200, 1200);
        bitmap.Erase(SKColors.Orange);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var dados = JsonSerializer.Serialize(new
        {
            titulo = "Referencia fiel", origem = "Fotografia", formato = "1:1", itens = new[] { item }, natureza = "Produto",
            largura = 8000, altura = 8000, aprovadoEm = DateTime.UtcNow, aprovadoPorId = UsuarioTesteId
        });
        using var form = FormularioNegativoAcervo(dados, png.ToArray());
        return await client.PostAsync("/api/acervo/imagens", form);
    }

    private static Dictionary<string, object?> BriefingCoberturaSeguranca(Guid item, Guid referencia, Guid? anterior = null) => new()
    {
        ["operacaoId"] = Guid.NewGuid(), ["itemId"] = item, ["canal"] = "site",
        ["finalidade"] = "Foto fiel do produto no cardapio", ["formato"] = "1:1", ["referencias"] = new[] { referencia },
        ["restricoes"] = "Preservar ingredientes, porcao e embalagem", ["prompt"] = "Reenquadrar somente o produto da referencia aprovada", ["anteriorId"] = anterior
    };

    private static async Task<AcervoImagem> AprovarReferenciaCoberturaAsync(HttpClient client, Guid item)
    {
        using var upload = await EnviarAcervoAsync(client, item);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        using var aprovar = await client.PostAsync($"/api/acervo/imagens/{imagem.Id}/aprovar", null);
        aprovar.StatusCode.Should().Be(HttpStatusCode.OK, await aprovar.Content.ReadAsStringAsync());
        return imagem;
    }

    private static IEnumerable<HttpRequestMessage> PedidosCoberturaSeguranca(Guid item)
    {
        yield return new(HttpMethod.Get, "/api/acervo/cobertura");
        yield return new(HttpMethod.Get, "/api/acervo/briefings");
        yield return new(HttpMethod.Get, "/api/acervo/usos");
        yield return new(HttpMethod.Post, "/api/acervo/briefings") { Content = JsonContent.Create(BriefingCoberturaSeguranca(item, Guid.NewGuid())) };
        yield return new(HttpMethod.Post, $"/api/acervo/briefings/{Guid.NewGuid()}/solicitacoes") { Content = JsonContent.Create(new { operacaoId = Guid.NewGuid() }) };
        yield return new(HttpMethod.Put, $"/api/acervo/imagens/{Guid.NewGuid()}/uso") { Content = JsonContent.Create(new { canal = "site" }) };
    }
}
