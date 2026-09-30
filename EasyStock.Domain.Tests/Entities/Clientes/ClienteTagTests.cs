using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Clientes;

/// <summary>S24: tag do cliente é normalizada (minúscula, sem acento, até 40) e única por cliente.</summary>
public class ClienteTagTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NormalizaEUnica()
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "Maria");

        var primeira = cliente.AdicionarTag("  Sem Glúten ", OrigemClienteTag.Dona, Agora);
        var repetida = cliente.AdicionarTag("sem_gluten", OrigemClienteTag.Agente, Agora.AddMinutes(1));

        primeira.Should().NotBeNull();
        primeira!.Tag.Should().Be("sem_gluten");
        primeira.Origem.Should().Be(OrigemClienteTag.Dona);
        primeira.EmpresaId.Should().Be(cliente.EmpresaId);
        primeira.ClienteId.Should().Be(cliente.Id);
        primeira.CriadoEm.Should().Be(Agora);
        repetida.Should().BeNull("tag duplicada é no-op no domínio");
        cliente.Tags.Should().ContainSingle();
    }

    [Theory]
    [InlineData("Intolerante à Lactose", "intolerante_a_lactose")]
    [InlineData("VEGANO", "vegano")]
    [InlineData("pão-de-ló", "pao_de_lo")]
    [InlineData("  encomenda  ", "encomenda")]
    public void Normalizar(string entrada, string esperado) =>
        ClienteTag.Normalizar(entrada).Should().Be(esperado);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void TagVaziaEhRecusada(string entrada)
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "Maria");
        var act = () => cliente.AdicionarTag(entrada, OrigemClienteTag.Dona, Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void TagAcimaDe40CaracteresEhRecusada()
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "Maria");
        var act = () => cliente.AdicionarTag(new string('a', 41), OrigemClienteTag.Dona, Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void OrigemForaDoVocabularioEhRecusada()
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "Maria");
        var act = () => cliente.AdicionarTag("vegano", "cliente", Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void RemoverTagNormalizaAntesDeProcurar()
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "Maria");
        cliente.AdicionarTag("vegano", OrigemClienteTag.Dona, Agora);

        cliente.RemoverTag("Vegano").Should().BeTrue();
        cliente.RemoverTag("vegano").Should().BeFalse();
        cliente.Tags.Should().BeEmpty();
    }

    [Fact]
    public void SugeridasJaEstaoNormalizadas() =>
        ClienteTag.Sugeridas.Should().OnlyContain(t => ClienteTag.Normalizar(t) == t)
            .And.Contain(["intolerante_lactose", "vegano", "sem_gluten", "risco", "encomenda"]);
}
