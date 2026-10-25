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

    // #2027: a chave só sai se for do dono.
    private static readonly Guid Vitrine = Guid.NewGuid();
    private static readonly Guid Prato = Guid.NewGuid();

    [Theory]
    [InlineData("/files/cardapios/E/{v}/{p}/foto.webp", true)]
    [InlineData("https://cdn/files/cardapios/E/{v}/{p}/acervo-1.webp", true)]
    [InlineData("/files/cardapios/E/{v}/outro-prato/foto.webp", false)]
    [InlineData("/files/cardapios/E/outra-vitrine/{p}/foto.webp", false)]
    [InlineData("/files/cardapios/E/{v}/{p}/../../outra/x/foto.webp", false)]
    [InlineData("/files/lojas/E/L/logo/x.png", false)]
    [InlineData("/files/cardapios/E/{v}/{p}", false)]
    public void ExtractDaFotoDoPrato_so_aceita_o_proprio_prato(string modelo, bool aceita)
    {
        var url = modelo.Replace("{v}", Vitrine.ToString()).Replace("{p}", Prato.ToString());

        var chave = StorageKeyExtractor.ExtractDaFotoDoPrato(url, Vitrine, Prato);

        (chave is not null).Should().Be(aceita);
    }

    [Theory]
    [InlineData("/files/usuarios/U/avatar/a.png", "usuarios/U/avatar/", true)]
    [InlineData("/files/usuarios/OUTRO/avatar/a.png", "usuarios/U/avatar/", false)]
    [InlineData("/files/usuarios/U/avatar/../../OUTRO/avatar/a.png", "usuarios/U/avatar/", false)]
    [InlineData(@"/files/usuarios/U/avatar/x\y.png", "usuarios/U/avatar/", false)]
    public void ExtractDoDono_confere_o_prefixo_e_recusa_subir_de_pasta(string url, string prefixo, bool aceita)
    {
        (StorageKeyExtractor.ExtractDoDono(url, prefixo) is not null).Should().Be(aceita);
    }
}
