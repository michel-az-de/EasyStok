using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Testes da entity <see cref="CheckoutIdempotency"/>.
///
/// CheckoutIdempotency previne checkouts duplicados do cliente (R5):
/// se o usuário clicar duas vezes em "Pagar", aceitamos o segundo POST,
/// detectamos a mesma (Key, ContentHash) e retornamos a Fatura/InitPoint
/// originais — sem criar Fatura nova nem reabrir InitPoint MercadoPago.
///
/// <para>
/// <strong>Key</strong>: gerado pelo client (header X-Idempotency-Key, UUID).
/// <strong>ContentHash</strong>: SHA-256 hex do conteúdo do cart no momento
/// do POST — se o conteúdo do cart muda mas a Key é a mesma, significa que
/// o cliente alterou o carrinho e clicou de novo: tratamos como cart novo.
/// </para>
///
/// <para>
/// <strong>TTL 24h</strong> definido na factory; cleanup job remove expirados
/// depois (escopo: outra task).
/// </para>
///
/// TDD red phase: todos os cenários abaixo devem FALHAR até a entity ser
/// implementada na green phase.
/// </summary>
public class CheckoutIdempotencyTests
{
    // ── Helpers ────────────────────────────────────────────────────────

    private const string HashValido =
        "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";

    private static CheckoutIdempotency NovoValido(
        Guid? key = null,
        string contentHash = HashValido)
    {
        return CheckoutIdempotency.Criar(key ?? Guid.NewGuid(), contentHash);
    }

    // ── Factory: happy path ────────────────────────────────────────────

    [Fact]
    public void Criar_define_estado_inicial_com_ttl_24h()
    {
        var key = Guid.NewGuid();
        var antes = DateTime.UtcNow;

        var idem = CheckoutIdempotency.Criar(key, HashValido);

        idem.Id.Should().NotBeEmpty();
        idem.Key.Should().Be(key);
        idem.ContentHash.Should().Be(HashValido);
        idem.FaturaId.Should().BeNull("Fatura é vinculada só após criar — começa null");
        idem.InitPoint.Should().BeNull("InitPoint é setado só após chamar MercadoPago — começa null");

        idem.CriadoEm.Should().BeOnOrAfter(antes).And.BeOnOrBefore(DateTime.UtcNow);
        idem.ExpiraEm.Should().BeCloseTo(idem.CriadoEm.AddHours(24), TimeSpan.FromSeconds(2),
            "TTL é 24h definido pela factory");
    }

    [Fact]
    public void Criar_normaliza_content_hash_para_lowercase()
    {
        var hashUpper = HashValido.ToUpperInvariant();

        var idem = CheckoutIdempotency.Criar(Guid.NewGuid(), hashUpper);

        idem.ContentHash.Should().Be(HashValido,
            "hash hex é case-insensitive — normalizamos para consistência no índice único");
    }

    [Fact]
    public void Criar_rejeita_key_vazia()
    {
        var act = () => CheckoutIdempotency.Criar(Guid.Empty, HashValido);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Key*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_rejeita_content_hash_vazio(string? hash)
    {
        var act = () => CheckoutIdempotency.Criar(Guid.NewGuid(), hash!);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*ContentHash*");
    }

    [Theory]
    [InlineData("abc123")]                                                    // muito curto
    [InlineData("a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9")]  // 63 chars
    [InlineData("a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f900")] // 65 chars
    [InlineData("g1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90")]  // char não-hex
    public void Criar_rejeita_content_hash_em_formato_invalido(string hash)
    {
        var act = () => CheckoutIdempotency.Criar(Guid.NewGuid(), hash);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*ContentHash*");
    }

    // ── VincularFatura: muda de "Iniciado" para "ComFatura" ────────────

    [Fact]
    public void VincularFatura_associa_fatura_id_e_init_point()
    {
        var idem = NovoValido();
        var faturaId = Guid.NewGuid();
        var initPoint = "https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=abc123";

        idem.VincularFatura(faturaId, initPoint);

        idem.FaturaId.Should().Be(faturaId);
        idem.InitPoint.Should().Be(initPoint);
    }

    [Fact]
    public void VincularFatura_rejeita_fatura_id_vazio()
    {
        var idem = NovoValido();

        var act = () => idem.VincularFatura(Guid.Empty, "https://mp.com/x");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Fatura*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void VincularFatura_rejeita_init_point_vazio(string? initPoint)
    {
        var idem = NovoValido();

        var act = () => idem.VincularFatura(Guid.NewGuid(), initPoint!);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*InitPoint*");
    }

    [Fact]
    public void VincularFatura_eh_idempotente_para_mesma_fatura()
    {
        var idem = NovoValido();
        var faturaId = Guid.NewGuid();
        var initPoint = "https://mp.com/x";
        idem.VincularFatura(faturaId, initPoint);

        var act = () => idem.VincularFatura(faturaId, initPoint);

        act.Should().NotThrow("re-vincular com a mesma Fatura é no-op — protege contra retry idempotente");
        idem.FaturaId.Should().Be(faturaId);
    }

    [Fact]
    public void VincularFatura_rejeita_segunda_vinculacao_para_fatura_diferente()
    {
        var idem = NovoValido();
        idem.VincularFatura(Guid.NewGuid(), "https://mp.com/x");

        var act = () => idem.VincularFatura(Guid.NewGuid(), "https://mp.com/y");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Fatura*");
    }

    // ── Expirou ────────────────────────────────────────────────────────

    [Fact]
    public void Expirou_retorna_false_dentro_do_ttl()
    {
        var idem = NovoValido();

        idem.Expirou(idem.CriadoEm.AddHours(23)).Should().BeFalse();
    }

    [Fact]
    public void Expirou_retorna_true_apos_ttl()
    {
        var idem = NovoValido();

        idem.Expirou(idem.CriadoEm.AddHours(24).AddSeconds(1)).Should().BeTrue();
    }

    // ── Conferência de match (Key + ContentHash) — semântica do índice único ──

    [Fact]
    public void Confere_retorna_true_quando_key_e_hash_batem()
    {
        var key = Guid.NewGuid();
        var idem = CheckoutIdempotency.Criar(key, HashValido);

        idem.Confere(key, HashValido).Should().BeTrue();
    }

    [Fact]
    public void Confere_retorna_false_quando_hash_diferente_significa_cart_mudou()
    {
        // Cliente clicou em "Pagar" com cart {A,B}, depois adicionou C e
        // clicou de novo. Mesma Key mas hash diferente → não é o mesmo
        // checkout — caller deve tratar como cart novo (criar Fatura nova).
        var key = Guid.NewGuid();
        var idem = CheckoutIdempotency.Criar(key, HashValido);
        var hashOutro =
            "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";

        idem.Confere(key, hashOutro).Should().BeFalse();
    }
}
