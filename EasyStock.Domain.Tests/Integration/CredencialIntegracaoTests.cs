using EasyStock.Domain.Integration;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Integration;

public class CredencialIntegracaoTests
{
    private static byte[] FakeBytes(int len = 16) => Enumerable.Range(0, len).Select(i => (byte)i).ToArray();

    private static CredencialIntegracao CriarValida(DateTime? validoAte = null) =>
        CredencialIntegracao.Criar(
            empresaId: Guid.NewGuid(),
            categoria: CategoriaIntegracao.Payments,
            providerKey: "mercadopago",
            ambiente: AmbienteIntegracao.Sandbox,
            payloadCifrado: FakeBytes(64),
            kekId: "kek-2026-01",
            iv: FakeBytes(12),
            tag: FakeBytes(16),
            criadoPorUsuarioId: Guid.NewGuid(),
            validoAte: validoAte);

    [Fact]
    public void Criar_credencial_valida_inicializa_campos()
    {
        var c = CriarValida();

        c.Id.Should().NotBe(Guid.Empty);
        c.Categoria.Should().Be(CategoriaIntegracao.Payments);
        c.ProviderKey.Should().Be("mercadopago");
        c.Ambiente.Should().Be(AmbienteIntegracao.Sandbox);
        c.Ativo.Should().BeTrue();
        c.UltimoUsoEm.Should().BeNull();
        c.ValidoDe.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Criar_normaliza_providerKey_para_lowercase_trim()
    {
        var c = CredencialIntegracao.Criar(
            Guid.NewGuid(), CategoriaIntegracao.Marketplace,
            providerKey: "  Mercado_Livre  ",
            AmbienteIntegracao.Sandbox,
            FakeBytes(), "k", FakeBytes(12), FakeBytes(16), Guid.NewGuid());

        c.ProviderKey.Should().Be("mercado_livre");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_providerKey_invalido_lanca(string? providerKey)
    {
        Action act = () => CredencialIntegracao.Criar(
            Guid.NewGuid(), CategoriaIntegracao.Payments,
            providerKey!, AmbienteIntegracao.Sandbox,
            FakeBytes(), "k", FakeBytes(12), FakeBytes(16), Guid.NewGuid());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criar_empresaId_vazio_lanca()
    {
        Action act = () => CredencialIntegracao.Criar(
            Guid.Empty, CategoriaIntegracao.Payments,
            "mp", AmbienteIntegracao.Sandbox,
            FakeBytes(), "k", FakeBytes(12), FakeBytes(16), Guid.NewGuid());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criar_payload_vazio_lanca()
    {
        Action act = () => CredencialIntegracao.Criar(
            Guid.NewGuid(), CategoriaIntegracao.Payments,
            "mp", AmbienteIntegracao.Sandbox,
            payloadCifrado: Array.Empty<byte>(),
            "k", FakeBytes(12), FakeBytes(16), Guid.NewGuid());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criar_validoAte_passado_lanca()
    {
        Action act = () => CredencialIntegracao.Criar(
            Guid.NewGuid(), CategoriaIntegracao.Payments,
            "mp", AmbienteIntegracao.Sandbox,
            FakeBytes(), "k", FakeBytes(12), FakeBytes(16), Guid.NewGuid(),
            validoAte: DateTime.UtcNow.AddMinutes(-1));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RegistrarUso_atualiza_UltimoUsoEm()
    {
        var c = CriarValida();
        c.UltimoUsoEm.Should().BeNull();

        c.RegistrarUso();

        c.UltimoUsoEm.Should().NotBeNull();
        c.UltimoUsoEm!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Desativar_seta_Ativo_false_e_atualiza_AlteradoEm()
    {
        var c = CriarValida();
        Thread.Sleep(5);
        c.Desativar();

        c.Ativo.Should().BeFalse();
        c.AlteradoEm.Should().BeAfter(c.CriadoEm);
    }

    [Fact]
    public void Desativar_idempotente()
    {
        var c = CriarValida();
        c.Desativar();
        var alteradoEmPrimeira = c.AlteradoEm;

        c.Desativar();

        c.Ativo.Should().BeFalse();
        c.AlteradoEm.Should().Be(alteradoEmPrimeira); // não mexe se já desativada
    }

    [Fact]
    public void Reativar_de_credencial_ativa_e_idempotente()
    {
        var c = CriarValida();
        var alteradoEmPrimeira = c.AlteradoEm;

        c.Reativar();

        c.Ativo.Should().BeTrue();
        c.AlteradoEm.Should().Be(alteradoEmPrimeira);
    }

    [Fact]
    public void Reativar_credencial_expirada_lanca()
    {
        // Cria credencial valida ate 1s no futuro
        var c = CriarValida(validoAte: DateTime.UtcNow.AddSeconds(1));
        c.Desativar();

        // Espera passar a expiração
        Thread.Sleep(1100);

        Action act = () => c.Reativar();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EstaUtilizavel_credencial_ativa_e_no_prazo()
    {
        var c = CriarValida(validoAte: DateTime.UtcNow.AddDays(30));
        c.EstaUtilizavel().Should().BeTrue();
    }

    [Fact]
    public void EstaUtilizavel_credencial_inativa_retorna_false()
    {
        var c = CriarValida();
        c.Desativar();
        c.EstaUtilizavel().Should().BeFalse();
    }

    [Fact]
    public void EstaUtilizavel_credencial_expirada_retorna_false()
    {
        var c = CriarValida(validoAte: DateTime.UtcNow.AddSeconds(1));
        Thread.Sleep(1100);
        c.EstaUtilizavel().Should().BeFalse();
    }

    [Fact]
    public void RotacionarKek_atualiza_payload_kekId_iv_tag()
    {
        var c = CriarValida();
        var novoPayload = FakeBytes(80);
        var novoIv = FakeBytes(12);
        var novaTag = FakeBytes(16);

        c.RotacionarKek(novoPayload, "kek-2027-01", novoIv, novaTag);

        c.PayloadCifrado.Should().BeEquivalentTo(novoPayload);
        c.KekId.Should().Be("kek-2027-01");
        c.Iv.Should().BeEquivalentTo(novoIv);
        c.Tag.Should().BeEquivalentTo(novaTag);
    }

    [Fact]
    public void RotacionarKek_payload_vazio_lanca()
    {
        var c = CriarValida();
        Action act = () => c.RotacionarKek(Array.Empty<byte>(), "k2", FakeBytes(12), FakeBytes(16));
        act.Should().Throw<ArgumentException>();
    }
}
