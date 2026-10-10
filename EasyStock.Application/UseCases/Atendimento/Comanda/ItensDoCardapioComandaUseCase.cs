using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.AdicionarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.EditarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleVisibilidadeCardapioItemAdmin;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

/// <summary>
/// Uma porção do prato (M1.4a, #1529): rótulo, peso, preço absoluto (ADR-0035), disponível e padrão.
/// Id null = porção nova. O vínculo com o estoque é do EasyStok, não do console.
/// </summary>
public sealed record PorcaoDoItem(
    Guid? Id, string Rotulo, decimal Preco, string? Peso = null, bool Disponivel = true, bool Padrao = false,
    string? Sku = null, Guid? ProdutoVariacaoId = null);

/// <summary>Campos do item que o console edita. null = não mexe.</summary>
/// <param name="Porcoes">M1.4a (#1529): null não mexe; lista (mesmo vazia) é a lista inteira de porções.</param>
/// <param name="MexerNovidade">true: <paramref name="NovidadeAte"/> vale, inclusive null (tira a novidade).</param>
/// <param name="MexerSecao">true: <paramref name="SecaoId"/> vale, inclusive null (sem categoria). M1.3.</param>
public sealed record DadosItemCardapio(
    string? Nome, LinhaProduto? Linha, string? Porcao, decimal? Preco, string? Categoria,
    string? Descricao = null, string? Ingredientes = null, string? Alergenos = null,
    int? TempoPreparoMinutos = null, string? InstrucaoFinalizacao = null,
    bool MexerNovidade = false, DateOnly? NovidadeAte = null,
    bool MexerSecao = false, Guid? SecaoId = null,
    IReadOnlyList<PorcaoDoItem>? Porcoes = null);

public sealed record ItemForaDoCardapio(Guid CardapioItemId, string Nome, decimal Preco, string? Porcao, string? Categoria, string? FotoUrl);

public sealed record VisibilidadeItemResult(Guid CardapioItemId, bool Visivel);

public sealed record ArquivamentoItemResult(Guid CardapioItemId, bool Arquivado);

/// <summary>O item inteiro para o formulário de edição (a comanda lê o menu público, sem a ficha).</summary>
public sealed record DetalheItemCardapio(
    Guid CardapioItemId, string Nome, LinhaProduto Linha, string? Porcao, decimal Preco, string? Categoria,
    string? Descricao, string? Ingredientes, string? Alergenos, int? TempoPreparoMinutos, string? InstrucaoFinalizacao,
    DateOnly? NovidadeAte, bool EmValidacao, bool Arquivado, Guid? SecaoId = null,
    IReadOnlyList<PorcaoDoItem>? Porcoes = null);

/// <summary>Linha da tela de gestão do cardápio (M1.1, M1.2): tudo o que a lista mostra e liga.</summary>
/// <param name="ControlaSaldo">Item ligado a um produto do estoque (avulso não tem saldo).</param>
/// <param name="Porcoes">M1.4a: quantas porções o prato tem; com porções, o preço é o "a partir de".</param>
public sealed record ItemGestaoCardapio(
    Guid CardapioItemId, string Nome, LinhaProduto Linha, string? Porcao, decimal Preco, string? Categoria,
    string? FotoUrl, bool Visivel, bool Disponivel, double Ordem, bool ControlaSaldo,
    bool Arquivado = false, bool EmValidacao = false, DateOnly? NovidadeAte = null, Guid? SecaoId = null, int Porcoes = 0);

public enum DirecaoMover { Subir, Descer }

public sealed record OrdemItemResult(Guid CardapioItemId, double Ordem);

/// <summary>
/// Itens do cardápio pelo console (#1241, M1.1, M1.2). Decisões do Felipe (08/10/2026): incluir,
/// editar e tirar item são do Gerente; tirar <b>arquiva</b> e é diferente de ocultar do site
/// (D-M1-07); item novo nasce em validação e o agente não oferece até ela confirmar (D-M1-08).
/// Reusa os use cases da vitrine (mesmas validações e guardas de HTML) na vitrine da empresa logada.
/// </summary>
public sealed class ItensDoCardapioComandaUseCase(
    IStorefrontRepository storefrontRepository,
    AdicionarCardapioItemAdminUseCase adicionar,
    EditarCardapioItemAdminUseCase editar,
    ToggleVisibilidadeCardapioItemAdminUseCase toggleVisivel,
    ICardapioItemRepository cardapioRepository,
    ICardapioSecaoRepository secaoRepository,
    IUnitOfWork unitOfWork,
    VincularPorcoesAoEstoqueUseCase vincularPorcoes)
{
    public async Task<AdicionarCardapioItemAdminResult> IncluirAsync(Guid empresaId, DadosItemCardapio dados, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dados.Nome))
            throw new UseCaseValidationException("Informe o nome do item.");
        if (dados.Preco is not > 0)
            throw new UseCaseValidationException("Informe o preço do item.");
        var opcoes = OpcoesDe(dados.Porcoes);
        var storefrontId = await VitrineAsync(empresaId, ct);

        // Item avulso (sem produto do estoque), visível no balcão e no site. Ordem alta: fim da lista.
        var r = await adicionar.ExecuteAsync(new AdicionarCardapioItemAdminCommand(
            storefrontId, null, dados.Nome.Trim(), dados.Categoria, 10_000, true,
            dados.Descricao, dados.Ingredientes, dados.Alergenos, null, null, null, dados.Preco, null, dados.Porcao, null,
            empresaId, Linha: dados.Linha, TempoPreparoMinutos: dados.TempoPreparoMinutos,
            InstrucaoFinalizacao: dados.InstrucaoFinalizacao, Opcoes: opcoes));

        // RN-15: nasce em validação (o agente não oferece) e, se ela pediu, já como novidade e na categoria.
        var item = await ItemAsync(storefrontId, r.ItemId, empresaId, ct);
        item.MarcarEmValidacao();
        if (dados.MexerNovidade) item.DefinirNovidade(dados.NovidadeAte);
        if (dados.MexerSecao) item.DefinirSecao(await SecaoValidaAsync(storefrontId, dados.SecaoId, ct));
        await unitOfWork.CommitAsync();
        return r;
    }

    public async Task EditarAsync(Guid empresaId, Guid itemId, DadosItemCardapio dados, CancellationToken ct = default)
    {
        var opcoes = OpcoesDe(dados.Porcoes);
        var storefrontId = await VitrineAsync(empresaId, ct);
        await editar.ExecuteAsync(new EditarCardapioItemAdminCommand(
            storefrontId, itemId, dados.Nome?.Trim(), dados.Categoria,
            dados.Descricao, dados.Ingredientes, dados.Alergenos, null, null, null, dados.Preco, null, dados.Porcao, null,
            empresaId, Linha: dados.Linha, TempoPreparoMinutos: dados.TempoPreparoMinutos,
            InstrucaoFinalizacao: dados.InstrucaoFinalizacao, Opcoes: opcoes));

        if (!dados.MexerNovidade && !dados.MexerSecao && opcoes is null) return;
        var item = await ItemAsync(storefrontId, itemId, empresaId, ct);
        if (dados.MexerNovidade) item.DefinirNovidade(dados.NovidadeAte);
        if (dados.MexerSecao) item.DefinirSecao(await SecaoValidaAsync(storefrontId, dados.SecaoId, ct));
        if (opcoes is not null) await vincularPorcoes.ExecuteAsync(empresaId, item);
        await unitOfWork.CommitAsync();
    }

    // M1.4a (#1529): porção do console → opção da vitrine (reconciliação keyed-by-Id, ADR-0035).
    private static List<CardapioItemVariacaoInput>? OpcoesDe(IReadOnlyList<PorcaoDoItem>? porcoes)
    {
        if (porcoes is null) return null;
        if (porcoes.Any(p => string.IsNullOrWhiteSpace(p.Rotulo)))
            throw new UseCaseValidationException("Dê um nome a cada porção (ex.: 300 g).");
        if (porcoes.Any(p => p.Preco <= 0))
            throw new UseCaseValidationException("Cada porção precisa de preço.");
        if (porcoes.GroupBy(p => p.Rotulo.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new UseCaseValidationException("Duas porções com o mesmo nome.");
        if (porcoes.Count(p => p.Padrao) > 1)
            throw new UseCaseValidationException("Só uma porção pode ser a padrão.");
        return porcoes.Select((p, i) => new CardapioItemVariacaoInput(
            p.Id, p.Rotulo.Trim(), p.Preco, p.Disponivel, p.Padrao, p.Peso?.Trim(), p.Sku, i)).ToList();
    }

    private static List<PorcaoDoItem> PorcoesDe(CardapioItem item) => item.Variacoes
        .OrderBy(v => v.OrdemExibicao).ThenBy(v => v.CriadoEm)
        .Select(v => new PorcaoDoItem(v.Id, v.Rotulo, v.PrecoStorefront, v.PesoExibicao, v.Disponivel, v.EhPadrao,
            v.Sku?.Value, v.ProdutoVariacaoId))
        .ToList();

    public async Task<VisibilidadeItemResult> DefinirVisivelAsync(Guid empresaId, Guid itemId, bool visivel, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var item = await ItemAsync(storefrontId, itemId, empresaId, ct);
        if (item.Visivel == visivel) return new VisibilidadeItemResult(item.Id, item.Visivel);

        var r = await toggleVisivel.ExecuteAsync(new ToggleVisibilidadeCardapioItemAdminCommand(storefrontId, itemId, empresaId));
        return new VisibilidadeItemResult(r.ItemId, r.VisivelAgora);
    }

    public async Task<DetalheItemCardapio> ObterAsync(Guid empresaId, Guid itemId, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var i = await ItemAsync(storefrontId, itemId, empresaId, ct);
        return new DetalheItemCardapio(
            i.Id, i.NomeEfetivo() ?? "(sem nome)", i.Linha, i.PesoExibicao, i.PrecoEfetivo(), i.CategoriaEfetiva(),
            i.DescricaoPublica, i.Ingredientes, i.Alergenos, i.TempoPreparoMinutos, i.InstrucaoFinalizacao,
            i.NovidadeAte, i.EmValidacao, i.EstaArquivado, i.SecaoId, PorcoesDe(i));
    }

    /// <summary>Tirar (arquivar) ou repor, por valor: o clique repetido não inverte de volta.</summary>
    public async Task<ArquivamentoItemResult> DefinirArquivadoAsync(Guid empresaId, Guid itemId, bool arquivado, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var item = await ItemAsync(storefrontId, itemId, empresaId, ct);
        if (item.EstaArquivado == arquivado) return new ArquivamentoItemResult(item.Id, arquivado);

        if (arquivado) item.Arquivar(DateTime.UtcNow);
        else item.Repor();
        await unitOfWork.CommitAsync();
        return new ArquivamentoItemResult(item.Id, item.EstaArquivado);
    }

    /// <summary>RN-15: a dona confirma que o item novo vale ficar; o agente passa a oferecer.</summary>
    public async Task ConfirmarValidacaoAsync(Guid empresaId, Guid itemId, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var item = await ItemAsync(storefrontId, itemId, empresaId, ct);
        if (!item.EmValidacao) return;
        item.ConfirmarValidacao();
        await unitOfWork.CommitAsync();
    }

    /// <summary>Itens tirados do cardápio (arquivados), para o console mostrar "Repor".</summary>
    public async Task<IReadOnlyList<ItemForaDoCardapio>> ListarForaAsync(Guid empresaId, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        return NaOrdem(await cardapioRepository.GetTodosDoStorefrontAsync(storefrontId, ct))
            .Where(i => i.EstaArquivado)
            .Select(i => new ItemForaDoCardapio(i.Id, i.NomeEfetivo() ?? "(sem nome)", i.PrecoEfetivo(), i.PesoExibicao,
                i.CategoriaEfetiva(), i.FotoUrl))
            .ToList();
    }

    /// <summary>
    /// Gestão do cardápio (M1.1): todos os itens da vitrine, inclusive os ocultos do site, os
    /// desligados do dia (RN-16) e os arquivados (para repor), na ordem de exibição.
    /// </summary>
    public async Task<IReadOnlyList<ItemGestaoCardapio>> ListarGestaoAsync(Guid empresaId, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var itens = await cardapioRepository.GetTodosDoStorefrontAsync(storefrontId, ct);
        return NaOrdem(itens)
            .Select(i => new ItemGestaoCardapio(
                i.Id, i.NomeEfetivo() ?? "(sem nome)", i.Linha, i.PesoExibicao,
                i.TemVariacoes() ? i.Variacoes.Min(v => v.PrecoStorefront) : i.PrecoEfetivo(), i.CategoriaEfetiva(),
                i.FotoUrl, i.Visivel, i.Disponivel, i.OrdemExibicao, i.ProdutoId.HasValue,
                i.EstaArquivado, i.EmValidacao, i.NovidadeAte, i.SecaoId, i.Variacoes.Count))
            .ToList();
    }

    /// <summary>
    /// Sobe ou desce o item uma posição (#1486). Os itens nascem com ordem 0 e a ordem não aceita
    /// valor negativo, então o meio dos vizinhos não serve num cardápio nunca reordenado (todos
    /// empatados). Aqui o servidor troca o item de lugar na lista de gestão (mesmo desempate) e
    /// renumera de 1 a n, gravando só quem mudou, num commit. Na ponta, não muda nada.
    /// </summary>
    public async Task<OrdemItemResult> MoverAsync(Guid empresaId, Guid itemId, DirecaoMover direcao, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var lista = NaOrdem(await cardapioRepository.GetTodosDoStorefrontAsync(storefrontId, ct)).ToList();
        var indice = lista.FindIndex(i => i.Id == itemId);
        if (indice < 0) throw new CardapioItemNaoEncontradoException(storefrontId, itemId);

        var alvo = indice + (direcao == DirecaoMover.Subir ? -1 : 1);
        if (alvo < 0 || alvo >= lista.Count) return new OrdemItemResult(itemId, lista[indice].OrdemExibicao);
        (lista[indice], lista[alvo]) = (lista[alvo], lista[indice]);

        double ordemDoItem = 0;
        for (var posicao = 0; posicao < lista.Count; posicao++)
        {
            var nova = posicao + 1;
            if (lista[posicao].Id == itemId) ordemDoItem = nova;
            if (Math.Abs(lista[posicao].OrdemExibicao - nova) < double.Epsilon) continue;
            var rastreado = await cardapioRepository.GetByIdAndScopeAsync(storefrontId, lista[posicao].Id, empresaId, ct);
            rastreado?.DefinirOrdem(nova);
        }
        await unitOfWork.CommitAsync();
        return new OrdemItemResult(itemId, ordemDoItem);
    }

    // Mesmo desempate do menu público (ordem, criado em, id): a lista que ela vê é a que muda.
    private static IEnumerable<CardapioItem> NaOrdem(IEnumerable<CardapioItem> itens) =>
        itens.OrderBy(i => i.OrdemExibicao).ThenBy(i => i.CriadoEm).ThenBy(i => i.Id);

    // A categoria do prato tem de ser da mesma vitrine (nunca de outra empresa). null = sem categoria.
    private async Task<Guid?> SecaoValidaAsync(Guid storefrontId, Guid? secaoId, CancellationToken ct)
    {
        if (secaoId is not { } id) return null;
        _ = await secaoRepository.GetByIdAsync(storefrontId, id, ct)
            ?? throw new UseCaseValidationException("Categoria não encontrada.");
        return id;
    }

    private async Task<CardapioItem> ItemAsync(Guid storefrontId, Guid itemId, Guid empresaId, CancellationToken ct) =>
        await cardapioRepository.GetByIdAndScopeAsync(storefrontId, itemId, empresaId, ct)
        ?? throw new CardapioItemNaoEncontradoException(storefrontId, itemId);

    private async Task<Guid> VitrineAsync(Guid empresaId, CancellationToken ct)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException();
        return storefront.Id;
    }
}
