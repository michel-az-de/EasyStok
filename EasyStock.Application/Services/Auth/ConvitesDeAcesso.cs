using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Auth;

/// <summary>
/// Emissão e revogação do convite de primeiro acesso (N9). Um token por canal (e-mail sempre; WhatsApp só com telefone e o
/// atestado da dona), 32 bytes em base64url, 72 h, só o hash vai ao banco. Sai UM evento <c>ConviteAcesso</c>, e o modo
/// <c>todos</c> do motor entrega cada segredo pelo seu canal. Sem atestado o payload restringe a <c>Email</c> e nenhum token de
/// WhatsApp existe. Não faz commit: o use case que chama faz, na mesma transação do evento (ADR-0030). Nenhum log leva
/// e-mail, telefone, link ou token: só o <c>UsuarioId</c>.
/// </summary>
public sealed class ConvitesDeAcesso(
    IResetTokenRepository tokens,
    INotificadorService notificador,
    IConsentimentoRepository consentimentos,
    IEmpresaRepository empresas,
    IConfiguration configuration,
    TimeProvider relogio,
    ILogger<ConvitesDeAcesso> logger)
{
    public static readonly TimeSpan Validade = TimeSpan.FromHours(72);

    /// <summary>Reenvios (e pedidos de "esqueci a senha" de quem ainda não aceitou) por usuário na última hora.</summary>
    public const int EmissoesPorHora = 3;

    public const string CanalEmail = "Email";
    public const string CanalWhatsApp = "WhatsApp";

    /// <summary>Chave do payload com o token do botão URL do modelo <c>convite_acesso_link</c> (só quando há atestado).</summary>
    public const string ChaveTokenWhatsApp = "token_convite_whatsapp";

    /// <summary>Recusa cedo, antes de gravar qualquer coisa, quando não há base de link segura configurada.</summary>
    public void ExigirBaseDeLink()
    {
        if (LinkConviteAcesso.Montar(configuration, "teste") is not null) return;

        logger.LogError(
            "Convite sem base de link: configure {Chave} ou uma origem em Auth:TrustedLinkOrigins.", LinkConviteAcesso.Chave);
        throw new UseCaseValidationException(
            "CONVITE_SEM_LINK", $"Não há link seguro para o convite: configure {LinkConviteAcesso.Chave}.");
    }

    /// <summary>
    /// Pessoa que pode receber o convite por WhatsApp: tem telefone e a dona atestou que ela aceitou receber
    /// (<c>ConsentimentoNotificacao</c> de WhatsApp em Segurança, com opt-in). Não exige telefone verificado: quem verifica é o aceite.
    /// </summary>
    public async Task<bool> ElegivelAoWhatsAppAsync(Usuario usuario, CancellationToken ct = default)
    {
        if (usuario.Telefone is null) return false;

        var atuais = await consentimentos.ListarPorUsuariosAsync([usuario.Id], ct);
        return atuais.Any(c => c.UsuarioId == usuario.Id && c.Canal == CanalNotificacao.WhatsApp
                               && c.Categoria == CategoriaConteudoNotificacao.Seguranca && c.OptIn);
    }

    /// <summary>Quantas emissões o usuário teve na última hora (as linhas <c>Convite</c> de e-mail).</summary>
    public Task<int> EmissoesNaUltimaHoraAsync(Guid usuarioId) =>
        tokens.ContarEmissoesDeConviteAsync(usuarioId, relogio.GetUtcNow().UtcDateTime.AddHours(-1));

    /// <summary>Revoga os convites abertos do usuário (todos os canais) e devolve quantos caíram.</summary>
    public Task<int> RevogarAbertosAsync(Guid usuarioId) => tokens.InvalidarConvitesAbertosAsync(usuarioId);

    /// <summary>
    /// Emite o convite: revoga os abertos (salvo <paramref name="revogarAbertos"/> falso, usuário recém-criado), grava um
    /// token por canal e enfileira o evento. Quem chama confere a base de link antes (<see cref="ExigirBaseDeLink"/>).
    /// </summary>
    /// <param name="comWhatsApp">Cria também o token do WhatsApp. Só passe verdadeiro com telefone e atestado.</param>
    public async Task EmitirAsync(
        Usuario usuario, Guid empresaId, bool comWhatsApp, string? ip, string? userAgent, bool revogarAbertos = true,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        ExigirBaseDeLink();

        var agora = relogio.GetUtcNow().UtcDateTime;
        var comCanalWhatsApp = comWhatsApp && usuario.Telefone is not null;

        if (revogarAbertos) await tokens.InvalidarConvitesAbertosAsync(usuario.Id);

        var textoEmail = SegredosDeAcesso.GerarLink();
        var link = LinkConviteAcesso.Montar(configuration, textoEmail)!;
        var tokenEmail = ResetToken.Criar(
            usuario.Id, SegredosDeAcesso.HashDoLink(textoEmail), agora + Validade, ip, userAgent,
            FinalidadeResetToken.Convite, canal: CanalEmail, criadoEm: agora);
        await tokens.AddAsync(tokenEmail);

        var payload = new Dictionary<string, object?>
        {
            ["usuarioId"] = usuario.Id,
            ["nome"] = usuario.Nome,
            ["email"] = usuario.Email,
            ["empresa"] = await NomeDaEmpresaAsync(empresaId),
            ["link_convite"] = link,
            ["expira_em_dias"] = (int)Validade.TotalDays,
            [NotificadorService.ChaveIdempotenciaPayload] = $"convite:{tokenEmail.Id:N}",
        };

        if (comCanalWhatsApp)
        {
            var textoWhats = SegredosDeAcesso.GerarLink();
            await tokens.AddAsync(ResetToken.Criar(
                usuario.Id, SegredosDeAcesso.HashDoLink(textoWhats), agora + Validade, ip, userAgent,
                FinalidadeResetToken.Convite, canal: CanalWhatsApp, criadoEm: agora));
            payload[ChaveTokenWhatsApp] = textoWhats;
        }
        else
        {
            payload[CanaisDaRotina.CanaisPayload] = new[] { nameof(CanalNotificacao.Email) };
        }

        await notificador.EnfileirarEventoAsync(
            TipoEventoNotificacao.ConviteAcesso, empresaId, JsonSerializer.Serialize(payload), ct: ct);

        logger.LogInformation(
            "Convite emitido para o usuario {UsuarioId} (whatsapp: {ComWhatsApp})", usuario.Id, comCanalWhatsApp);
    }

    private async Task<string> NomeDaEmpresaAsync(Guid empresaId)
    {
        var empresa = await empresas.GetByIdAsync(empresaId);
        var nome = empresa is null ? null : string.IsNullOrWhiteSpace(empresa.NomeFantasia) ? empresa.Nome : empresa.NomeFantasia;
        return string.IsNullOrWhiteSpace(nome) ? "EasyStok" : nome.Trim();
    }
}
