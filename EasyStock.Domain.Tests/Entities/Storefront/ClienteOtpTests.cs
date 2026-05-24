using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Testes da entity <see cref="ClienteOtp"/>.
///
/// Cobertura: factory (estado inicial + carimbo de TimeProvider) + invariantes
/// de segurança (NUNCA armazena plaintext, apenas hashes; SHA-256 do telefone
/// é determinístico; BCrypt do código é não-determinístico) + transições
/// (validar / consumir / registrar tentativa / expirar) + limite anti-brute
/// force (5 tentativas).
///
/// O verificador BCrypt é injetado como <c>Func&lt;string, string, bool&gt;</c>
/// para manter Domain puro (sem dep BCrypt.Net). Em produção, o caso de uso
/// passa <c>BCrypt.Net.BCrypt.Verify</c>.
///
/// TDD red phase: todos os cenários abaixo devem FALHAR até a entity ser
/// implementada na green phase.
/// </summary>
public class ClienteOtpTests
{
    private static readonly DateTimeOffset Inicio =
        new(2026, 5, 24, 10, 0, 0, TimeSpan.Zero);

    private const string TelefoneE164 = "+5511997573992";
    private const string Codigo = "123456";
    // Hash placeholder — em produção o caller faz BCrypt.HashPassword(codigo).
    // Pra testes usamos um marcador previsível que o verificador (Func) sabe comparar.
    private const string CodigoHashFake = "HASHED:123456";

    private static bool VerificadorFake(string codigo, string hash) =>
        hash == $"HASHED:{codigo}";

    private static ClienteOtp NovoOtpValido(FakeTime time, Guid? empresaId = null)
    {
        return ClienteOtp.Criar(
            empresaId: empresaId ?? Guid.NewGuid(),
            telefoneHash: ClienteOtp.CalcularTelefoneHash(TelefoneE164),
            codigoHash: CodigoHashFake,
            time: time,
            ipOrigem: "203.0.113.42",
            userAgent: "Mozilla/5.0 (TestAgent)");
    }

    // ── Factory: happy path ────────────────────────────────────────────

    [Fact]
    public void Criar_define_estado_inicial_e_carimba_via_TimeProvider()
    {
        var time = new FakeTime(Inicio);
        var empresaId = Guid.NewGuid();

        var otp = ClienteOtp.Criar(
            empresaId: empresaId,
            telefoneHash: ClienteOtp.CalcularTelefoneHash(TelefoneE164),
            codigoHash: CodigoHashFake,
            time: time,
            ipOrigem: "203.0.113.42",
            userAgent: "Mozilla/5.0");

        otp.Id.Should().NotBeEmpty();
        otp.EmpresaId.Should().Be(empresaId);
        otp.TelefoneHash.Should().NotBeNullOrEmpty();
        otp.CodigoHash.Should().Be(CodigoHashFake);
        otp.Tentativas.Should().Be(0);
        otp.Consumido.Should().BeFalse();
        otp.IpOrigem.Should().Be("203.0.113.42");
        otp.UserAgent.Should().Be("Mozilla/5.0");

        otp.CriadoEm.Should().Be(Inicio.UtcDateTime, "factory carimba via TimeProvider, não DateTime.UtcNow");
        otp.ExpiraEm.Should().Be(Inicio.UtcDateTime.AddMinutes(5), "OTP expira em 5 minutos (anti-brute force + UX)");
    }

    // ── Hash determinismo / não-plaintext ──────────────────────────────

    [Fact]
    public void CalcularTelefoneHash_e_deterministico_mesmo_telefone_mesmo_hash()
    {
        var hash1 = ClienteOtp.CalcularTelefoneHash(TelefoneE164);
        var hash2 = ClienteOtp.CalcularTelefoneHash(TelefoneE164);

        hash1.Should().Be(hash2, "lookup por telefone exige hash determinístico (SHA-256)");
        hash1.Length.Should().Be(64, "SHA-256 em hex tem 64 caracteres");
    }

    [Fact]
    public void CalcularTelefoneHash_telefones_diferentes_produzem_hashes_diferentes()
    {
        var h1 = ClienteOtp.CalcularTelefoneHash("+5511999990001");
        var h2 = ClienteOtp.CalcularTelefoneHash("+5511999990002");

        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Hash_de_telefone_NUNCA_e_igual_ao_telefone_plaintext()
    {
        var hash = ClienteOtp.CalcularTelefoneHash(TelefoneE164);
        hash.Should().NotBe(TelefoneE164, "hash NUNCA pode ser o próprio plaintext");
        hash.Should().NotContain("11997573992", "hash não pode conter o telefone");
    }

    // ── ValidarCodigo ──────────────────────────────────────────────────

    [Fact]
    public void ValidarCodigo_retorna_true_quando_codigo_bate_e_nao_expirou_e_nao_consumido()
    {
        var time = new FakeTime(Inicio);
        var otp = NovoOtpValido(time);

        otp.ValidarCodigo(Codigo, VerificadorFake, time).Should().BeTrue();
    }

    [Fact]
    public void ValidarCodigo_retorna_false_quando_codigo_errado()
    {
        var time = new FakeTime(Inicio);
        var otp = NovoOtpValido(time);

        otp.ValidarCodigo("000000", VerificadorFake, time).Should().BeFalse();
    }

    [Fact]
    public void ValidarCodigo_retorna_false_quando_expirou()
    {
        var time = new FakeTime(Inicio);
        var otp = NovoOtpValido(time);

        time.Advance(TimeSpan.FromMinutes(6));

        otp.Expirou(time).Should().BeTrue();
        otp.ValidarCodigo(Codigo, VerificadorFake, time).Should().BeFalse(
            "código correto mas OTP expirado deve falhar");
    }

    [Fact]
    public void ValidarCodigo_retorna_false_quando_ja_consumido()
    {
        var time = new FakeTime(Inicio);
        var otp = NovoOtpValido(time);

        otp.Consumir();

        otp.ValidarCodigo(Codigo, VerificadorFake, time).Should().BeFalse(
            "OTP consumido não pode ser revalidado (one-shot)");
    }

    // ── Anti-brute force (5 tentativas) ─────────────────────────────────

    [Fact]
    public void RegistrarTentativa_incrementa_contador_e_bloqueia_apos_5_tentativas()
    {
        var time = new FakeTime(Inicio);
        var otp = NovoOtpValido(time);

        for (var i = 0; i < 5; i++)
        {
            otp.RegistrarTentativa();
        }
        otp.Tentativas.Should().Be(5);

        var act = () => otp.RegistrarTentativa();
        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*5 tentativas*");
    }

    // ── Consumir idempotente ───────────────────────────────────────────

    [Fact]
    public void Consumir_e_idempotente()
    {
        var time = new FakeTime(Inicio);
        var otp = NovoOtpValido(time);

        otp.Consumir();
        otp.Consumido.Should().BeTrue();

        var act = () => otp.Consumir();
        act.Should().NotThrow("Consumir 2x não deve quebrar — operação idempotente");
        otp.Consumido.Should().BeTrue();
    }

    // ── TimeProvider mockado (Expirou) ──────────────────────────────────

    [Fact]
    public void Expirou_usa_TimeProvider_injetado_e_nao_DateTime_UtcNow()
    {
        var time = new FakeTime(Inicio);
        var otp = NovoOtpValido(time);

        otp.Expirou(time).Should().BeFalse("recém-criado: agora = CriadoEm, < ExpiraEm");

        time.Advance(TimeSpan.FromMinutes(4));
        otp.Expirou(time).Should().BeFalse("4 minutos < 5 minutos");

        time.Advance(TimeSpan.FromMinutes(2)); // total 6min
        otp.Expirou(time).Should().BeTrue("6 minutos > 5 minutos");
    }
}
