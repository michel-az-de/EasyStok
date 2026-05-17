using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.Integration;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Fiscal;

public class EmpresaConfiguracaoFiscalTests
{
    private static EmpresaConfiguracaoFiscal CriarValida() =>
        EmpresaConfiguracaoFiscal.Criar(Guid.NewGuid(), RegimeTributario.Simples);

    private static Endereco EnderecoCompleto() =>
        new(Logradouro: "Av. Paulista", Numero: "1000", Bairro: "Bela Vista",
            Cidade: "Sao Paulo", Uf: "SP", Cep: "01310-100");

    [Fact]
    public void Criar_com_dados_validos_inicializa_defaults()
    {
        var c = CriarValida();

        c.Id.Should().NotBe(Guid.Empty);
        c.RegimeTributario.Should().Be(RegimeTributario.Simples);
        c.Ambiente.Should().Be(AmbienteIntegracao.Sandbox);
        c.ProvedorPreferido.Should().Be("mock");
        c.SerieNfce.Should().Be((short)1);
        c.ProximoNumeroNfce.Should().Be(1L);
        c.Habilitada.Should().BeFalse();
        c.CriadoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Criar_empresaId_vazio_lanca()
    {
        Action act = () => EmpresaConfiguracaoFiscal.Criar(Guid.Empty, RegimeTributario.Simples);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EscolherProvedor_normaliza_para_lowercase()
    {
        var c = CriarValida();
        c.EscolherProvedor("  FOCUS  ");
        c.ProvedorPreferido.Should().Be("focus");
    }

    [Theory]
    [InlineData("invalido")]
    [InlineData("xpto")]
    public void EscolherProvedor_desconhecido_lanca(string provedor)
    {
        var c = CriarValida();
        Action act = () => c.EscolherProvedor(provedor);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EscolherProvedor_vazio_lanca(string provedor)
    {
        var c = CriarValida();
        Action act = () => c.EscolherProvedor(provedor);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Habilitar_sem_inscricao_estadual_lanca()
    {
        var c = CriarValida();
        c.AtualizarDadosEmitente(inscricaoEstadual: null, inscricaoMunicipal: null, endereco: EnderecoCompleto());

        Action act = () => c.Habilitar();
        act.Should().Throw<RegraDeDominioVioladaException>()
           .WithMessage("*Inscricao Estadual*");
    }

    [Fact]
    public void Habilitar_sem_endereco_completo_lanca()
    {
        var c = CriarValida();
        c.AtualizarDadosEmitente("123456789", null, endereco: null);

        Action act = () => c.Habilitar();
        act.Should().Throw<RegraDeDominioVioladaException>()
           .WithMessage("*Endereco*");
    }

    [Fact]
    public void Habilitar_sandbox_com_dados_completos_passa()
    {
        var c = CriarValida();
        c.AtualizarDadosEmitente("123456789", null, EnderecoCompleto());

        c.Habilitar();

        c.Habilitada.Should().BeTrue();
    }

    [Fact]
    public void Habilitar_producao_exige_certificado()
    {
        var c = CriarValida();
        c.AlterarAmbiente(AmbienteIntegracao.Production);
        c.AtualizarDadosEmitente("123456789", null, EnderecoCompleto());
        c.EscolherProvedor("focus");

        Action act = () => c.Habilitar();
        act.Should().Throw<RegraDeDominioVioladaException>()
           .WithMessage("*Certificado*");
    }

    [Fact]
    public void Habilitar_producao_com_provedor_mock_lanca()
    {
        var c = CriarValida();
        c.AlterarAmbiente(AmbienteIntegracao.Production);
        c.AtualizarDadosEmitente("123456789", null, EnderecoCompleto());
        c.VincularCertificado(Guid.NewGuid());

        Action act = () => c.Habilitar();
        act.Should().Throw<RegraDeDominioVioladaException>()
           .WithMessage("*mock*producao*");
    }

    [Fact]
    public void Habilitar_eh_idempotente()
    {
        var c = CriarValida();
        c.AtualizarDadosEmitente("123456789", null, EnderecoCompleto());
        c.Habilitar();
        var alteradoEm1 = c.AlteradoEm;

        c.Habilitar();

        c.AlteradoEm.Should().Be(alteradoEm1);
    }

    [Fact]
    public void ReservarProximoNumero_incrementa_e_retorna_anterior()
    {
        var c = CriarValida();
        c.ProximoNumeroNfce.Should().Be(1L);

        var n1 = c.ReservarProximoNumero();
        var n2 = c.ReservarProximoNumero();

        n1.Should().Be(1L);
        n2.Should().Be(2L);
        c.ProximoNumeroNfce.Should().Be(3L);
    }

    [Fact]
    public void Desabilitar_habilitada_desliga()
    {
        var c = CriarValida();
        c.AtualizarDadosEmitente("123456789", null, EnderecoCompleto());
        c.Habilitar();

        c.Desabilitar();

        c.Habilitada.Should().BeFalse();
    }

    [Fact]
    public void ConfigurarCsc_com_dados_validos_armazena()
    {
        var c = CriarValida();
        c.ConfigurarCsc("1", "abc123def456");

        c.CscId.Should().Be("1");
        c.CscToken.Should().Be("abc123def456");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConfigurarCsc_cscId_vazio_lanca(string? cscId)
    {
        var c = CriarValida();
        Action act = () => c.ConfigurarCsc(cscId!, "token-valido");
        act.Should().Throw<ArgumentException>().WithMessage("*CSC ID*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConfigurarCsc_cscToken_vazio_lanca(string? cscToken)
    {
        var c = CriarValida();
        Action act = () => c.ConfigurarCsc("1", cscToken!);
        act.Should().Throw<ArgumentException>().WithMessage("*CSC Token*");
    }

    [Fact]
    public void ConfigurarCsc_atualiza_AlteradoEm()
    {
        var c = CriarValida();
        var antes = c.AlteradoEm;

        c.ConfigurarCsc("1", "abc123");

        c.AlteradoEm.Should().BeOnOrAfter(antes);
    }

    [Fact]
    public void ConfigurarCsc_sobrescreve_valores_anteriores()
    {
        var c = CriarValida();
        c.ConfigurarCsc("1", "token-antigo");
        c.ConfigurarCsc("2", "token-novo");

        c.CscId.Should().Be("2");
        c.CscToken.Should().Be("token-novo");
    }

    [Fact]
    public void AlterarSerieNfce_valor_valido_atualiza()
    {
        var c = CriarValida();
        c.AlterarSerieNfce(5);

        c.SerieNfce.Should().Be((short)5);
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)-1)]
    public void AlterarSerieNfce_valor_invalido_lanca(short serie)
    {
        var c = CriarValida();
        Action act = () => c.AlterarSerieNfce(serie);
        act.Should().Throw<ArgumentException>().WithMessage("*Serie*");
    }

    [Fact]
    public void AlterarSerieNfce_mesmo_valor_nao_atualiza_timestamp()
    {
        var c = CriarValida();
        var antes = c.AlteradoEm;

        c.AlterarSerieNfce(1);

        c.AlteradoEm.Should().Be(antes);
    }
}
