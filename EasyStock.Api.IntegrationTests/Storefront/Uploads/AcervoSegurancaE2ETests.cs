using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Domain.Entities.Acervo;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SkiaSharp;

namespace EasyStock.Api.IntegrationTests.Storefront.Uploads;

public sealed partial class CardapioItemFotoUploadE2ETests
{
    [SkippableFact]
    public async Task Acervo_original_privado_nao_sai_por_caminhos_publicos_normalizados()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-caminhos");
        var outra = Guid.NewGuid();
        await SeedAsync(factory, outra, "acervo-caminhos-outra");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        using var upload = await EnviarAcervoAsync(client, item);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imagem = (await upload.Content.ReadFromJsonAsync<AcervoEnvelope<AcervoImagem>>())!.Data;
        using var autenticado = await client.GetAsync($"/api/acervo/imagens/{imagem.Id}/arquivo");
        autenticado.StatusCode.Should().Be(HttpStatusCode.OK);
        (await autenticado.Content.ReadAsByteArrayAsync()).Should().Equal(PngValido);
        autenticado.Headers.CacheControl!.NoStore.Should().BeTrue();
        autenticado.Headers.CacheControl.Private.Should().BeTrue();

        StoredFileResult publico;
        using (var scope = factory.Services.CreateScope())
        {
            publico = await scope.ServiceProvider.GetRequiredService<IFileStorage>().UploadAsync(
                new FileUploadRequest($"cardapios/{empresa}/{item}", "controle.png", "image/png", PngValido));
        }
        client.DefaultRequestHeaders.Authorization = null;
        foreach (var caminho in new[]
        {
            $"/files/{imagem.StorageKey}",
            $"/files//{imagem.StorageKey}",
            $"/files///{imagem.StorageKey}",
            $"/files/{imagem.StorageKey.Replace("/", "//")}",
            $"/files/./{imagem.StorageKey}"
        })
        {
            foreach (var metodo in new[] { HttpMethod.Get, HttpMethod.Head })
            {
                using var request = new HttpRequestMessage(metodo, caminho);
                using var resposta = await client.SendAsync(request);
                resposta.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{metodo} {caminho} deve manter o original privado");
            }
        }
        (await client.GetByteArrayAsync(new Uri(publico.Url).PathAndQuery)).Should().Equal(PngValido);
        using var anonimo = await client.GetAsync($"/api/acervo/imagens/{imagem.Id}/arquivo");
        anonimo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", outra));
        using var outraEmpresa = await client.GetAsync($"/api/acervo/imagens/{imagem.Id}/miniatura");
        outraEmpresa.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Acervo_recusa_assinatura_sem_imagem_e_dimensao_acima_do_limite()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-imagem-invalida");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        using var bitmap = new SKBitmap(8193, 1);
        bitmap.Erase(SKColors.White);
        using var imagem = SKImage.FromBitmap(bitmap);
        using var grande = imagem.Encode(SKEncodedImageFormat.Png, 100);
        foreach (var bytes in new[] { PngValido[..8], grande.ToArray() })
        {
            using var form = new MultipartFormDataContent();
            var arquivo = new ByteArrayContent(bytes);
            arquivo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(arquivo, "file", "invalido.png");
            form.Add(new StringContent(JsonSerializer.Serialize(new
            {
                titulo = "Inválida", origem = "Fotografia", formato = "livre", itens = new[] { item }
            })), "dados");
            using var resposta = await client.PostAsync("/api/acervo/imagens", form);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, await resposta.Content.ReadAsStringAsync());
        }
        var lista = (await client.GetFromJsonAsync<AcervoEnvelope<AcervoImagem[]>>("/api/acervo/imagens"))!.Data;
        lista.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Acervo_publicacao_recusa_original_que_nao_pode_ser_decodificado()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-fallback");
        var bytes = PngValido[..8];
        var imagem = new AcervoImagem
        {
            EmpresaId = empresa, CriadoPorId = UsuarioTesteId, Titulo = "Original legado inválido",
            Origem = "Fotografia", Formato = "1:1", ContentType = "image/png", Tamanho = bytes.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), ItensJson = JsonSerializer.Serialize(new[] { item }),
            AprovadoEm = DateTime.UtcNow, AprovadoPorId = UsuarioTesteId
        };
        using (var scope = factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            var arquivo = await storage.UploadAsync(new FileUploadRequest($"acervo/{empresa}/{imagem.Id}", "original.png", "image/png", bytes, IsPublic: false));
            imagem.StorageKey = arquivo.StorageKey;
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            db.Add(imagem);
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresa));
        using var resposta = await client.PostAsJsonAsync($"/api/acervo/imagens/{imagem.Id}/publicar", new
        {
            operacaoId = Guid.NewGuid(), itemId = item, galeria = false, capaEsperada = (string?)null
        });
        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("preparar a imagem");
        using var verificacao = factory.Services.CreateScope();
        var banco = verificacao.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var escopo = banco.UseRowLevelSecurityBypass();
        (await banco.CardapioItens.SingleAsync(x => x.Id == item)).FotoUrl.Should().BeNull();
        (await banco.Set<AcervoPublicacao>().CountAsync(x => x.EmpresaId == empresa)).Should().Be(0);
    }

    [SkippableFact]
    public async Task Acervo_rls_isola_leitura_e_escrita_sem_superusuario_nem_bypass()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresa = Guid.NewGuid();
        var outra = Guid.NewGuid();
        var (_, item) = await SeedAsync(factory, empresa, "acervo-rls-a");
        var (_, itemOutra) = await SeedAsync(factory, outra, "acervo-rls-b");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            foreach (var (tenant, itemId) in new[] { (empresa, item), (outra, itemOutra) })
            {
                var imagem = new AcervoImagem { EmpresaId = tenant, CriadoPorId = UsuarioTesteId, Titulo = "RLS", Sha256 = new string('a', 64) };
                db.Add(imagem);
                db.Add(new AcervoSolicitacao { EmpresaId = tenant, UsuarioId = UsuarioTesteId, ItemId = itemId });
                db.Add(new AcervoPublicacao { Id = Guid.NewGuid(), EmpresaId = tenant, UsuarioId = UsuarioTesteId, ItemId = itemId, ImagemId = imagem.Id });
            }
            await db.SaveChangesAsync();
        }
        var role = "acervo_rls_" + Guid.NewGuid().ToString("N");
        var senha = Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_pg!.GetConnectionString());
        await admin.OpenAsync();
        await using (var criar = new NpgsqlCommand($"""
            CREATE ROLE {role} LOGIN PASSWORD '{senha}' NOSUPERUSER NOBYPASSRLS;
            GRANT USAGE ON SCHEMA public TO {role};
            GRANT SELECT, INSERT, UPDATE, DELETE ON acervo_imagens, acervo_solicitacoes, acervo_publicacoes TO {role};
            """, admin))
            await criar.ExecuteNonQueryAsync();
        var conexao = new NpgsqlConnectionStringBuilder(_pg.GetConnectionString()) { Username = role, Password = senha, Pooling = false };
        await using var restrito = new NpgsqlConnection(conexao.ConnectionString);
        await restrito.OpenAsync();
        await using (var privilegios = new NpgsqlCommand("SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user", restrito))
            ((bool)(await privilegios.ExecuteScalarAsync())!).Should().BeFalse();
        await DefinirTenantAsync(empresa.ToString());
        foreach (var tabela in new[] { "acervo_imagens", "acervo_solicitacoes", "acervo_publicacoes" })
        {
            await using (var protecao = new NpgsqlCommand("SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = @tabela", restrito))
            {
                protecao.Parameters.AddWithValue("tabela", tabela);
                ((bool)(await protecao.ExecuteScalarAsync())!).Should().BeTrue(tabela);
            }
            (await ContarAsync(tabela)).Should().Be(1, $"{tabela} deve ocultar a segunda empresa sem filtros EF");
            foreach (var sql in new[]
            {
                $"UPDATE {tabela} SET \"EmpresaId\" = \"EmpresaId\" WHERE \"EmpresaId\" = @empresa",
                $"DELETE FROM {tabela} WHERE \"EmpresaId\" = @empresa"
            })
            {
                await using var comando = new NpgsqlCommand(sql, restrito);
                comando.Parameters.AddWithValue("empresa", outra);
                (await comando.ExecuteNonQueryAsync()).Should().Be(0, $"RLS deve ocultar a outra empresa em {tabela}");
            }
            await using (var mover = new NpgsqlCommand($"UPDATE {tabela} SET \"EmpresaId\" = @outra", restrito))
            {
                mover.Parameters.AddWithValue("outra", outra);
                Func<Task> executar = async () => { await mover.ExecuteNonQueryAsync(); };
                (await executar.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            }
            foreach (var destino in new[] { outra, empresa })
            {
                await using var inserir = new NpgsqlCommand($"""
                    INSERT INTO {tabela}
                    SELECT (jsonb_populate_record(NULL::{tabela}, to_jsonb(t) ||
                        jsonb_build_object('Id', @id, 'EmpresaId', @empresa, 'Sha256', @hash))).*
                    FROM {tabela} t LIMIT 1
                    """, restrito);
                inserir.Parameters.AddWithValue("id", Guid.NewGuid());
                inserir.Parameters.AddWithValue("empresa", destino);
                inserir.Parameters.AddWithValue("hash", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
                if (destino == outra)
                {
                    Func<Task> executar = async () => { await inserir.ExecuteNonQueryAsync(); };
                    (await executar.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
                }
                else (await inserir.ExecuteNonQueryAsync()).Should().Be(1, $"a própria empresa deve poder inserir em {tabela}");
            }
            (await ContarAsync(tabela)).Should().Be(2);
        }
        await DefinirTenantAsync(outra.ToString());
        foreach (var tabela in new[] { "acervo_imagens", "acervo_solicitacoes", "acervo_publicacoes" })
            (await ContarAsync(tabela)).Should().Be(1, "nenhuma escrita da primeira empresa pode alterar a segunda");
        await DefinirTenantAsync("");
        foreach (var tabela in new[] { "acervo_imagens", "acervo_solicitacoes", "acervo_publicacoes" })
            (await ContarAsync(tabela)).Should().Be(0, "sem empresa, RLS deve negar leitura");

        async Task DefinirTenantAsync(string tenant)
        {
            await using var comando = new NpgsqlCommand("SELECT set_config('app.empresa_id', @tenant, false), set_config('app.bypass_rls', 'false', false)", restrito);
            comando.Parameters.AddWithValue("tenant", tenant);
            await comando.ExecuteNonQueryAsync();
        }
        async Task<long> ContarAsync(string tabela)
        {
            await using var comando = new NpgsqlCommand($"SELECT count(*) FROM {tabela}", restrito);
            return (long)(await comando.ExecuteScalarAsync())!;
        }
    }
}
