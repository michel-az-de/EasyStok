using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Auth;

/// <summary>
/// A regra única da troca de contato do usuário (N4): senha atual exigida (a errada conta como falha de login e responde
/// 403, não 401), troca de e-mail em duas etapas (<c>Usuario.EmailPendente</c>, confirmação no endereço novo, aviso mascarado
/// no antigo) e aviso de contato alterado. Serve ao <c>PATCH api/auth/me</c>, ao <c>PUT api/auth/me/telefone</c> e ao
/// <c>PUT api/usuarios/{id}</c> do Admin, para os três não divergirem. Não faz commit: o use case que chama faz.
/// </summary>
public sealed class TrocaDeContatoService(
    IUsuarioRepository usuarios,
    IEmailConfirmationTokenRepository tokens,
    INotificadorService notificador,
    IEmpresaPadraoResolver empresaPadrao,
    ITenantContextAccessor tenantContext,
    ICurrentUserAccessor usuarioAtual,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    TimeProvider relogio,
    ILogger<TrocaDeContatoService> logger)
{
    /// <summary>Validade do link de confirmação do endereço novo (a de <c>EmailConfirmationToken.Criar</c>).</summary>
    public const int ValidadeDoLinkEmHoras = 24;

    /// <summary>
    /// A empresa em que nasce o evento: a do token e, sem empresa (SuperAdmin), a empresa padrão da plataforma (N13), com o
    /// tenant ligado no escopo. Chame antes de ler ou gravar, e falha cedo quando não há onde publicar.
    /// </summary>
    public async Task<Guid> ResolverEmpresaDoEventoAsync(CancellationToken ct = default)
    {
        if (usuarioAtual.EmpresaId != Guid.Empty) return usuarioAtual.EmpresaId;

        var padrao = await empresaPadrao.ResolverAsync(ct);
        if (padrao is { } id && id != Guid.Empty)
        {
            tenantContext.SetCurrentTenant(id);
            return id;
        }

        logger.LogWarning(
            "Troca de contato sem empresa: o token não traz empresa e {Chave} não resolve.", EmpresaPadraoResolver.Chave);
        throw new UseCaseValidationException(
            "EMPRESA_PADRAO_NAO_RESOLVIDA",
            $"Sem empresa no token e sem empresa padrão: configure {EmpresaPadraoResolver.Chave} (CNPJ ou nome exato).");
    }

    /// <summary>
    /// Exige a senha atual. Ausente, ou com a conta bloqueada, ou errada: <see cref="UsuarioNaoAutorizadoException"/> (403). A
    /// errada conta como falha de login (cinco bloqueiam por 15 minutos, a regra do login) e é gravada antes de recusar.
    /// </summary>
    public async Task ExigirSenhaAtualAsync(Usuario usuario, string? senhaAtual)
    {
        if (string.IsNullOrWhiteSpace(senhaAtual))
            throw new UsuarioNaoAutorizadoException("Informe a senha atual para trocar o contato.");

        if (usuario.EstaBloqueado())
            throw new UsuarioNaoAutorizadoException("Conta temporariamente bloqueada por tentativas de senha. Tente mais tarde.");

        if (passwordHasher.Verify(senhaAtual, usuario.SenhaHash)) return;

        usuario.RegistrarFalhaDeSenha();
        await usuarios.UpdateAsync(usuario);
        await unitOfWork.CommitAsync();

        logger.LogWarning("Senha atual incorreta na troca de contato do usuario {UsuarioId}", usuario.Id);
        throw new UsuarioNaoAutorizadoException("Senha atual incorreta.");
    }

    /// <summary>
    /// Pede a troca de e-mail: valida o formato e a unicidade, grava <c>EmailPendente</c> (o <c>Email</c> e a confirmação
    /// ficam como estão), apaga os tokens de confirmação anteriores (o link só vale para o pendente mais recente) e
    /// enfileira a confirmação para o endereço NOVO e o aviso, com o endereço novo mascarado, para o ANTIGO.
    /// </summary>
    public async Task SolicitarTrocaDeEmailAsync(
        Usuario usuario, string novoEmail, string? baseUrl, Guid empresaId, CancellationToken ct = default)
    {
        EmailValidator.EnsureValid(novoEmail);
        var novo = novoEmail.Trim();

        var existente = await usuarios.GetByEmailAsync(novo);
        if (existente is not null && existente.Id != usuario.Id)
            throw new UseCaseValidationException("Email ja cadastrado.");

        var emailAntigo = usuario.Email;
        usuario.SolicitarTrocaDeEmail(novo);
        await usuarios.UpdateAsync(usuario);

        // Um novo pedido invalida o link anterior.
        await tokens.DeleteAllByUsuarioIdAsync(usuario.Id);
        var token = Guid.NewGuid().ToString();
        await tokens.AddAsync(EmailConfirmationToken.Criar(
            usuario.Id, TokenHashHelper.ComputeSha256Hash(token), usuarioAtual.Ip, usuarioAtual.UserAgent));

        // Nunca confiar no BaseUrl do corpo: só compõe o link quando o host está na allowlist (#765); sem ele, token puro.
        var baseConfiavel = LinkBaseUrlResolver.ResolveTrusted(baseUrl, configuration);
        if (!string.IsNullOrEmpty(baseUrl) && baseConfiavel is null)
            logger.LogWarning("BaseUrl da confirmacao de e-mail ignorado por nao estar na allowlist de origens confiaveis.");
        var link = baseConfiavel is not null
            ? $"{baseConfiavel}/auth/confirmar-email?token={Uri.EscapeDataString(token)}"
            : token;

        await notificador.EnfileirarEventoAsync(
            TipoEventoNotificacao.ConfirmacaoEmail, empresaId,
            Serializar(new Dictionary<string, object?>
            {
                ["nome"] = usuario.Nome,
                ["email"] = novo,
                ["link_confirmacao"] = link,
                ["expira_em_horas"] = ValidadeDoLinkEmHoras,
            }), ct: ct);

        await AvisarContatoAlteradoAsync(usuario, emailAntigo, "e-mail", MascararEmail(novo), empresaId, ct);
    }

    /// <summary>Enfileira o aviso de contato alterado (<c>ContatoAlterado</c>, categoria Segurança) para <paramref name="emailDoAviso"/>.</summary>
    public async Task AvisarContatoAlteradoAsync(
        Usuario usuario, string emailDoAviso, string contato, string novoMascarado, Guid empresaId, CancellationToken ct = default)
    {
        await notificador.EnfileirarEventoAsync(
            TipoEventoNotificacao.ContatoAlterado, empresaId,
            Serializar(new Dictionary<string, object?>
            {
                ["nome"] = usuario.Nome,
                ["email"] = emailDoAviso,
                ["contato"] = contato,
                ["novo_mascarado"] = novoMascarado,
                ["quando"] = FormatarInstante(relogio.GetUtcNow().UtcDateTime),
            }), ct: ct);
    }

    /// <summary><c>maria@casadababa.com</c> vira <c>m***@casadababa.com</c>: o aviso nunca revela o endereço novo inteiro.</summary>
    public static string MascararEmail(string email)
    {
        var arroba = email.IndexOf('@');
        if (arroba <= 0) return "***";
        return $"{email[0]}***{email[arroba..]}";
    }

    /// <summary><c>+5511997573992</c> vira <c>+55 11 *****-3992</c>: só o final e o DDD aparecem.</summary>
    public static string MascararTelefone(string e164)
    {
        var digitos = new string(e164.Where(char.IsDigit).ToArray());
        return digitos.Length < 6 ? "***" : $"+{digitos[..2]} {digitos[2..4]} *****-{digitos[^4..]}";
    }

    private static string Serializar(Dictionary<string, object?> payload) => JsonSerializer.Serialize(payload);

    private static string FormatarInstante(DateTime utc)
    {
        try
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
            return local.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return utc.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR")) + " UTC";
        }
    }
}
