using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Storefront.Agendamento;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

/// <param name="Itens">cardapio_item_id dos itens do pedido; sem itens vale o preparo padrão.</param>
public sealed record ListarJanelasAtendimentoInput(
    Guid EmpresaId,
    DateOnly? DataInicio,
    DateOnly? DataFim,
    IReadOnlyCollection<Guid> Itens,
    int Maximo = ListarJanelasAtendimentoUseCase.MaximoPadrao);

/// <param name="LojaDisponivel">Falso sem vitrine ativa: não há janela a oferecer.</param>
public sealed record JanelasAtendimentoResult(
    bool LojaDisponivel,
    int PrazoMinimoMinutos,
    IReadOnlyList<JanelaDisponivelDto> Janelas);

/// <summary>
/// Janelas com vaga que respeitam o prazo mínimo do pedido (S16, RN-21: maior preparo dos itens mais o
/// respiro da configuração), para a vitrine da empresa. Um caminho só para o agente
/// (<c>listar_janelas</c>) e para a comanda do console (F03).
/// </summary>
public sealed class ListarJanelasAtendimentoUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioItemRepository,
    IConfiguracaoAtendimentoRepository configuracaoRepository,
    ListarJanelasDisponiveisUseCase listarJanelas)
{
    public const int MaximoPadrao = 10;

    public async Task<JanelasAtendimentoResult> ExecuteAsync(ListarJanelasAtendimentoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var storefront = await storefrontRepository.GetByEmpresaAsync(input.EmpresaId, ct);
        if (storefront is null || !storefront.Ativo)
            return new JanelasAtendimentoResult(false, 0, []);

        var prazo = await PrazoMinimoAsync(input.EmpresaId, storefront.Id, input.Itens, ct);
        var janelas = (await listarJanelas.ExecuteAsync(
                new ListarJanelasDisponiveisInput(storefront.Slug, input.DataInicio, input.DataFim, null, prazo), ct))
            .Where(j => !j.Esgotado)
            .Take(input.Maximo)
            .ToList();
        return new JanelasAtendimentoResult(true, prazo, janelas);
    }

    private async Task<int> PrazoMinimoAsync(Guid empresaId, Guid storefrontId, IReadOnlyCollection<Guid> itens, CancellationToken ct)
    {
        var configuracao = await configuracaoRepository.GetOrDefaultAsync(empresaId);

        var tempos = new List<int?>();
        foreach (var id in itens)
        {
            var item = await cardapioItemRepository.GetByIdAsync(storefrontId, id, ct);
            if (item is not null) tempos.Add(item.TempoPreparoMinutos);
        }
        if (tempos.Count == 0) tempos.Add(null); // sem itens: vale o preparo padrão

        return CalculadoraPrazoPedido.PrazoMinimo(tempos, configuracao.TempoPreparoPadraoMinutos, configuracao.RespiroMinutos);
    }
}
