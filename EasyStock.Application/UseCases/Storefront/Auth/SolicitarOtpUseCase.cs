using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Messaging;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Storefront.Auth;

/// <summary>
/// Use case do primeiro passo do fluxo de autenticação storefront (ADR-0012):
/// cliente fornece telefone → gera código 6 dígitos numérico → persiste hash
/// (<c>BCrypt</c>) em <c>ClienteOtp</c> → dispara WhatsApp via
/// <see cref="IWhatsAppOtpSender"/>.
///
/// <para>
/// <strong>Anti-abuso</strong>: máx 3 OTPs/hora/telefone (4ª =
/// <see cref="OtpRateLimitExcedidoException"/>). 5 tentativas/OTP e expiração
/// 5 min são responsabilidade da entity <c>ClienteOtp</c> (EZ-005).
/// </para>
///
/// <para>
/// <strong>Idempotência</strong>: janela de 60s por <c>telefoneHash</c>.
/// Se há OTP recém-criado (independente da <c>IdempotencyKey</c> recebida),
/// retorna o mesmo TTL sem regerar nem enviar — cobre double-tap do botão
/// "Reenviar". <see cref="SolicitarOtpInput.IdempotencyKey"/> é usado apenas
/// como correlation id em logs (não persistido — iteração futura).
/// </para>
///
/// <para>
/// <strong>Ordem persist→send</strong>: OTP é persistido + commit ANTES do
/// envio externo. Se provider falhar, exceção propaga mas OTP fica no DB —
/// cliente pode tentar "Reenviar" e o use case reaproveita o registro recente.
/// Alternativa send→persist teria pior contrato: código enviado sem hash no DB
/// → cliente não conseguiria validar.
/// </para>
///
/// <para>
/// <strong>Logging seguro</strong>: telefone mascarado (<c>+5511*****1234</c>);
/// código NUNCA em log/response/métrica/exception. Idempotency key fica como
/// correlation id (não é PII).
/// </para>
/// </summary>
public sealed class SolicitarOtpUseCase(
    IStorefrontRepository storefrontRepository,
    IClienteOtpRepository clienteOtpRepository,
    IWhatsAppOtpSender whatsAppOtpSender,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<SolicitarOtpUseCase> logger)
{
    /// <summary>Janela de idempotência — double-tap do "Reenviar" dentro disso reaproveita o OTP.</summary>
    public static readonly TimeSpan JanelaIdempotencia = TimeSpan.FromSeconds(60);

    /// <summary>Janela do rate limit anti-abuso.</summary>
    public static readonly TimeSpan JanelaRateLimit = TimeSpan.FromHours(1);

    /// <summary>Cota máxima de OTPs por janela (incl. consumidos/expirados).</summary>
    public const int MaxOtpsPorJanela = 3;

    /// <summary>
    /// E.164 BR: <c>+55</c> + DDD (2 dígitos) + número (8 ou 9 dígitos) =
    /// 13 ou 14 chars total.
    /// </summary>
    private static readonly Regex TelefoneE164BrRegex =
        new(@"^\+55[1-9][0-9]\d{8,9}$", RegexOptions.Compiled);

    public async Task<SolicitarOtpResult> ExecuteAsync(SolicitarOtpInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // ── 1. Normalizar + validar telefone ────────────────────────────
        var telefoneE164 = NormalizarTelefone(input.Telefone);
        var telefoneMascarado = MascararTelefone(telefoneE164);

        // ── 2. Resolver storefront → empresaId ──────────────────────────
        var storefront = await storefrontRepository.GetBySlugAsync(input.Slug);
        if (storefront is null || !storefront.Ativo)
        {
            logger.LogWarning(
                "Solicitacao de OTP para storefront inexistente/inativo: slug={Slug} telefone={Telefone} ip={Ip} idempotencyKey={IdempotencyKey}",
                input.Slug, telefoneMascarado, input.IpOrigem, input.IdempotencyKey);
            throw new StorefrontNaoEncontradoException(input.Slug);
        }

        var empresaId = storefront.EmpresaId;
        var telefoneHash = ClienteOtp.CalcularTelefoneHash(telefoneE164);
        var agora = timeProvider.GetUtcNow().UtcDateTime;

        // ── 3. Idempotência (janela 60s) ────────────────────────────────
        var otpExistente = await clienteOtpRepository.GetAtivoPorTelefoneHashAsync(
            empresaId, telefoneHash, agora);
        if (otpExistente is not null && (agora - otpExistente.CriadoEm) < JanelaIdempotencia)
        {
            var ttlRestante = Math.Max(0, (int)(otpExistente.ExpiraEm - agora).TotalSeconds);
            logger.LogInformation(
                "OTP reaproveitado (idempotencia <60s). empresaId={EmpresaId} telefone={Telefone} idempotencyKey={IdempotencyKey} ttlRestante={Ttl}s",
                empresaId, telefoneMascarado, input.IdempotencyKey, ttlRestante);
            return new SolicitarOtpResult(ttlRestante, Reaproveitado: true);
        }

        // ── 4. Rate limit (3/hora) ──────────────────────────────────────
        var desde = agora - JanelaRateLimit;
        var emitidos = await clienteOtpRepository.ContarCriadosDesdeAsync(
            empresaId, telefoneHash, desde);
        if (emitidos >= MaxOtpsPorJanela)
        {
            var retryAfter = CalcularRetryAfterSegundos(otpExistente, agora);
            logger.LogWarning(
                "Rate limit atingido. empresaId={EmpresaId} telefone={Telefone} emitidosNaJanela={Count} retryAfter={Retry}s",
                empresaId, telefoneMascarado, emitidos, retryAfter);
            throw new OtpRateLimitExcedidoException(retryAfter);
        }

        // ── 5. Gerar código 6 dígitos via RNG seguro ────────────────────
        var codigo = GerarCodigo6Digitos();
        var codigoHash = passwordHasher.Hash(codigo);

        // ── 6. Criar entity + persistir + commit ────────────────────────
        var otp = ClienteOtp.Criar(
            empresaId: empresaId,
            telefoneHash: telefoneHash,
            codigoHash: codigoHash,
            time: timeProvider,
            ipOrigem: input.IpOrigem,
            userAgent: input.UserAgent);

        await clienteOtpRepository.AddAsync(otp);
        await unitOfWork.CommitAsync();

        var ttl = (int)ClienteOtp.TempoVidaPadrao.TotalSeconds;
        logger.LogInformation(
            "OTP gerado. empresaId={EmpresaId} telefone={Telefone} otpId={OtpId} ip={Ip} idempotencyKey={IdempotencyKey} ttl={Ttl}s",
            empresaId, telefoneMascarado, otp.Id, input.IpOrigem, input.IdempotencyKey, ttl);

        // ── 7. Enviar (fora da transação) — failure não desfaz persist ──
        await whatsAppOtpSender.EnviarOtpAsync(telefoneE164, codigo);

        return new SolicitarOtpResult(ttl, Reaproveitado: false);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Aceita formatos comuns:
    /// <c>"+5511997573992"</c>, <c>"(11) 99757-3992"</c>, <c>"11 99757-3992"</c>,
    /// <c>"+55 11 9 9757 3992"</c>. Lança <see cref="TelefoneInvalidoException"/>
    /// se não bater <see cref="TelefoneE164BrRegex"/> após normalização.
    /// </summary>
    private static string NormalizarTelefone(string telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone))
            throw new TelefoneInvalidoException();

        // Mantém apenas dígitos e o '+' inicial (se houver). Espaços, hífens,
        // parênteses, pontos somem.
        var span = telefone.Trim();
        var digitos = new System.Text.StringBuilder(span.Length);
        var primeiro = true;
        foreach (var c in span)
        {
            if (primeiro && c == '+')
            {
                digitos.Append('+');
            }
            else if (char.IsDigit(c))
            {
                digitos.Append(c);
            }
            else if (c is ' ' or '(' or ')' or '-' or '.')
            {
                // skip
            }
            else
            {
                // caractere inválido — não-dígito, não-separador comum
                throw new TelefoneInvalidoException();
            }
            primeiro = false;
        }

        var normalizado = digitos.ToString();

        // Aceita também input sem prefixo: "11997573992" → "+5511997573992"
        if (!normalizado.StartsWith('+'))
        {
            // Só aceita se o usuário já incluiu o 55 implicitamente OU se for um
            // DDD + número BR puro (10-11 dígitos).
            if (normalizado.Length is 10 or 11)
                normalizado = "+55" + normalizado;
            else
                throw new TelefoneInvalidoException();
        }

        if (!TelefoneE164BrRegex.IsMatch(normalizado))
            throw new TelefoneInvalidoException();

        return normalizado;
    }

    /// <summary>
    /// Mascarar para logs: <c>+5511997573992</c> → <c>+5511*****3992</c>.
    /// Preserva código país + DDD + 4 últimos dígitos.
    /// </summary>
    private static string MascararTelefone(string telefoneE164)
    {
        // +55 + DDD (2) = 5 chars iniciais; 4 últimos preservados; meio mascarado.
        if (telefoneE164.Length < 9)
            return "+55*****";
        var inicio = telefoneE164[..5];
        var fim = telefoneE164[^4..];
        return $"{inicio}*****{fim}";
    }

    /// <summary>
    /// Gera 6 dígitos numéricos usando <see cref="RandomNumberGenerator"/>
    /// (criptograficamente seguro — NÃO usar <see cref="Random"/>).
    /// </summary>
    private static string GerarCodigo6Digitos()
    {
        // 0..999_999 uniformemente distribuído via rejection sampling.
        // 4 bytes = 32 bits = ~4.29e9; descartamos valores que cairiam em bucket
        // truncado para evitar bias.
        const uint Limite = 1_000_000u;
        const uint Maximo = uint.MaxValue - (uint.MaxValue % Limite);
        Span<byte> buffer = stackalloc byte[4];
        uint valor;
        do
        {
            RandomNumberGenerator.Fill(buffer);
            valor = BitConverter.ToUInt32(buffer);
        } while (valor >= Maximo);

        return (valor % Limite).ToString("D6");
    }

    /// <summary>
    /// Quando bater rate limit, sugere quando o cliente pode tentar de novo:
    /// até o OTP mais antigo expirar + 1s de folga (best-effort sem query
    /// adicional).
    /// </summary>
    private static int CalcularRetryAfterSegundos(ClienteOtp? otpMaisRecente, DateTime agora)
    {
        // Fallback: até completar a janela do rate limit (1h).
        if (otpMaisRecente is null)
            return (int)JanelaRateLimit.TotalSeconds;

        var ate = otpMaisRecente.ExpiraEm;
        var segundos = (int)Math.Max(1, (ate - agora).TotalSeconds);
        return Math.Min(segundos, (int)JanelaRateLimit.TotalSeconds);
    }
}
