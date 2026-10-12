using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

public sealed record SecaoDoCardapio(Guid SecaoId, string Nome, double Ordem, bool Visivel, int Itens);

public sealed record MigracaoDeCategoriasResult(int SecoesCriadas, int ItensLigados);

/// <summary>
/// Categorias do cardápio pelo console (M1.3, #1483). D-M1-01 (Felipe, 08/10/2026): a categoria que o
/// cliente vê é a seção do cardápio. v1 de restaurante: seções de um nível (raiz), com nome único
/// na vitrine, ordem renumerada no servidor (mesmo motivo do #1486), ocultar sem apagar e excluir
/// só a vazia. <see cref="MigrarCategoriasAsync"/> leva o texto livre antigo para seções sem apagá-lo.
/// </summary>
public sealed class SecoesDoCardapioComandaUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioSecaoRepository secaoRepository,
    ICardapioItemRepository cardapioRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<SecaoDoCardapio>> ListarAsync(Guid empresaId, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var secoes = await secaoRepository.GetDoStorefrontAsync(storefrontId, ct);
        var contagem = await secaoRepository.ContarItensPorSecaoAsync(storefrontId, ct);
        return Raizes(secoes)
            .Select(s => new SecaoDoCardapio(s.Id, s.Nome, s.OrdemExibicao, s.Visivel, contagem.GetValueOrDefault(s.Id)))
            .ToList();
    }

    public async Task<SecaoDoCardapio> CriarAsync(Guid empresaId, string nome, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var secoes = await secaoRepository.GetDoStorefrontAsync(storefrontId, ct);
        GarantirNomeLivre(secoes, nome, ignorar: null);
        var ordem = secoes.Count == 0 ? 1 : secoes.Max(s => s.OrdemExibicao) + 1;
        var secao = CardapioSecao.CriarRaiz(storefrontId, nome, ordem);
        await secaoRepository.AddAsync(secao, ct);
        await unitOfWork.CommitAsync();
        return new SecaoDoCardapio(secao.Id, secao.Nome, secao.OrdemExibicao, secao.Visivel, 0);
    }

    public async Task RenomearAsync(Guid empresaId, Guid secaoId, string nome, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var secoes = await secaoRepository.GetDoStorefrontAsync(storefrontId, ct);
        var secao = SecaoDe(secoes, secaoId);
        GarantirNomeLivre(secoes, nome, ignorar: secao.Id);
        secao.Renomear(nome);
        await unitOfWork.CommitAsync();
    }

    /// <summary>Esconde a categoria inteira do site e da comanda, sem apagar nem soltar os pratos.</summary>
    public async Task DefinirVisivelAsync(Guid empresaId, Guid secaoId, bool visivel, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var secao = await secaoRepository.GetByIdAsync(storefrontId, secaoId, ct)
            ?? throw new UseCaseValidationException("Categoria não encontrada.");
        if (secao.Visivel == visivel) return;
        secao.AlterarVisibilidade(visivel);
        await unitOfWork.CommitAsync();
    }

    /// <summary>Sobe ou desce entre as irmãs e renumera de 1 a n (seção também nasce em ordem 0).</summary>
    public async Task MoverAsync(Guid empresaId, Guid secaoId, DirecaoMover direcao, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var lista = Raizes(await secaoRepository.GetDoStorefrontAsync(storefrontId, ct)).ToList();
        var indice = lista.FindIndex(s => s.Id == secaoId);
        if (indice < 0) throw new UseCaseValidationException("Categoria não encontrada.");
        var alvo = indice + (direcao == DirecaoMover.Subir ? -1 : 1);
        if (alvo < 0 || alvo >= lista.Count) return;
        (lista[indice], lista[alvo]) = (lista[alvo], lista[indice]);
        for (var posicao = 0; posicao < lista.Count; posicao++) lista[posicao].Reordenar(posicao + 1);
        await unitOfWork.CommitAsync();
    }

    /// <summary>Só a categoria vazia sai: com prato dentro, ela precisa mover os pratos antes.</summary>
    public async Task ExcluirAsync(Guid empresaId, Guid secaoId, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var secoes = await secaoRepository.GetDoStorefrontAsync(storefrontId, ct);
        var secao = SecaoDe(secoes, secaoId);
        var itens = (await secaoRepository.ContarItensPorSecaoAsync(storefrontId, ct)).GetValueOrDefault(secao.Id);
        if (itens > 0)
            throw new UseCaseValidationException(
                $"A categoria \"{secao.Nome}\" tem {itens} prato(s). Mude os pratos de categoria antes de excluir.");
        if (secoes.Any(s => s.SecaoPaiId == secao.Id))
            throw new UseCaseValidationException($"A categoria \"{secao.Nome}\" tem subcategorias.");
        await secaoRepository.RemoveAsync(secao, ct);
        await unitOfWork.CommitAsync();
    }

    /// <summary>
    /// Leva o texto livre de categoria (<c>CategoriaTexto</c>) para seções: uma seção raiz por texto
    /// distinto (sem diferenciar maiúsculas), na ordem alfabética de hoje, e liga os pratos sem seção.
    /// Idempotente: rodar de novo não cria nem liga nada a mais. Não apaga o texto antigo.
    /// </summary>
    public async Task<MigracaoDeCategoriasResult> MigrarCategoriasAsync(Guid empresaId, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var secoes = (await secaoRepository.GetDoStorefrontAsync(storefrontId, ct)).ToList();
        var itens = await cardapioRepository.GetTodosDoStorefrontAsync(storefrontId, ct);
        var semSecao = itens.Where(i => i.SecaoId is null && !string.IsNullOrWhiteSpace(i.CategoriaTexto)).ToList();

        var criadas = 0;
        var proximaOrdem = secoes.Count == 0 ? 1 : secoes.Max(s => s.OrdemExibicao) + 1;
        var porNome = Raizes(secoes).ToDictionary(s => Chave(s.Nome), s => s.Id);
        foreach (var texto in semSecao.Select(i => i.CategoriaTexto!.Trim())
                     .DistinctBy(Chave).OrderBy(t => t, StringComparer.Ordinal))
        {
            if (porNome.ContainsKey(Chave(texto))) continue;
            var secao = CardapioSecao.CriarRaiz(storefrontId, Exibicao(texto), proximaOrdem++);
            await secaoRepository.AddAsync(secao, ct);
            porNome[Chave(texto)] = secao.Id;
            criadas++;
        }

        var ligados = 0;
        foreach (var item in semSecao)
        {
            var rastreado = await cardapioRepository.GetByIdAndScopeAsync(storefrontId, item.Id, empresaId, ct);
            if (rastreado is null || rastreado.SecaoId is not null) continue;
            rastreado.DefinirSecao(porNome[Chave(item.CategoriaTexto!)]);
            ligados++;
        }

        if (criadas > 0 || ligados > 0) await unitOfWork.CommitAsync();
        return new MigracaoDeCategoriasResult(criadas, ligados);
    }

    private static IEnumerable<CardapioSecao> Raizes(IEnumerable<CardapioSecao> secoes) =>
        secoes.Where(s => s.SecaoPaiId is null)
            .OrderBy(s => s.OrdemExibicao).ThenBy(s => s.CriadoEm).ThenBy(s => s.Id);

    private static string Chave(string nome) => nome.Trim().ToLowerInvariant();

    // O texto avulso é gravado em minúsculo (factory do item): a seção nasce com inicial maiúscula.
    private static string Exibicao(string texto) => texto.Length == 0 ? texto : char.ToUpperInvariant(texto[0]) + texto[1..];

    private static void GarantirNomeLivre(IEnumerable<CardapioSecao> secoes, string nome, Guid? ignorar)
    {
        if (string.IsNullOrWhiteSpace(nome)) throw new UseCaseValidationException("Informe o nome da categoria.");
        if (secoes.Any(s => s.Id != ignorar && s.SecaoPaiId is null && Chave(s.Nome) == Chave(nome)))
            throw new UseCaseValidationException($"Já existe a categoria \"{nome.Trim()}\".");
    }

    private static CardapioSecao SecaoDe(IEnumerable<CardapioSecao> secoes, Guid secaoId) =>
        secoes.FirstOrDefault(s => s.Id == secaoId) ?? throw new UseCaseValidationException("Categoria não encontrada.");

    private async Task<Guid> VitrineAsync(Guid empresaId, CancellationToken ct)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException();
        return storefront.Id;
    }
}
