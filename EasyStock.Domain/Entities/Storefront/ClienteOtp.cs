using System.Security.Cryptography;
using System.Text;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Código OTP emitido para autenticar um cliente storefront via WhatsApp.
///
/// <para>
/// Armazena <strong>apenas hashes</strong>:
/// <see cref="TelefoneHash"/> = SHA-256(E.164) — determinístico, permite lookup.
/// <see cref="CodigoHash"/> = BCrypt(código) — não-determinístico, valida via verifier.
/// </para>
///
/// <para>
/// Política anti-brute force: <see cref="TempoVidaPadrao"/> = 5 minutos,
/// <see cref="MaxTentativas"/> = 5. Após 5 tentativas, <see cref="RegistrarTentativa"/>
/// lança <see cref="RegraDeDominioVioladaException"/>.
/// </para>
///
/// <para>
/// <strong>Tempo via TimeProvider</strong> (ADR-0012): factory, validação e
/// <see cref="Expirou"/> recebem <see cref="TimeProvider"/> injetado — nunca
/// <c>DateTime.UtcNow</c> direto, para manter testes determinísticos.
/// </para>
/// </summary>
public class ClienteOtp
{
    public static readonly TimeSpan TempoVidaPadrao = TimeSpan.FromMinutes(5);
    public const int MaxTentativas = 5;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }

    /// <summary>SHA-256 hex (64 chars) do telefone normalizado em E.164.</summary>
    public string TelefoneHash { get; private set; } = null!;

    /// <summary>Hash BCrypt do código (não-determinístico). Validar via <see cref="ValidarCodigo"/>.</summary>
    public string CodigoHash { get; private set; } = null!;

    public DateTime ExpiraEm { get; private set; }
    public int Tentativas { get; private set; }
    public bool Consumido { get; private set; }

    /// <summary>IP de origem em formato textual. Max 45 chars (suporta IPv6).</summary>
    public string? IpOrigem { get; private set; }

    /// <summary>User-Agent do cliente na hora de emissão. Max 300 chars.</summary>
    public string? UserAgent { get; private set; }

    public DateTime CriadoEm { get; private set; }

    // EF Core ctor sem parâmetros
    private ClienteOtp() { }

    /// <summary>
    /// Factory. <paramref name="codigoHash"/> deve ser pré-computado pelo caller
    /// (ex.: <c>BCrypt.Net.BCrypt.HashPassword(codigo)</c>) — Domain não depende de BCrypt.
    /// </summary>
    public static ClienteOtp Criar(
        Guid empresaId,
        string telefoneHash,
        string codigoHash,
        TimeProvider time,
        string? ipOrigem = null,
        string? userAgent = null)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        if (string.IsNullOrWhiteSpace(telefoneHash))
            throw new RegraDeDominioVioladaException("TelefoneHash é obrigatório.");
        if (string.IsNullOrWhiteSpace(codigoHash))
            throw new RegraDeDominioVioladaException("CodigoHash é obrigatório.");
        ArgumentNullException.ThrowIfNull(time);

        var agora = time.GetUtcNow().UtcDateTime;
        return new ClienteOtp
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            TelefoneHash = telefoneHash,
            CodigoHash = codigoHash,
            ExpiraEm = agora.Add(TempoVidaPadrao),
            Tentativas = 0,
            Consumido = false,
            IpOrigem = string.IsNullOrWhiteSpace(ipOrigem) ? null : ipOrigem.Trim(),
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim(),
            CriadoEm = agora,
        };
    }

    /// <summary>
    /// Calcula SHA-256 hex (lowercase, 64 chars) do telefone. Determinístico —
    /// mesmo input sempre produz mesmo output (permite lookup por telefone hashado).
    /// </summary>
    public static string CalcularTelefoneHash(string telefoneE164)
    {
        if (string.IsNullOrWhiteSpace(telefoneE164))
            throw new RegraDeDominioVioladaException("Telefone é obrigatório para hash.");

        var bytes = Encoding.UTF8.GetBytes(telefoneE164.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Valida código plaintext contra <see cref="CodigoHash"/>. Retorna <c>false</c>
    /// se expirado, consumido ou hash não bate.
    ///
    /// <paramref name="verificador"/> é a função de verificação (em produção,
    /// <c>BCrypt.Net.BCrypt.Verify</c>). <strong>NÃO incrementa tentativas</strong> —
    /// caller decide via <see cref="RegistrarTentativa"/>.
    /// </summary>
    public bool ValidarCodigo(string codigo, Func<string, string, bool> verificador, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(verificador);
        ArgumentNullException.ThrowIfNull(time);

        if (Consumido) return false;
        if (Expirou(time)) return false;
        if (Tentativas >= MaxTentativas) return false;
        if (string.IsNullOrEmpty(codigo)) return false;

        return verificador(codigo, CodigoHash);
    }

    /// <summary><c>true</c> se <c>now &gt; ExpiraEm</c> (TimeProvider).</summary>
    public bool Expirou(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        return time.GetUtcNow().UtcDateTime > ExpiraEm;
    }

    /// <summary>
    /// Incrementa contador. Lança <see cref="RegraDeDominioVioladaException"/> se
    /// já atingiu <see cref="MaxTentativas"/> — caller deve checar antes ou tratar.
    /// </summary>
    public void RegistrarTentativa()
    {
        if (Tentativas >= MaxTentativas)
            throw new RegraDeDominioVioladaException(
                $"OTP excedeu limite de {MaxTentativas} tentativas — bloqueado.");

        Tentativas++;
    }

    /// <summary>Marca como consumido (one-shot). Idempotente.</summary>
    public void Consumir()
    {
        if (Consumido) return;
        Consumido = true;
    }
}
