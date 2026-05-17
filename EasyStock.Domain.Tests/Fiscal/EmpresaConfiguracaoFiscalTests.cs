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
}
