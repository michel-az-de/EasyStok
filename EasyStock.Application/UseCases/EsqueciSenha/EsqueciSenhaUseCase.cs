using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.UseCases.EsqueciSenha;

/// <summary>
/// Pedido de redefinição de senha (N8). Responde sempre o mesmo, exista a conta ou não, e nunca faz rede: só banco.
/// Aplica os limites (IP 5 em 15 min; conta 3 por hora, 6 por dia e 60 s entre pedidos), invalida os segredos
/// anteriores, grava o link (sempre) e o código (só para conta elegível) e enfileira UM evento <c>ResetSenha</c> no motor,
/// na mesma transação (ADR-0030). O modo <c>todos</c> da rotina entrega o link por e-mail e o código por WhatsApp.
/// Nenhum log leva e-mail, telefone, link ou código: só o <c>UsuarioId</c>.
/// </summary>
public sealed class EsqueciSenhaUseCase(
    IUsuarioRepository usuarioRepository,
    IResetTokenRepository resetTokenRepository,
    IAuditLogRepository auditLogRepository,
    IConsentimentoRepository consentimentoRepository,
    INotificadorService notificador,
    EmpresaDoEventoAnonimo empresaDoEvento,
    LimitePedidosAcesso limitePorIp,
    ConvitesDeAcesso convites,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    TimeProvider relogio,
    ILogger<EsqueciSenhaUseCase> logger) : IUseCase<EsqueciSenhaCommand, EsqueciSenhaResult>
{
    public static readonly TimeSpan ValidadeDoLink = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ValidadeDoCodigo = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan IntervaloEntrePedidos = TimeSpan.FromSeconds(60);
    public const int PedidosPorHora = 3;
    public const int PedidosPorDia = 6;

    public async Task<EsqueciSenhaResult> ExecuteAsync(EsqueciSenhaCommand command)
    {
        // Teto por IP: a única resposta que difere (429) não depende da conta.
        await limitePorIp.ExigirAsync(command.Ip);

        var resposta = new EsqueciSenhaResult(true);

        // Formato inválido, conta inexistente e conta inativa recebem a mesma resposta, sem efeito colateral.
        if (!EmailValidator.IsValid(command.Email)) return resposta;

        var usuario = await usuarioRepository.GetByEmailAsync(command.Email);
        if (usuario is null || !usuario.Ativo) return resposta;

        var agora = relogio.GetUtcNow().UtcDateTime;

        // N9: quem ainda nao aceitou o convite nao tem senha para redefinir. O pedido reemite o convite e nunca gera
        // token de reset: o reset so vale para quem ja aceitou (um caminho paralelo ao aceite, sem verificar o canal).
        if (usuario.ConvitePendente)
        {
            await ReemitirConviteAsync(usuario, command);
            return resposta;
        }

        var motivoDoLimite = await MotivoDoLimiteDeContaAsync(usuario, agora);
        if (motivoDoLimite is not null)
        {
            await auditLogRepository.AddAsync(AuditLog.Criar(
                usuario.Id, "forgot-password-limitado", false, $"Limite por conta: {motivoDoLimite}", command.Ip, command.UserAgent));
            await unitOfWork.CommitAsync();
            logger.LogWarning("Pedido de redefinicao limitado para o usuario {UsuarioId} ({Motivo})", usuario.Id, motivoDoLimite);
            return resposta;
        }

        var linkTexto = SegredosDeAcesso.GerarLink();
        var link = LinkRedefinicaoSenha.Montar(configuration, linkTexto);
        if (link is null)
        {
            logger.LogError(
                "Pedido de redefinicao do usuario {UsuarioId} sem base de link: configure {Chave} ou uma origem em Auth:TrustedLinkOrigins.",
                usuario.Id, LinkRedefinicaoSenha.Chave);
            return resposta;
        }

        var empresaId = await empresaDoEvento.ResolverAsync(usuario);
        if (empresaId is null) return resposta;

        var comCodigo = await ElegivelAoCodigoAsync(usuario);

        // Só depois de saber que o evento sai: invalidar antes de ter o que enviar deixaria a conta sem segredo vivo.
        await resetTokenRepository.InvalidarAbertosAsync(usuario.Id, agora);

        var tokenDoLink = ResetToken.Criar(
            usuario.Id, SegredosDeAcesso.HashDoLink(linkTexto), agora + ValidadeDoLink, command.Ip, command.UserAgent,
            FinalidadeResetToken.Reset, canal: "Email", criadoEm: agora);
        await resetTokenRepository.AddAsync(tokenDoLink);

        string? codigo = null;
        if (comCodigo)
        {
            codigo = SegredosDeAcesso.GerarCodigo();
            var id = Guid.NewGuid();
            await resetTokenRepository.AddAsync(ResetToken.Criar(
                usuario.Id, SegredosDeAcesso.HashDoCodigo(id, codigo), agora + ValidadeDoCodigo, command.Ip, command.UserAgent,
                FinalidadeResetToken.ResetCodigo, canal: "WhatsApp", id: id, criadoEm: agora));
        }

        await auditLogRepository.AddAsync(AuditLog.Criar(
            usuario.Id, "forgot-password", true,
            comCodigo ? "Link e codigo de redefinicao gerados" : "Link de redefinicao gerado", command.Ip, command.UserAgent));

        var payload = new Dictionary<string, object?>
        {
            ["usuarioId"] = usuario.Id,
            ["nome"] = usuario.Nome,
            ["email"] = usuario.Email,
            ["link_redefinicao"] = link,
            ["expira_em_minutos"] = (int)ValidadeDoLink.TotalMinutes,
            [NotificadorService.ChaveIdempotenciaPayload] = $"reset-senha:{tokenDoLink.Id:N}",
        };
        if (comCodigo)
        {
            payload["codigo"] = codigo;
            payload["codigo_expira_em_minutos"] = (int)ValidadeDoCodigo.TotalMinutes;
        }
        else
        {
            payload[CanaisDaRotina.CanaisPayload] = new[] { nameof(CanalNotificacao.Email) };
        }

        await notificador.EnfileirarEventoAsync(
            TipoEventoNotificacao.ResetSenha, empresaId.Value, JsonSerializer.Serialize(payload));

        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Redefinicao de senha pedida pelo usuario {UsuarioId} (codigo por WhatsApp: {ComCodigo})", usuario.Id, comCodigo);
        return resposta;
    }

    /// <summary>
    /// Convite novo para o pendente que pediu "esqueci a senha". Respeita as 3 emissoes por hora do convite (sem aviso: a
    /// resposta e a mesma de qualquer conta) e nunca vale para superadmin, que nao nasce por convite.
    /// </summary>
    private async Task ReemitirConviteAsync(Usuario usuario, EsqueciSenhaCommand command)
    {
        if (usuario.EhSuperAdmin()) return;

        if (await convites.EmissoesNaUltimaHoraAsync(usuario.Id) >= ConvitesDeAcesso.EmissoesPorHora)
        {
            logger.LogWarning("Reenvio de convite limitado para o usuario {UsuarioId} (3 por hora)", usuario.Id);
            return;
        }

        var empresaId = await empresaDoEvento.ResolverAsync(usuario);
        if (empresaId is null) return;

        try
        {
            convites.ExigirBaseDeLink();
        }
        catch (UseCaseValidationException)
        {
            return; // sem base de link nao ha o que enviar, e a resposta nao pode revelar isso
        }

        var comWhatsApp = await convites.ElegivelAoWhatsAppAsync(usuario);
        await convites.EmitirAsync(usuario, empresaId.Value, comWhatsApp, command.Ip, command.UserAgent);
        await auditLogRepository.AddAsync(AuditLog.Criar(
            usuario.Id, "forgot-password-convite", true, "Convite reemitido no lugar da redefinicao", command.Ip, command.UserAgent));
        await unitOfWork.CommitAsync();
    }

    /// <summary>Por conta: 3 por hora, 6 por dia e 60 s entre pedidos, contados nas linhas <c>Reset</c> de <c>reset_tokens</c>.</summary>
    private async Task<string?> MotivoDoLimiteDeContaAsync(Usuario usuario, DateTime agora)
    {
        var contagem = await resetTokenRepository.ContarPedidosAsync(usuario.Id, agora);
        if (contagem.UltimoPedidoEm is { } ultimo && agora - ultimo < IntervaloEntrePedidos) return "intervalo de 60 s";
        if (contagem.NaUltimaHora >= PedidosPorHora) return "3 por hora";
        if (contagem.NasUltimas24Horas >= PedidosPorDia) return "6 por dia";
        return null;
    }

    /// <summary>
    /// Conta que pode receber o código por WhatsApp: ativa, nunca superadmin (o canal não é forte o bastante para a
    /// conta mais poderosa), telefone verificado, opt-in explícito em Segurança e WhatsApp de plataforma ligado.
    /// </summary>
    private async Task<bool> ElegivelAoCodigoAsync(Usuario usuario)
    {
        if (usuario.EhSuperAdmin()
            || usuario.Telefone is null
            || usuario.TelefoneVerificadoEm is null
            || string.IsNullOrWhiteSpace(configuration[ResolvedorAudiencia.ChaveWhatsAppPlataforma]))
            return false;

        var consentimentos = await consentimentoRepository.ListarPorUsuariosAsync([usuario.Id]);
        return consentimentos.Any(c => c.UsuarioId == usuario.Id && c.Canal == CanalNotificacao.WhatsApp
                                       && c.Categoria == CategoriaConteudoNotificacao.Seguranca && c.OptIn);
    }
}
