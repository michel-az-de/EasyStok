using EasyStock.Api.Services.Faturacao;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Api.UnitTests.Faturacao;

/// <summary>
/// Cobertura para o helper que constroi commands de emissao de Fatura para
/// fluxos auto-gerados pelo SaaS (Job de cobranca + Backfill).
/// </summary>
public class FaturaSaasFactoryTests
{
    private static IConfiguration BuildConfiguration(Dictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
    }

    private static (AssinaturaEmpresa assinatura, Plano plano, Empresa empresa) BuildStubs()
    {
        var empresa = Empresa.Criar("Casa da Baba LTDA", "12345678000100");
        var plano = new Plano
        {
            Id = Guid.NewGuid(),
            Nome = "Plus",
            PrecoMensal = 199.90m,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        var assinatura = new AssinaturaEmpresa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            PlanoId = plano.Id,
            DataInicio = DateTime.UtcNow.AddDays(-30),
            DataFim = DateTime.UtcNow.AddDays(7),
            Status = StatusAssinatura.Ativa,
            CriadoEm = DateTime.UtcNow.AddDays(-30),
            AlteradoEm = DateTime.UtcNow
        };
        return (assinatura, plano, empresa);
    }

    [Fact]
    public void BuildParaAssinatura_DefineOrigemAssinaturaEOrigemRefIdComoAssinaturaId()
    {
        var (a, p, e) = BuildStubs();
        var factory = new FaturaSaasFactory(BuildConfiguration());

        var cmd = factory.BuildParaAssinatura(a, p, e);

        cmd.Origem.Should().Be(OrigemFatura.Assinatura);
        cmd.OrigemRefId.Should().Be(a.Id);
        cmd.EmpresaId.Should().Be(e.Id);
        cmd.IdempotentePorOrigem.Should().BeTrue();
    }

    [Fact]
    public void BuildParaAssinatura_CriaItemUnicoComPrecoMensalDoPlano()
    {
        var (a, p, e) = BuildStubs();
        var factory = new FaturaSaasFactory(BuildConfiguration());

        var cmd = factory.BuildParaAssinatura(a, p, e);

        cmd.Itens.Should().ContainSingle();
        var item = cmd.Itens[0];
        item.Tipo.Should().Be(TipoItemFatura.Recorrencia);
        item.Quantidade.Should().Be(1);
        item.PrecoUnitario.Should().Be(p.PrecoMensal);
        item.Descricao.Should().Contain(p.Nome);
    }

    [Fact]
    public void BuildParaAssinatura_FaturadoUsaSnapshotDaEmpresa()
    {
        var (a, p, e) = BuildStubs();
        var factory = new FaturaSaasFactory(BuildConfiguration());

        var cmd = factory.BuildParaAssinatura(a, p, e);

        cmd.DadosFaturado.Nome.Should().Be(e.Nome);
        cmd.DadosFaturado.Documento.Should().Be(e.Documento);
    }

    [Fact]
    public void BuildParaAssinatura_EmissorDefault_QuandoSemConfiguracao()
    {
        var (a, p, e) = BuildStubs();
        var factory = new FaturaSaasFactory(BuildConfiguration());

        var cmd = factory.BuildParaAssinatura(a, p, e);

        cmd.DadosEmissor.Nome.Should().Be("EasyStock");
        cmd.DadosEmissor.Documento.Should().BeNull();
    }

    [Fact]
    public void BuildParaAssinatura_EmissorLeDeConfigurationCompleta()
    {
        var (a, p, e) = BuildStubs();
        var config = BuildConfiguration(new()
        {
            ["Saas:Emissor:Nome"] = "EasyStock SaaS Brasil",
            ["Saas:Emissor:Documento"] = "98765432000100",
            ["Saas:Emissor:RazaoSocial"] = "EasyStock Tecnologia LTDA",
            ["Saas:Emissor:InscricaoMunicipal"] = "1234567",
            ["Saas:Emissor:Email"] = "billing@easystock.com",
            ["Saas:Emissor:Endereco:Logradouro"] = "Av. Paulista",
            ["Saas:Emissor:Endereco:Numero"] = "1500",
            ["Saas:Emissor:Endereco:Cidade"] = "Sao Paulo",
            ["Saas:Emissor:Endereco:Uf"] = "SP",
            ["Saas:Emissor:Endereco:Cep"] = "01310-100"
        });
        var factory = new FaturaSaasFactory(config);

        var cmd = factory.BuildParaAssinatura(a, p, e);

        cmd.DadosEmissor.Nome.Should().Be("EasyStock SaaS Brasil");
        cmd.DadosEmissor.Documento.Should().Be("98765432000100");
        cmd.DadosEmissor.RazaoSocial.Should().Be("EasyStock Tecnologia LTDA");
        cmd.DadosEmissor.InscricaoMunicipal.Should().Be("1234567");
        cmd.DadosEmissor.Endereco.Should().NotBeNull();
        cmd.DadosEmissor.Endereco!.Logradouro.Should().Be("Av. Paulista");
        cmd.DadosEmissor.Endereco.Cidade.Should().Be("Sao Paulo");
        cmd.DadosEmissor.Endereco.Uf.Should().Be("SP");
    }

    [Fact]
    public void BuildParaAssinatura_AceitaDataEmissaoEVencimentoCustom()
    {
        var (a, p, e) = BuildStubs();
        var factory = new FaturaSaasFactory(BuildConfiguration());
        var dataEmissao = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var dataVencimento = new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc);

        var cmd = factory.BuildParaAssinatura(a, p, e, dataEmissao, dataVencimento);

        cmd.DataEmissao.Should().Be(dataEmissao);
        cmd.DataVencimento.Should().Be(dataVencimento);
    }

    [Fact]
    public void BuildParaAssinatura_NullArguments_Lancam()
    {
        var (a, p, e) = BuildStubs();
        var factory = new FaturaSaasFactory(BuildConfiguration());

        FluentActions.Invoking(() => factory.BuildParaAssinatura(null!, p, e))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => factory.BuildParaAssinatura(a, null!, e))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => factory.BuildParaAssinatura(a, p, null!))
            .Should().Throw<ArgumentNullException>();
    }
}
