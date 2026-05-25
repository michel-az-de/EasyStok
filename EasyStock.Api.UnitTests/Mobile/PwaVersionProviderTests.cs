using EasyStock.Api.Mobile.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// PwaVersionProvider é a fonte da verdade da versão atual do bundle PWA.
/// Quebra disso = OTA do PWA não funciona (PWA fica em loop tentando atualizar
/// pra uma versão que o servidor nem entende, ou nunca atualiza porque servidor
/// reporta versão errada).
///
/// Cobertura aqui:
///   - Lê CACHE_VERSION corretamente de várias formas que o sw.js pode estar escrito
///   - Cache TTL: chamadas dentro de 60s servem do cache (não bate disco)
///   - Fallback para config quando arquivo não tem CACHE_VERSION
///   - Fallback final 'cdb-unknown' quando nem config tem
///   - Thread-safety: 100 threads concorrentes pegam o MESMO valor
///   - Edge cases: arquivo vazio, regex que não casa, sw.js inexistente
/// </summary>
public sealed class PwaVersionProviderTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _pwaDir;

    public PwaVersionProviderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "pwa-version-tests-" + Guid.NewGuid().ToString("N"));
        _pwaDir = Path.Combine(_tempRoot, "wwwroot", "pwa");
        Directory.CreateDirectory(_pwaDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    private PwaVersionProvider BuildProvider(string? configFallback = null)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.WebRootPath.Returns(Path.Combine(_tempRoot, "wwwroot"));
        env.ContentRootPath.Returns(_tempRoot);

        var settings = new Dictionary<string, string?>();
        if (configFallback is not null) settings["Mobile:PwaCacheVersion"] = configFallback;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new PwaVersionProvider(env, config, NullLogger<PwaVersionProvider>.Instance);
    }

    private void WriteSw(string content)
    {
        File.WriteAllText(Path.Combine(_pwaDir, "sw.js"), content);
    }

    [Theory]
    [InlineData("const CACHE_VERSION = 'cdb-v3-20260506a';", "cdb-v3-20260506a")]
    [InlineData("const CACHE_VERSION='cdb-tight';", "cdb-tight")]
    [InlineData("const   CACHE_VERSION   =   'cdb-spaces' ;", "cdb-spaces")]
    [InlineData("const CACHE_VERSION = \"cdb-double-quotes\";", "cdb-double-quotes")]
    [InlineData("// preface\nconst CACHE_VERSION = 'cdb-with-comment';\n// after", "cdb-with-comment")]
    public void Le_CACHE_VERSION_de_varias_formas_de_sintaxe(string swContent, string expected)
    {
        WriteSw(swContent);
        var provider = BuildProvider();

        provider.GetCurrentCacheVersion().Should().Be(expected);
    }

    [Fact]
    public void Quando_sw_js_nao_existe_usa_fallback_da_config()
    {
        // Não cria sw.js
        var provider = BuildProvider(configFallback: "cdb-config-fallback");

        provider.GetCurrentCacheVersion().Should().Be("cdb-config-fallback");
    }

    [Fact]
    public void Quando_sw_js_nao_existe_e_config_vazio_usa_cdb_unknown()
    {
        var provider = BuildProvider(configFallback: null);

        provider.GetCurrentCacheVersion().Should().Be("cdb-unknown");
    }

    [Fact]
    public void Quando_sw_js_nao_tem_CACHE_VERSION_usa_fallback_da_config()
    {
        WriteSw("// arquivo sem CACHE_VERSION nenhum\nself.addEventListener('install', () => {});");
        var provider = BuildProvider(configFallback: "cdb-config-fallback");

        provider.GetCurrentCacheVersion().Should().Be("cdb-config-fallback");
    }

    [Fact]
    public void Cache_TTL_evita_releitura_de_disco()
    {
        WriteSw("const CACHE_VERSION = 'cdb-v1';");
        var provider = BuildProvider();

        // Primeira chamada lê do disco
        provider.GetCurrentCacheVersion().Should().Be("cdb-v1");

        // Mudo o arquivo no disco MAS dentro da janela de cache
        WriteSw("const CACHE_VERSION = 'cdb-v2';");

        // Próxima chamada deve ainda servir do cache (60s TTL)
        provider.GetCurrentCacheVersion().Should().Be("cdb-v1");
    }

    [Fact]
    public void Multiplas_instancias_leem_o_arquivo_atual()
    {
        // Cada instância tem seu próprio cache. Nova instância sempre relê.
        WriteSw("const CACHE_VERSION = 'cdb-v1';");
        BuildProvider().GetCurrentCacheVersion().Should().Be("cdb-v1");

        WriteSw("const CACHE_VERSION = 'cdb-v2';");
        BuildProvider().GetCurrentCacheVersion().Should().Be("cdb-v2");
    }

    [Fact]
    public async Task Concorrencia_100_threads_pegam_o_MESMO_valor()
    {
        WriteSw("const CACHE_VERSION = 'cdb-concurrent';");
        var provider = BuildProvider();

        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() => provider.GetCurrentCacheVersion())).ToArray();
        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r => r.Should().Be("cdb-concurrent"));
        results.Distinct().Should().HaveCount(1);
    }

    [Fact]
    public void Arquivo_vazio_cai_no_fallback()
    {
        WriteSw(string.Empty);
        var provider = BuildProvider(configFallback: "cdb-fb");

        provider.GetCurrentCacheVersion().Should().Be("cdb-fb");
    }

    [Fact]
    public void Le_de_dentro_de_string_complexa_com_escapes()
    {
        // Sintaxe similar à real: comentários, multilinhas, código depois.
        var sw = """
                 // header
                 // multilinhas
                 const CACHE_VERSION = 'cdb-real-20260506a';
                 const STATIC_ASSETS = ['./','./index.html'];
                 self.addEventListener('install', (event) => {});
                 """;
        WriteSw(sw);
        var provider = BuildProvider();

        provider.GetCurrentCacheVersion().Should().Be("cdb-real-20260506a");
    }

    [Fact]
    public void Le_o_sw_js_real_do_repo()
    {
        // Smoke test: o sw.js commitado deve ser lido sem erro pelo provider.
        // Procura ANCESTRAL — sobe a árvore até achar um diretório com solution.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EasyStok.sln"))
                                && !File.Exists(Path.Combine(dir.FullName, "easystock-back.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null) return; // executando fora da árvore — skip silencioso

        var realSw = Path.Combine(dir.FullName, "EasyStock.Api", "wwwroot", "pwa", "sw.js");
        if (!File.Exists(realSw)) return;

        // Copia o sw.js real pro temp
        File.Copy(realSw, Path.Combine(_pwaDir, "sw.js"), overwrite: true);

        var provider = BuildProvider();
        var version = provider.GetCurrentCacheVersion();

        // Deve ser uma versão "cdb-*" plausível (o provider extraiu corretamente).
        version.Should().StartWith("cdb-", "o sw.js real sempre tem CACHE_VERSION com prefixo cdb-");
        version.Should().NotBe("cdb-unknown", "o regex deve casar com o sw.js real");
        version.Should().NotContain("'", "regex deve remover as aspas do valor");
        version.Should().NotContain("\"");
    }
}
