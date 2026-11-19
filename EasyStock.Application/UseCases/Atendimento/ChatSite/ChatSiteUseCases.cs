using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.ChatSite;

public sealed record SessaoChatSiteAberta(Guid SessaoId, string Token, DateTime ExpiraEm);

/// <summary>
/// Mensagem como o visitante vê: a dele ou a da loja. Notas internas não aparecem. <c>Tipo</c>
/// (<c>texto</c>, <c>imagem</c>...) e <c>TemMidia</c> (#1448): com mídia, o widget busca o arquivo em
/// <c>GET mensagens/{id}/midia</c> com o mesmo <c>X-Chat-Token</c>; foto pode vir sem texto.
/// </summary>
public sealed record MensagemChatSiteResult(
    Guid Id, bool DoVisitante, string? Texto, DateTime EnviadaEm, string Tipo = "texto", bool TemMidia = false,
    string Autor = "equipe")
{
    /// <summary>
    /// #2876: <c>Autor</c> é <c>visitante</c>, <c>assistente</c> (resposta do agente) ou <c>equipe</c> (console ou aviso do sistema).
    /// </summary>
    public static MensagemChatSiteResult De(Mensagem m) =>
        new(m.Id, m.Direcao == DirecaoMensagem.Entrada, m.Texto, m.EnviadaEm,
            m.TipoConteudo.ToString().ToLowerInvariant(), m.MidiaChave is not null, AutorDe(m));

    private static string AutorDe(Mensagem m) => m.Direcao == DirecaoMensagem.Entrada ? "visitante"
        : m.Autor == AutorMensagem.Agente ? "assistente" : "equipe";
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
        if (sessao is null || sessao.EmpresaId != loja.EmpresaId || sessao.StorefrontId != loja.Id || !sessao.EstaValida(agora))
            throw new SessaoChatSiteInvalidaException();
        return sessao;
    }

    public static string NovoToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Tamanho do token de <see cref="NovoToken"/>: base64url sem preenchimento de <see cref="TokenBytes"/> bytes.</summary>
    public static readonly int TamanhoDoToken = (TokenBytes * 4 + 2) / 3;

    /// <summary>
    /// #1725-1: o texto tem o formato de <see cref="NovoToken"/>? Não diz se a sessão existe; serve ao limite de taxa, que
    /// só particiona por token no formato gerado (o resto cai no IP).
    /// </summary>
    public static bool TemFormatoDeToken(string? token) =>
        token is not null && token.Length == TamanhoDoToken && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

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
    ILogger<EnviarMensagemVisitanteUseCase> logger,
    EasyStock.Application.Services.Atendimento.ConversaChatSiteService conversaChat,
    IQueueService? fila = null)
{
    public const int TextoTamanhoMaximo = 1000;
    public const string PrefixoConfirmacaoRecebimento = "chatsite:recepcao:";

    public Task<MensagemChatSiteResult> ExecuteAsync(string slug, string? token, string? texto, CancellationToken ct = default) =>
        EnviarAsync(slug, token, texto, null, ct);

    public Task<MensagemChatSiteResult> ExecuteNaConversaAsync(string slug, string? token, string? texto, Guid conversaId, CancellationToken ct = default) =>
        EnviarAsync(slug, token, texto, conversaId, ct);

    private async Task<MensagemChatSiteResult> EnviarAsync(string slug, string? token, string? texto, Guid? conversaEsperada, CancellationToken ct)
    {
        var limpo = texto?.Trim();
        if (string.IsNullOrEmpty(limpo))
            throw new UseCaseValidationException("Escreva a mensagem.");
        if (limpo.Length > TextoTamanhoMaximo)
            throw new UseCaseValidationException($"A mensagem passa de {TextoTamanhoMaximo} caracteres.");

        var agora = DateTime.UtcNow;
        var sessao = await acesso.ResolverSessaoAsync(slug, token, agora, ct);
        sessao.RegistrarUso(agora);

        // A trava de contato do serviço dura até entrada e confirmação estarem persistidas.
        var assistenteAtende = false;
        var mensagem = await unitOfWork.ExecuteInTransactionSemRetryAsync(async tokenCt =>
        {
            Conversa conversa;
            if (conversaEsperada is { } esperada)
            {
                if (sessao.ConversaId != esperada) throw new SessaoChatSiteInvalidaException();
                conversa = await conversaRepository.ObterPorIdComLockAsync(sessao.EmpresaId, esperada, tokenCt)
                    ?? throw new SessaoChatSiteInvalidaException();
                if (conversa.EmpresaId != sessao.EmpresaId || !conversa.EstaAberta || conversa.Canal != CanalConversa.ChatSite)
                    throw new SessaoChatSiteInvalidaException();
                await conversaChat.GarantirSessaoAsync(sessao, agora, tokenCt);
            }
            else
            {
                conversa = await conversaChat.ObterOuCriarAsync(sessao, null, agora, tokenCt);
                // #1797: a trava do contato não exclui o console. A linha travada e relida não grava não lidas velhas por cima
                // do marcar-lida; encerrada enquanto esperava, a mensagem abre outra conversa (nova, ninguém mais a vê).
                conversa = await conversaRepository.ObterPorIdComLockAsync(sessao.EmpresaId, conversa.Id, tokenCt)
                    ?? throw new ConversaNaoEncontradaException(conversa.Id);
                if (!conversa.EstaAberta) conversa = await conversaChat.ObterOuCriarAsync(sessao, null, agora, tokenCt);
            }
            var entrada = Mensagem.Entrada(sessao.EmpresaId, conversa.Id, agora, TipoConteudoMensagem.Texto, limpo);
            conversa.RegistrarEntrada(agora);
            await conversaRepository.AddMensagemAsync(entrada, tokenCt);
            // #2876: com o Assistente atendendo ESTA conversa, ele responde no turno e a confirmação automática não entra.
            // Conversa em conferência, escalada ou assumida pela equipe não tem turno: sem a confirmação o visitante ficaria no silêncio.
            assistenteAtende = await conversaChat.AssistenteAtendeAsync(sessao.EmpresaId)
                && !PortoesDoAssistente.DaConversa(conversa, agora).Any(etapa => etapa.Barra);
            var confirmacaoId = PrefixoConfirmacaoRecebimento + conversa.Id.ToString("N");
            // #1991-4: 1 µs depois da entrada (a precisão do timestamp no banco): com o mesmo instante a ordem do chat variava.
            if (!assistenteAtende && await conversaRepository.ObterMensagemPorExternoIdAsync(sessao.EmpresaId, confirmacaoId, tokenCt) is null)
                await conversaRepository.AddMensagemAsync(Mensagem.Saida(sessao.EmpresaId, conversa.Id,
                    AutorMensagem.Sistema, agora.AddTicks(TimeSpan.TicksPerMicrosecond), TipoConteudoMensagem.Texto,
                    "Recebemos sua mensagem. A equipe da loja responde por aqui.", confirmacaoId), tokenCt);
            await unitOfWork.CommitAsync();
            return entrada;
        }, ct);

        // #2876: o turno roda depois do commit, para o agente já enxergar a mensagem do visitante.
        if (assistenteAtende && fila is not null)
            await fila.EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, new ProcessarTurnoAgenteJob(sessao.EmpresaId, mensagem.ConversaId));

        try
        {
            await eventPublisher.PublicarAsync("conversa.mensagem_recebida", sessao.EmpresaId,
                new { conversaId = mensagem.ConversaId, mensagemId = mensagem.Id }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Chat do site: mensagem {MensagemId} gravada, aviso ao console falhou.", mensagem.Id);
        }

        return MensagemChatSiteResult.De(mensagem);
    }


}

/// <summary>O que o visitante preencheu no formulário antes do chat (#1430).</summary>
public sealed record IdentificacaoVisitanteInput(string? Nome, string? Telefone, string? Email, bool AceitePrivacidade);

/// <summary>Os dados como ficaram gravados (telefone em E.164, e-mail minúsculo).</summary>
public sealed record IdentificacaoVisitanteResult(string Nome, string Telefone, string? Email, DateTime InformadoEm);

/// <summary>
/// #1430: formulário curto antes do chat. Grava nome, telefone, e-mail e o aceite da política na sessão; a
/// conversa já aberta recebe o contato na hora, e a que nascer depois nasce com ele
/// (<see cref="EasyStock.Application.Services.Atendimento.ConversaChatSiteService"/>).
/// #2876: o formulário também cria o Cliente (lead do site, com o aceite do canal e aviso à equipe). Telefone que já é de
/// um cadastro, ou compartilhado, não é provado só por ter sido digitado: a conversa fica em conferência, sem cliente
/// ligado, e passa para a equipe. Sem cliente ligado o Assistente não carrega dossiê, histórico, endereço nem pedidos.
/// </summary>
public sealed class IdentificarVisitanteChatSiteUseCase(
    AcessoChatSite acesso, IConversaRepository conversaRepository, IUnitOfWork unitOfWork,
    IOperacaoEventPublisher operacaoEventos,
    EasyStock.Application.Services.Atendimento.ConversaChatSiteService conversaChat,
    IdentificarClientePorTelefoneUseCase identificarCliente,
    IConsentimentoContatoRepository consentimentos,
    IEscaladorConversa escalador,
    INotificadorService notificador)
{
    public const string MotivoConferencia = "Telefone do formulário do site já tem cadastro: confira a pessoa antes de atender.";
    public const string MotivoLeadNovo = "Novo contato pelo chat do site.";

    public async Task<IdentificacaoVisitanteResult> ExecuteAsync(
        string slug, string? token, IdentificacaoVisitanteInput? input, CancellationToken ct = default)
    {
        // #1643-4: loja e sessão primeiro (404/403); o corpo só é validado para quem tem sessão.
        var agora = DateTime.UtcNow;
        var sessao = await acesso.ResolverSessaoAsync(slug, token, agora, ct);
        if (input is null)
            throw new UseCaseValidationException("Preencha nome e telefone.");

        ContatoInformadoVisitante contato;
        try
        {
            contato = ContatoInformadoVisitante.Criar(input.Nome, input.Telefone, input.Email, input.AceitePrivacidade, agora);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        sessao.RegistrarUso(agora);
        sessao.Identificar(contato);

        var conversa = await conversaChat.ObterOuCriarAsync(sessao, null, agora, ct);
        conversa = await conversaRepository.ObterPorIdAsync(sessao.EmpresaId, conversa.Id, ct) ?? conversa;
        if (conversa is not { EstaAberta: true, Canal: CanalConversa.ChatSite } || conversa.EmpresaId != sessao.EmpresaId)
            throw new SessaoChatSiteInvalidaException();

        conversa.RegistrarContatoInformado(contato);
        string? leadNovo = null;
        if (conversa.ClienteId is null && !conversa.VinculoEmRevisao)
            leadNovo = await IdentificarClienteAsync(sessao.EmpresaId, conversa, contato, agora, ct);

        // #2908 (ADR-0030): o aviso só é estagiado na UoW e grava no mesmo commit do lead. Depois do commit ele se perderia.
        if (leadNovo is not null)
            await new PublicadorAvisoEquipe(notificador).AtencaoNoAtendimentoAsync(sessao.EmpresaId, conversa.Id, leadNovo, MotivoLeadNovo, ct);
        await unitOfWork.CommitAsync();
        // #1643-5: o console troca nome e contato na hora, sem esperar a próxima sincronização.
        await operacaoEventos.PublicarAsync(EventosOperacao.ConversaAtualizada, sessao.EmpresaId,
            new ConversaAtualizadaOperacao(conversa.Id), ct);
        return new IdentificacaoVisitanteResult(contato.Nome, contato.Telefone, contato.Email, contato.InformadoEm);
    }

    /// <summary>Devolve o nome do cliente recém-cadastrado (lead novo a avisar à equipe) ou nulo.</summary>
    private async Task<string?> IdentificarClienteAsync(
        Guid empresaId, Conversa conversa, ContatoInformadoVisitante contato, DateTime agora, CancellationToken ct)
    {
        IdentificacaoCliente identificacao;
        try
        {
            // ReutilizarCadastro:false: cadastro existente nunca é ligado só pelo telefone digitado.
            identificacao = await identificarCliente.ExecuteAsync(
                new IdentificarClientePorTelefoneInput(empresaId, contato.Telefone.TrimStart('+'), contato.Nome, ReutilizarCadastro: false), ct);
        }
        catch (TelefoneExigeVerificacaoException)
        {
            await ExigirConferenciaAsync(empresaId, conversa, agora, ct);
            return null;
        }

        if (identificacao is not { Cliente: { } cliente, EhNovo: true })
        {
            await ExigirConferenciaAsync(empresaId, conversa, agora, ct);
            return null;
        }

        cliente.Email = contato.Email;
        conversa.VincularCliente(cliente.Id);
        conversa.AtualizarContatoNome(cliente.Nome);
        await consentimentos.AddAsync(ConsentimentoContato.Registrar(empresaId, cliente.Id, CanalConversa.ChatSite,
            FinalidadeContato.Transacional, SituacaoConsentimento.Concedido,
            ClienteDaConversa.CadastrarClienteDaConversaUseCase.OrigemAceiteChatSite, contato.InformadoEm), ct);
        return cliente.Nome;
    }

    private async Task ExigirConferenciaAsync(Guid empresaId, Conversa conversa, DateTime agora, CancellationToken ct)
    {
        conversa.ExigirConferenciaCliente();
        await escalador.EscalarAsync(empresaId, conversa, MotivoConferencia, agora, ct);
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
        var sessao = await sessaoRepository.ObterSnapshotPorTokenHashAsync(empresaId, tokenHash, ct);
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
