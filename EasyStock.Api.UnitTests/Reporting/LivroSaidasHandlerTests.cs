using EasyStock.Application.Ports.Output;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.Fiscal.LivroSaidas;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Async.Reporting.Handlers.Fiscal;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Reporting;

/// <summary>
/// #1507: o Livro de Saídas pré-carrega os itens do período agregados por documento (antes eram 2 queries por NF-e).
/// Caracterização: CFOP principal, tributos e flag de rastreio saem iguais, inclusive para nota sem itens e legada.
/// </summary>
public class LivroSaidasHandlerTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private readonly EasyStockDbContext _db;
    private readonly ITenantScopedQueryBuilder _tenantQuery = Substitute.For<ITenantScopedQueryBuilder>();

    public LivroSaidasHandlerTests()
    {
        var bypass = Substitute.For<ICurrentUserAccessor>();
        bypass.IsAuthenticated.Returns(true);
        bypass.Nivel.Returns(NivelAcesso.SuperAdmin);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"livro-saidas-{Guid.NewGuid()}")
                .Options,
            bypass);
        _tenantQuery.Query<NfeDocumento>().Returns(_ => _db.NfeDocumentos.Where(n => n.EmpresaId == Empresa));
    }

    private NfeDocumento Nota(long numero, StatusNfe status, DateTime autorizadaEm, Guid? empresa = null,
        params (string? Cfop, decimal Preco, decimal? BaseIcms, decimal? Icms, decimal? Pis, decimal? Cofins)[] itens)
    {
        var doc = NfeDocumento.Criar(empresa ?? Empresa, Guid.NewGuid(), 1, numero,
            new DadosEmissor("Casa da Baba LTDA"), new DadosFaturado("Joao da Silva", "123.456.789-00"),
            Dinheiro.FromDecimal(100m));
        foreach (var (cfop, preco, baseIcms, icms, pis, cofins) in itens)
        {
            var item = doc.AdicionarItem("Bolo", 1, Dinheiro.FromDecimal(preco), "UN", cfop: cfop);
            item.BaseIcms = baseIcms;
            item.ValorIcms = icms;
            item.Pis = pis;
            item.Cofins = cofins;
        }
        doc.Status = status;
        doc.DataAutorizacao = autorizadaEm;
        _db.NfeDocumentos.Add(doc);
        return doc;
    }

    [Fact]
    public async Task Agrega_itens_por_nota_com_o_mesmo_resultado_de_antes()
    {
        Nota(1, StatusNfe.Autorizada, new DateTime(2026, 9, 10, 10, 0, 0),
            itens: [("5102", 10m, 10m, 1.8m, 0.1m, 0.5m), ("5405", 50m, null, null, null, null)]);
        Nota(2, StatusNfe.Cancelada, new DateTime(2026, 9, 11, 10, 0, 0));
        Nota(3, StatusNfe.Autorizada, new DateTime(2026, 9, 12, 10, 0, 0),
            itens: [(null, 100m, null, null, null, null), ("5101", 5m, null, null, null, null)]);
        Nota(4, StatusNfe.Rascunho, new DateTime(2026, 9, 13, 10, 0, 0), itens: [("5102", 1m, 1m, 1m, 1m, 1m)]);
        Nota(5, StatusNfe.Autorizada, new DateTime(2026, 10, 1, 10, 0, 0), itens: [("5102", 1m, 1m, 1m, 1m, 1m)]);
        Nota(6, StatusNfe.Autorizada, new DateTime(2026, 9, 14, 10, 0, 0), empresa: Guid.NewGuid(),
            itens: [("5102", 1m, 1m, 1m, 1m, 1m)]);
        await _db.SaveChangesAsync();

        var handler = new LivroSaidasHandler(_db, _tenantQuery);
        var linhas = new List<LivroSaidasRow>();
        await foreach (var linha in handler.StreamAsync(new LivroSaidasParams(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), default))
            linhas.Add(linha);

        linhas.Select(l => l.Numero).Should().Equal(1, 2, 3);

        var comTributos = linhas[0];
        comTributos.CfopPrincipal.Should().Be("5405", "é o CFOP do item de maior subtotal");
        comTributos.TributosRastreados.Should().BeTrue();
        comTributos.BaseIcms.Should().Be(10m);
        comTributos.ValorIcms.Should().Be(1.8m);
        comTributos.Pis.Should().Be(0.1m);
        comTributos.Cofins.Should().Be(0.5m);

        var semItens = linhas[1];
        semItens.CfopPrincipal.Should().BeNull();
        semItens.TributosRastreados.Should().BeFalse();
        (semItens.BaseIcms + semItens.ValorIcms + semItens.Pis + semItens.Cofins).Should().Be(0m);

        var legada = linhas[2];
        legada.CfopPrincipal.Should().Be("5101", "item sem CFOP não conta, mesmo com subtotal maior");
        legada.TributosRastreados.Should().BeFalse();
        legada.BaseIcms.Should().Be(0m);
    }
}
