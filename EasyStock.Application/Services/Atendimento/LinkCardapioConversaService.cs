using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

public sealed record LinkCardapioConversaGerado(string Token, string Url, DateTime ExpiraEm);

/// <summary>Link do cardápio inexistente, vencido, já usado ou de conversa encerrada: 410.</summary>
public sealed class LinkCardapioConversaIndisponivelException() : Exception(Orientacao)
{
    public const string Orientacao = "Este link do cardápio venceu ou já foi usado. Peça um link novo na conversa.";
}

/// <summary>
/// Gera e valida o link do cardápio ligado à conversa (S48). O token é aleatório (256 bits,
/// base64url) e vai na URL como <c>?c=</c>; o banco guarda só o hash. Vale 24 h e um pedido só.
/// </summary>
public sealed class LinkCardapioConversaService(ILinkCardapioConversaRepository repository, SaudacaoAtendimento saudacao)
{
    public const string ParametroToken = "c";

    /// <summary>Cria o link da conversa. Quem chama faz o commit (o turno do agente, na ferramenta).</summary>
    public async Task<LinkCardapioConversaGerado> GerarAsync(
        Guid empresaId, Guid conversaId, DateTime agora, CancellationToken ct = default)
    {
        var token = AcessoChatSite.NovoToken();
        var link = LinkCardapioConversa.Gerar(empresaId, conversaId, AcessoChatSite.HashDoToken(token), agora);
        await repository.AddAsync(link, ct);

        var cardapio = await saudacao.ResolverLinkCardapioAsync(empresaId, ct);
        var separador = cardapio.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return new LinkCardapioConversaGerado(token, $"{cardapio}{separador}{ParametroToken}={token}", link.ExpiraEm);
    }

    /// <summary>Link válido pelo token. Inexistente, vencido ou já usado: <see cref="LinkCardapioConversaIndisponivelException"/>.</summary>
    public async Task<LinkCardapioConversa> ValidarAsync(string? token, DateTime agora, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new LinkCardapioConversaIndisponivelException();

        var link = await repository.ObterPorTokenHashAsync(AcessoChatSite.HashDoToken(token.Trim()), ct);
        if (link is null || !link.EstaValido(agora))
            throw new LinkCardapioConversaIndisponivelException();
        return link;
    }

    /// <summary>Marca o uso de forma atômica; quem perdeu a corrida recebe 410 como quem chegou depois.</summary>
    public async Task ConsumirAsync(LinkCardapioConversa link, DateTime agora, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!await repository.TentarConsumirAsync(link.EmpresaId, link.Id, agora, ct))
            throw new LinkCardapioConversaIndisponivelException();
    }

    public Task LiberarAsync(LinkCardapioConversa link, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        return repository.LiberarAsync(link.EmpresaId, link.Id, ct);
    }
}
