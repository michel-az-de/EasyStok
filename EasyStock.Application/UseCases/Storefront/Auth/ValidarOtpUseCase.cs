using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.Extensions.Logging;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.UseCases.Storefront.Auth;

/// <summary>
/// Use case do segundo passo do fluxo de autenticação storefront (ADR-0012):
/// cliente envia código OTP recebido via WhatsApp → backend valida → cria
/// <c>ClienteSession</c> server-side → controller seta cookie <c>__Host-cdb_session</c>.
///
/// <para>
/// <strong>Segurança anti-enumeração</strong>: OTP não encontrado e código
/// incorreto retornam a mesma <see cref="OtpInvalidoException"/> com a mesma
/// mensagem genérica — não confirma se o telefone existe.
/// </para>
///
/// <para>
/// <strong>Anti-brute force</strong>: cada tentativa incorreta incrementa
/// <c>ClienteOtp.Tentativas</c>. Na 5ª tentativa <see cref="ClienteOtp.RegistrarTentativa"/>
/// lança <see cref="RegraDeDominioVioladaException"/> que mapeia para
/// <see cref="OtpTentativasExcedidasException"/>.
/// </para>
///
/// <para>
/// <strong>Fingerprint de sessão</strong>: SHA-256(UA + Accept-Language) gravado
/// em <see cref="ClienteSession.Fingerprint"/>. Middleware valida a cada request
/// autenticado — divergência força re-login (heurística anti-hijacking).
/// </para>
///
/// <para>
/// <strong>Logging seguro</strong>: telefone mascarado (<c>+5511*****1234</c>);
/// código OTP NUNCA aparece em log/response/exception.
/// </para>
/// </summary>
public sealed class ValidarOtpUseCase(
    IStorefrontRepository storefrontRepository,
    IClienteOtpRepository clienteOtpRepository,
    IClienteStorefrontRepository clienteRepository,
    IClienteSessionRepository clienteSessionRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ValidarOtpUseCase> logger)
{
    public const int MaxAgeSecs = 60 * 60 * 24 * 30; // 30 dias

    private static readonly Regex TelefoneE164BrRegex =
        new(@"^\+55[1-9][0-9]\d{8,9}$", RegexOptions.Compiled);

    public async Task<ValidarOtpResult> ExecuteAsync(ValidarOtpInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // ── 1. Normalizar + validar telefone ────────────────────────────
        var telefoneE164 = NormalizarTelefone(input.Telefone);
        var telefoneMascarado = MascararTelefone(telefoneE164);

        // ── 2. Resolver storefront → empresaId ──────────────────────────
        var storefront = await storefrontRepository.GetBySlugAsync(input.Slug, ct);
        if (storefront is null || !storefront.Ativo)
        {
            logger.LogWarning(
                "ValidarOtp para storefront inexistente/inativo: slug={Slug} telefone={Telefone}",
                input.Slug, telefoneMascarado);
            throw new StorefrontNaoEncontradoException(input.Slug);
        }

        var empresaId = storefront.EmpresaId;
        var telefoneHash = ClienteOtp.CalcularTelefoneHash(telefoneE164);
        var agora = timeProvider.GetUtcNow().UtcDateTime;

        // ── 3. Buscar OTP ativo ────────────────────────────────────────
        var otp = await clienteOtpRepository.GetAtivoPorTelefoneHashAsync(
            empresaId, telefoneHash, agora, ct);

        if (otp is null)
        {
            logger.LogWarning(
                "OTP não encontrado: empresaId={EmpresaId} telefone={Telefone}",
                empresaId, telefoneMascarado);
            throw new OtpInvalidoException();
        }

        // ── 4. Verificar expiração ─────────────────────────────────────
        if (otp.Expirou(timeProvider))
        {
            logger.LogInformation(
                "OTP expirado: empresaId={EmpresaId} otpId={OtpId} telefone={Telefone}",
                empresaId, otp.Id, telefoneMascarado);
            throw new OtpExpiradoException();
        }

        // ── 5. Verificar código ────────────────────────────────────────
        if (!otp.ValidarCodigo(input.Codigo, passwordHasher.Verify, timeProvider))
        {
            // Registra tentativa — lança RegraDeDominioVioladaException se já estava no limite
            try
            {
                otp.RegistrarTentativa();
            }
            catch (Domain.Exceptions.RegraDeDominioVioladaException)
            {
                await clienteOtpRepository.UpdateAsync(otp, ct);
                await unitOfWork.CommitAsync();
                logger.LogWarning(
                    "OTP tentativas esgotadas: empresaId={EmpresaId} otpId={OtpId} telefone={Telefone}",
                    empresaId, otp.Id, telefoneMascarado);
                throw new OtpTentativasExcedidasException();
            }

            await clienteOtpRepository.UpdateAsync(otp, ct);
            await unitOfWork.CommitAsync();

            // Após incrementar, verifica se acabou de atingir o limite
            if (otp.Tentativas >= ClienteOtp.MaxTentativas)
            {
                logger.LogWarning(
                    "OTP tentativas esgotadas (última tentativa): empresaId={EmpresaId} otpId={OtpId} telefone={Telefone}",
                    empresaId, otp.Id, telefoneMascarado);
                throw new OtpTentativasExcedidasException();
            }

            logger.LogWarning(
                "Código OTP incorreto: empresaId={EmpresaId} otpId={OtpId} tentativas={Tentativas} telefone={Telefone}",
                empresaId, otp.Id, otp.Tentativas, telefoneMascarado);
            throw new OtpInvalidoException();
        }

        // ── 6. Consumir OTP ────────────────────────────────────────────
        otp.Consumir();
        await clienteOtpRepository.UpdateAsync(otp, ct);

        // ── 7. Criar ou atualizar Cliente ──────────────────────────────
        var cliente = await clienteRepository.GetByTelefoneHashAsync(empresaId, telefoneHash, ct);
        if (cliente is null)
        {
            cliente = ClienteEntity.CriarParaStorefront(empresaId, telefoneHash, timeProvider);
            await clienteRepository.AddAsync(cliente, ct);
            logger.LogInformation(
                "Novo cliente storefront criado: empresaId={EmpresaId} clienteId={ClienteId} telefone={Telefone}",
                empresaId, cliente.Id, telefoneMascarado);
        }
        else
        {
            cliente.RegistrarAcessoStorefront(timeProvider);
            await clienteRepository.UpdateAsync(cliente, ct);
        }

        // ── 8. Criar ClienteSession com fingerprint ────────────────────
        var fingerprint = ClienteFingerprintCalculator.Calcular(input.UserAgent, input.AcceptLanguage);
        var session = ClienteSession.Criar(
            clienteId: cliente.Id,
            empresaId: empresaId,
            time: timeProvider,
            ipInicial: input.IpOrigem,
            uaInicial: input.UserAgent,
            fingerprint: fingerprint);

        await clienteSessionRepository.AddAsync(session, ct);
        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Sessão storefront criada: empresaId={EmpresaId} clienteId={ClienteId} sessionId={SessionId} telefone={Telefone}",
            empresaId, cliente.Id, session.Id, telefoneMascarado);

        return new ValidarOtpResult(
            SessionId: session.Id,
            TelefoneOfuscado: telefoneMascarado,
            PrimeiroNome: string.IsNullOrWhiteSpace(cliente.Nome) ? "Olá" : cliente.Nome.Split(' ')[0],
            MaxAgeSecs: MaxAgeSecs);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string NormalizarTelefone(string telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone))
            throw new TelefoneInvalidoException();

        var span = telefone.Trim();
        var digitos = new System.Text.StringBuilder(span.Length);
        var primeiro = true;
        foreach (var c in span)
        {
            if (primeiro && c == '+')
                digitos.Append('+');
            else if (char.IsDigit(c))
                digitos.Append(c);
            else if (c is ' ' or '(' or ')' or '-' or '.')
            { /* skip */ }
            else
                throw new TelefoneInvalidoException();

            primeiro = false;
        }

        var normalizado = digitos.ToString();
        if (!normalizado.StartsWith('+'))
        {
            if (normalizado.Length is 10 or 11)
                normalizado = "+55" + normalizado;
            else
                throw new TelefoneInvalidoException();
        }

        if (!TelefoneE164BrRegex.IsMatch(normalizado))
            throw new TelefoneInvalidoException();

        return normalizado;
    }

    private static string MascararTelefone(string telefoneE164)
    {
        if (telefoneE164.Length < 9)
            return "+55*****";
        var inicio = telefoneE164[..5];
        var fim = telefoneE164[^4..];
        return $"{inicio}*****{fim}";
    }
}
