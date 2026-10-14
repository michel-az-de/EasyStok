using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Application.UseCases.Atendimento;

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
/// <strong>Anti-brute force</strong>: cada tentativa é reservada em <c>ClienteOtp.Tentativas</c> num
/// UPDATE atômico, com o limite no WHERE, antes de conferir o código (#1781): rajada paralela não passa de
/// <see cref="ClienteOtp.MaxTentativas"/>. Sem tentativa a reservar, ou errando a última, responde
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
    IdentificarClientePorTelefoneUseCase identificarCliente,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ValidarOtpUseCase> logger)
{
    public const int MaxAgeSecs = 60 * 60 * 24 * 30; // 30 dias

    public async Task<ValidarOtpResult> ExecuteAsync(ValidarOtpInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // ── 1. Normalizar + validar telefone ────────────────────────────
        var telefoneE164 = NormalizadorTelefone.NormalizarE164Br(input.Telefone);
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
        // #1781: a tentativa é contada no banco, num UPDATE atômico, antes do BCrypt; rajada paralela não passa
        // do limite. Recusada: as tentativas acabaram (ou outra requisição consumiu o OTP).
        if (!await clienteOtpRepository.ReservarTentativaAsync(otp.Id, ct))
        {
            logger.LogWarning(
                "OTP tentativas esgotadas: empresaId={EmpresaId} otpId={OtpId} telefone={Telefone}",
                empresaId, otp.Id, telefoneMascarado);
            throw new OtpTentativasExcedidasException();
        }

        if (!otp.ValidarCodigo(input.Codigo, passwordHasher.Verify, timeProvider))
        {
            // #1781: a reserva já gravou a tentativa; a entidade só espelha. Gravar o OTP lido antes da reserva
            // devolveria o contador ao valor velho e perderia as tentativas paralelas.
            otp.RegistrarTentativa();

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
        // #1781: consumo atômico no banco. Dois códigos certos em paralelo: só um afeta a linha; o outro
        // responde como código inválido, sem sessão e sem tentar criar o cliente de novo.
        if (!await clienteOtpRepository.ConsumirAsync(otp.Id, ct))
        {
            logger.LogWarning(
                "OTP já consumido por outra requisição: empresaId={EmpresaId} otpId={OtpId} telefone={Telefone}",
                empresaId, otp.Id, telefoneMascarado);
            throw new OtpInvalidoException();
        }

        // ── 7. Criar ou atualizar Cliente ──────────────────────────────
        var cliente = await clienteRepository.GetByTelefoneHashAsync(empresaId, telefoneHash, ct);
        var clienteNovo = false;
        if (cliente is null)
        {
            // Só após comprovar posse pelo OTP pode reutilizar o cadastro do WhatsApp/ERP.
            var identificacao = await identificarCliente.ExecuteAsync(
                new IdentificarClientePorTelefoneInput(empresaId, telefoneE164.TrimStart('+'), null), ct);
            cliente = identificacao.Cliente;
            clienteNovo = identificacao.EhNovo;
            logger.LogInformation(
                "Novo cliente storefront criado: empresaId={EmpresaId} clienteId={ClienteId} telefone={Telefone}",
                empresaId, cliente.Id, telefoneMascarado);
        }
        if (cliente.EmpresaId != empresaId)
            throw new OtpInvalidoException();
        cliente.RegistrarAcessoStorefront(timeProvider);
        if (!clienteNovo)
            await clienteRepository.UpdateAsync(cliente, ct);

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
            "Sessão storefront criada: empresaId={EmpresaId} clienteId={ClienteId} telefone={Telefone}",
            empresaId, cliente.Id, telefoneMascarado);

        return new ValidarOtpResult(
            SessionId: session.Id,
            TelefoneOfuscado: telefoneMascarado,
            PrimeiroNome: (string.IsNullOrWhiteSpace(cliente.Nome) || cliente.Nome == IdentificarClientePorTelefoneUseCase.NomePadraoLead) ? "Olá" : cliente.Nome.Split(' ')[0],
            MaxAgeSecs: MaxAgeSecs);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string MascararTelefone(string telefoneE164)
    {
        if (telefoneE164.Length < 9)
            return "+55*****";
        var inicio = telefoneE164[..5];
        var fim = telefoneE164[^4..];
        return $"{inicio}*****{fim}";
    }
}
