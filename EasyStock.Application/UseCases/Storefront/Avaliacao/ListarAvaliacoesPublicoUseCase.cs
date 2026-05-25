using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Storefront.Avaliacao;

/// <summary>
/// Lista avaliações públicas de um storefront — anonimizadas, sem PII.
/// </summary>
public sealed class ListarAvaliacoesPublicoUseCase(
    IStorefrontRepository storefrontRepo,
    IPedidoAvaliacaoRepository avaliacaoRepo)
{
    private const int LimitePadrao = 20;
    private const int LimiteMaximo = 100;

    public async Task<ListarAvaliacoesResult> ExecuteAsync(
        ListarAvaliacoesInput input,
        CancellationToken ct = default)
    {
        var storefront = await storefrontRepo.GetBySlugAsync(input.Slug, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException(input.Slug);

        var limit = Math.Clamp(input.Limit ?? LimitePadrao, 1, LimiteMaximo);

        var avaliacoes = await avaliacaoRepo.GetVisiveisDaEmpresaAsync(
            storefront.EmpresaId, limit, ct);

        var items = avaliacoes.Select(a => new AvaliacaoPublicaDto(
            Nota: a.Estrelas,
            Comentario: a.Comentario,
            PrimeiroNomeCliente: ExtrairPrimeiroNome(a.Id),
            CriadaEm: a.RespondidoEm)).ToList();

        return new ListarAvaliacoesResult(items, avaliacoes.Count);
    }

    private static string ExtrairPrimeiroNome(Guid avaliacaoId)
    {
        // ClienteNome não está em PedidoAvaliacao — apenas Id/EmpresaId/etc.
        // O nome do cliente fica no Pedido. Para não expor PII via join,
        // retornamos placeholder por ora. Iteração futura pode adicionar
        // PrimeiroNome diretamente na entity de avaliação.
        return "Cliente";
    }
}

public sealed record ListarAvaliacoesInput(string Slug, int? Page, int? Limit);

public sealed record AvaliacaoPublicaDto(
    int Nota,
    string? Comentario,
    string PrimeiroNomeCliente,
    DateTime? CriadaEm);

public sealed record ListarAvaliacoesResult(
    IReadOnlyList<AvaliacaoPublicaDto> Items,
    int Total);
