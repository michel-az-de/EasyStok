using EasyStock.Application.UseCases.GerenciarUploads;

namespace EasyStock.Application.Tests.UseCases;

public class StorageKeyExtractorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Extract_retorna_null_para_url_vazia(string? url)
    {
        StorageKeyExtractor.Extract(url).Should().BeNull();
    }

    [Fact]
    public void Extract_remove_prefixo_files_em_url_relativa()
    {
        StorageKeyExtractor.Extract("/files/produtos/123/foto.jpg")
            .Should().Be("produtos/123/foto.jpg");
    }

    [Fact]
    public void Extract_aceita_case_misto_no_prefixo_files()
    {
        StorageKeyExtractor.Extract("/FILES/lojas/abc/logo.png")
            .Should().Be("lojas/abc/logo.png");
    }

    [Fact]
    public void Extract_em_url_absoluta_s3_remove_primeiro_segmento_como_bucket()
    {
        StorageKeyExtractor.Extract("https://bucket.example.com/meu-bucket/produtos/1/foto.webp")
            .Should().Be("produtos/1/foto.webp");
    }

    [Fact]
    public void Extract_em_url_absoluta_com_dominio_path_style()
    {
        StorageKeyExtractor.Extract("https://s3.amazonaws.com/bucket/path/arquivo.jpg")
            .Should().Be("path/arquivo.jpg");
    }

    [Fact]
    public void Extract_em_url_absoluta_sem_sub_path_retorna_path_simples()
    {
        StorageKeyExtractor.Extract("https://cdn.example.com/arquivo.jpg")
            .Should().Be("arquivo.jpg");
    }

    [Fact]
    public void Extract_em_string_qualquer_nao_uri_retorna_null()
    {
        StorageKeyExtractor.Extract("nao-e-uma-url")
            .Should().BeNull();
    }

    [Fact]
    public void Extract_encontra_files_mesmo_no_meio_da_url()
    {
        StorageKeyExtractor.Extract("https://api.example.com/files/usuarios/42/avatar.png")
            .Should().Be("usuarios/42/avatar.png");
    }
}
