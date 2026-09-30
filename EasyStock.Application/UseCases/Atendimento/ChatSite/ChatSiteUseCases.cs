using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.ChatSite;

public sealed record SessaoChatSiteAberta(Guid SessaoId, string Token, DateTime ExpiraEm);

/// <summary>Mensagem como o visitante vê: a dele ou a da loja. Notas internas não aparecem.</summary>
public sealed record MensagemChatSiteResult(Guid Id, bool DoVisitante, string? Texto, DateTime EnviadaEm)
{
    internal static MensagemChatSiteResult De(Mensagem m) =>
        new(m.Id, m.Direcao == DirecaoMensagem.Entrada, m.Texto, m.EnviadaEm);
}

/// <summary>Loja inexistente, inativa ou sem o chat ligado: 404, sem dizer qual dos três.</summary>
public sealed class ChatSiteIndisponivelException() : Exception("Chat indisponível nesta loja.");

/// <summary>Token ausente, inventado, vencido ou de outra loja: 403.</summary>
public sealed class SessaoChatSiteInvalidaException() : Exception("Sessão do chat inválida ou vencida.");

/// <summary>
/// Porta de entrada do chat do site (S36): resolve a loja pelo slug, confere as flags
/// (<see cref="FeatureCatalogo.ModuloAtendimento"/> e <see cref="FeatureCatalogo.CanalChatSite"/>),
/// liga o tenant da requisição anônima (RLS) e valida a sessão pelo hash do token.
/// </summary>
public sealed class AcessoChatSite(
    IStorefrontRepository storefrontRepository,
    ITenantFeatureFlagRepository featureFlagRepository,
    ITenantContextAccessor tenantContext,
    ISessaoChatSiteRepository sessaoRepository)
{
    public const int TokenBytes = 32;

    public async Task<StorefrontEntity> ResolverLojaAsync(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ChatSiteIndisponivelException();
        var loja = await storefrontRepository.GetBySlugAsync(slug.Trim(), ct);
        if (loja is null || !loja.Ativo)
            throw new ChatSiteIndisponivelException();

        tenantContext.SetCurrentTenant(loja.EmpresaId);
        var flags = await featureFlagRepository.ListarAtivasAsync(loja.EmpresaId, ct);
        if (!flags.Contains(FeatureCatalogo.ModuloAtendimento, StringComparer.OrdinalIgnoreCase)
            || !flags.Contains(FeatureCatalogo.CanalChatSite, StringComparer.OrdinalIgnoreCase))
            throw new ChatSiteIndisponivelException();

        return loja;
    }

    public async Task<SessaoChatSite> ResolverSessaoAsync(string slug, string? token, DateTime agora, CancellationToken ct)
    {
        var loja = await ResolverLojaAsync(slug, ct);
        if (string.IsNullOrWhiteSpace(token))
            throw new SessaoChatSiteInvalidaException();

        var sessao = await sessaoRepository.ObterPorTokenHashAsync(loja.EmpresaId, HashDoToken(token.Trim()), ct);
        if (sessao is null || sessao.StorefrontId != loja.Id || !sessao.EstaValida(agora))
            throw new SessaoChatSiteInvalidaException();
        return sessao;
    }

    public static string NovoToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string HashDoToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>S36: o visitante abre o chat. O token volta uma vez só; o banco guarda o hash.</summary>
public sealed class AbrirSessaoChatSiteUseCase(
    AcessoChatSite acesso, ISessaoChatSiteRepository sessaoRepository, IUnitOfWork unitOfWork)
{
    public async Task<SessaoChatSiteAberta> ExecuteAsync(string slug, CancellationToken ct = default)
    {
        var loja = await acesso.ResolverLojaAsync(slug, ct);
        var token = AcessoChatSite.NovoToken();
        var sessao = SessaoChatSite.Abrir(loja.EmpresaId, loja.Id, AcessoChatSite.HashDoToken(token), DateTime.UtcNow);
        await sessaoRepository.AddAsync(sessao, ct);
        await unitOfWork.CommitAsync();
        return new SessaoChatSiteAberta(sessao.Id, token, sessao.ExpiraEm);
    }
}

/// <summary>
/// S36: mensagem do visitante. Abre (ou reaproveita) a conversa do canal <c>ChatSite</c> e a põe na
/// fila humana sem responsável: o agente ainda envia direto pelo WhatsApp e não responde aqui.
/// </summary>
public sealed class EnviarMensagemVisitanteUseCase(
    AcessoChatSite acesso,
    IConversaRepository conversaRepository,
    IOperacaoEventPublisher eventPublisher,
    IUnitOfWork unitOfWork,
    ILogger<EnviarMensagemVisitanteUseCase> logger)
{
    public const int TextoTamanhoMaximo = 1000;

    public async Task<MensagemChatSiteResult> ExecuteAsync(string slug, string? token, string? texto, CancellationToken ct = default)
    {
        var limpo = texto?.Trim();
        if (string.IsNullOrEmpty(limpo))
            throw new UseCaseValidationException("Escreva a mensagem.");
        if (limpo.Length > TextoTamanhoMaximo)
            throw new UseCaseValidationException($"A mensagem passa de {TextoTamanhoMaximo} caracteres.");

        var agora = DateTime.UtcNow;
        var sessao = await acesso.ResolverSessaoAsync(slug, token, agora, ct);
        sessao.RegistrarUso(agora);

        var conversa = await ConversaDaSessaoAsync(sessao, agora, ct);
        var mensagem = Mensagem.Entrada(sessao.EmpresaId, conversa.Id, agora, TipoConteudoMensagem.Texto, limpo);
        conversa.RegistrarEntrada(agora);
        await conversaRepository.AddMensagemAsync(mensagem, ct);
        await unitOfWork.CommitAsync();

        try
        {
            await eventPublisher.PublicarAsync("conversa.mensagem_recebida", sessao.EmpresaId,
                new { conversaId = conversa.Id, mensagemId = mensagem.Id }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Chat do site: mensagem {MensagemId} gravada, aviso ao console falhou.", mensagem.Id);
        }

        return MensagemChatSiteResult.De(mensagem);
    }

    private async Task<Conversa> ConversaDaSessaoAsync(SessaoChatSite sessao, DateTime agora, CancellationToken ct)
    {
        if (sessao.ConversaId is { } id
            && await conversaRepository.ObterPorIdAsync(sessao.EmpresaId, id, ct) is { EstaAberta: true } existente)
            return existente;

        var conversa = await conversaRepository.ObterAbertaPorContatoAsync(sessao.EmpresaId, CanalConversa.ChatSite, sessao.ContatoIdExterno, ct);
        if (conversa is null)
        {
            conversa = Conversa.Abrir(sessao.EmpresaId, sessao.ContatoIdExterno, agora, contatoNome: "Visitante do site", canal: CanalConversa.ChatSite);
            conversa.Assumir(agora); // fila humana, sem responsável
            await conversaRepository.AddAsync(conversa, ct);
        }

        sessao.VincularConversa(conversa.Id);
        return conversa;
    }
}

/// <summary>
/// S36: o que o visitante vê depois de um instante (cursor). Só a mensagem dele e as enviadas pela loja
/// pelo canal (com id externo); nota interna do sistema fica de fora. Ler não renova a sessão.
/// </summary>
public sealed class ListarMensagensChatSiteUseCase(
    AcessoChatSite acesso, ISessaoChatSiteRepository sessaoRepository, IConversaRepository conversaRepository)
{
    public const int Limite = 50;

    public async Task<IReadOnlyList<MensagemChatSiteResult>> ExecuteAsync(
        string slug, string? token, DateTime? depoisDe, CancellationToken ct = default)
    {
        var sessao = await acesso.ResolverSessaoAsync(slug, token, DateTime.UtcNow, ct);
        return await ListarAsync(sessao, depoisDe, ct);
    }

    /// <summary>
    /// Para o stream: a loja foi resolvida na abertura; cada rodada relê a sessão pelo hash (a conversa
    /// nasce quando o visitante manda a primeira mensagem, em outra requisição) e confere a validade.
    /// </summary>
    public async Task<IReadOnlyList<MensagemChatSiteResult>> ListarParaStreamAsync(
        Guid empresaId, string tokenHash, DateTime? depoisDe, CancellationToken ct = default)
    {
        var sessao = await sessaoRepository.ObterPorTokenHashAsync(empresaId, tokenHash, ct);
        if (sessao is null || !sessao.EstaValida(DateTime.UtcNow))
            throw new SessaoChatSiteInvalidaException();
        return await ListarAsync(sessao, depoisDe, ct);
    }

    private async Task<IReadOnlyList<MensagemChatSiteResult>> ListarAsync(
        SessaoChatSite sessao, DateTime? depoisDe, CancellationToken ct)
    {
        if (sessao.ConversaId is not { } conversaId)
            return [];

        var cursor = depoisDe is { } d ? DateTime.SpecifyKind(d.ToUniversalTime(), DateTimeKind.Utc) : (DateTime?)null;
        var mensagens = await conversaRepository.ListarMensagensDepoisAsync(sessao.EmpresaId, conversaId, cursor, Limite, ct);
        return mensagens
            .Where(m => m.Direcao == DirecaoMensagem.Entrada || m.ExternoId is not null)
            .Select(MensagemChatSiteResult.De)
            .ToList();
    }
}
