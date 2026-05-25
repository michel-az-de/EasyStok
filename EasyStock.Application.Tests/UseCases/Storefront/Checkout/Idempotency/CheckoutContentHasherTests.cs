using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using FluentAssertions;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Storefront.Checkout.Idempotency;

public class CheckoutContentHasherTests
{
    private static readonly Guid SlugStorefront = Guid.NewGuid();
    private static readonly Guid JanelaId = Guid.NewGuid();
    private static readonly DateOnly DataEntrega = new(2026, 6, 2);
    private static readonly Guid ItemA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ItemB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static IniciarCheckoutInput InputBase(string slug = "casa-da-baba") => new(
        Slug: slug,
        ClienteId: Guid.NewGuid(),
        Items: new List<CheckoutItemInput> { new(ItemA, 2), new(ItemB, 1) },
        JanelaId: JanelaId,
        DataEntrega: DataEntrega,
        Cep: "01310100");

    [Fact]
    public void ComputarHash_MesmaEntrada_RetornaMesmoHash()
    {
        var input = InputBase();

        var hash1 = CheckoutContentHasher.ComputarHash(input);
        var hash2 = CheckoutContentHasher.ComputarHash(input);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputarHash_RetornaHexLowercase64Chars()
    {
        var hash = CheckoutContentHasher.ComputarHash(InputBase());

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void ComputarHash_OrdemItensDiferente_RetornaMesmoHash()
    {
        var inputAB = InputBase() with
        {
            Items = new List<CheckoutItemInput> { new(ItemA, 2), new(ItemB, 1) },
        };
        var inputBA = InputBase() with
        {
            Items = new List<CheckoutItemInput> { new(ItemB, 1), new(ItemA, 2) },
        };

        CheckoutContentHasher.ComputarHash(inputAB)
            .Should().Be(CheckoutContentHasher.ComputarHash(inputBA));
    }

    [Fact]
    public void ComputarHash_CepComMascara_MesmoHashQueSemMascara()
    {
        var comMascara = InputBase() with { Cep = "01310-100" };
        var semMascara = InputBase() with { Cep = "01310100" };

        CheckoutContentHasher.ComputarHash(comMascara)
            .Should().Be(CheckoutContentHasher.ComputarHash(semMascara));
    }

    [Fact]
    public void ComputarHash_SlugDiferente_HashDiferente()
    {
        var hash1 = CheckoutContentHasher.ComputarHash(InputBase("loja-a"));
        var hash2 = CheckoutContentHasher.ComputarHash(InputBase("loja-b"));

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputarHash_QtdDiferente_HashDiferente()
    {
        var input1 = InputBase() with { Items = new List<CheckoutItemInput> { new(ItemA, 2) } };
        var input2 = InputBase() with { Items = new List<CheckoutItemInput> { new(ItemA, 3) } };

        CheckoutContentHasher.ComputarHash(input1)
            .Should().NotBe(CheckoutContentHasher.ComputarHash(input2));
    }

    [Fact]
    public void ComputarHash_JanelaDiferente_HashDiferente()
    {
        var outraJanela = Guid.NewGuid();
        var input1 = InputBase();
        var input2 = InputBase() with { JanelaId = outraJanela };

        CheckoutContentHasher.ComputarHash(input1)
            .Should().NotBe(CheckoutContentHasher.ComputarHash(input2));
    }

    [Fact]
    public void ComputarHash_DataDiferente_HashDiferente()
    {
        var input1 = InputBase() with { DataEntrega = new DateOnly(2026, 6, 2) };
        var input2 = InputBase() with { DataEntrega = new DateOnly(2026, 6, 9) };

        CheckoutContentHasher.ComputarHash(input1)
            .Should().NotBe(CheckoutContentHasher.ComputarHash(input2));
    }
}
