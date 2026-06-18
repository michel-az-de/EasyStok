namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Seção do cardápio de um <see cref="Storefront"/> (ADR-0035). Estrutura auto-referenciada
/// que serve tanto um menu de restaurante (seções de 1 nível: "Entradas", "Massas", "Sobremesas")
/// quanto uma loja de departamentos (departamento → categoria → subcategoria, até 3 níveis).
///
/// <para>
/// Desacoplada da <see cref="EasyStock.Domain.Entities.Categoria"/> do ERP de propósito: tenants
/// sem ERP (ex.: Casa da Baba) organizam o cardápio sem precisar de categorias de inventário.
/// </para>
///
/// <para>
/// <strong>Reparent fora da v1</strong>: a seção só é criada/renomeada/reordenada/excluída.
/// Por isso <see cref="Nivel"/> é estável na criação (não há drift). Reparent futuro exigirá
/// recompute de <see cref="Nivel"/> da subárvore + revalidação de profundidade.
/// </para>
/// </summary>
public class CardapioSecao
{
    /// <summary>Nível máximo (0-based): 0, 1, 2 = profundidade de 3 níveis.</summary>
    public const short NivelMaximo = 2;

    public Guid Id { get; private set; }
    public Guid StorefrontId { get; private set; }

    /// <summary>Seção pai. Null = seção raiz (topo: departamento ou seção de restaurante).</summary>
    public Guid? SecaoPaiId { get; private set; }

    public string Nome { get; private set; } = null!;

    /// <summary>Ordem de exibição entre irmãs (double — permite inserir entre sem renumerar).</summary>
    public double OrdemExibicao { get; private set; }

    /// <summary>Esconde a seção inteira sem apagá-la. Default true.</summary>
    public bool Visivel { get; private set; }

    /// <summary>Profundidade 0..<see cref="NivelMaximo"/> (CHECK no banco). Estável na criação (sem reparent na v1).</summary>
    public short Nivel { get; private set; }

    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    public CardapioSecao? SecaoPai { get; set; }
    public ICollection<CardapioSecao> SubSecoes { get; set; } = new List<CardapioSecao>();

    // EF Core ctor sem parâmetros
    private CardapioSecao() { }

    /// <summary>Cria uma seção raiz (nível 0) do storefront.</summary>
    public static CardapioSecao CriarRaiz(Guid storefrontId, string nome, double ordemExibicao = 0)
        => Criar(storefrontId, nome, secaoPaiId: null, nivel: 0, ordemExibicao);

    /// <summary>
    /// Cria uma subseção sob <paramref name="pai"/>. Rejeita se o pai já está no nível máximo
    /// (profundidade de 3 níveis).
    /// </summary>
    public static CardapioSecao CriarSubsecao(CardapioSecao pai, string nome, double ordemExibicao = 0)
    {
        if (pai is null)
            throw new RegraDeDominioVioladaException("Seção pai é obrigatória.");
        if (pai.Id == Guid.Empty)
            throw new RegraDeDominioVioladaException("Seção pai deve ter Id válido.");
        if (pai.Nivel >= NivelMaximo)
            throw new RegraDeDominioVioladaException(
                $"Profundidade máxima de seções é {NivelMaximo + 1} níveis.");

        return Criar(pai.StorefrontId, nome, pai.Id, (short)(pai.Nivel + 1), ordemExibicao);
    }

    private static CardapioSecao Criar(Guid storefrontId, string nome, Guid? secaoPaiId, short nivel, double ordemExibicao)
    {
        if (storefrontId == Guid.Empty)
            throw new RegraDeDominioVioladaException("StorefrontId é obrigatório.");
        if (ordemExibicao < 0)
            throw new RegraDeDominioVioladaException(
                $"Ordem de exibição não pode ser negativa (recebido: {ordemExibicao}).");

        var agora = DateTime.UtcNow;
        return new CardapioSecao
        {
            Id = Guid.NewGuid(),
            StorefrontId = storefrontId,
            SecaoPaiId = secaoPaiId,
            Nome = NormalizarNome(nome),
            Nivel = nivel,
            OrdemExibicao = ordemExibicao,
            Visivel = true,
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    public void Renomear(string nome)
    {
        var limpo = NormalizarNome(nome);
        if (string.Equals(limpo, Nome, StringComparison.Ordinal)) return;
        Nome = limpo;
        AlteradoEm = DateTime.UtcNow;
    }

    public void Reordenar(double ordem)
    {
        if (ordem < 0)
            throw new RegraDeDominioVioladaException(
                $"Ordem de exibição não pode ser negativa (recebido: {ordem}).");
        if (Math.Abs(OrdemExibicao - ordem) < double.Epsilon) return;
        OrdemExibicao = ordem;
        AlteradoEm = DateTime.UtcNow;
    }

    public void AlterarVisibilidade(bool visivel)
    {
        if (Visivel == visivel) return;
        Visivel = visivel;
        AlteradoEm = DateTime.UtcNow;
    }

    private static string NormalizarNome(string nome)
    {
        var limpo = (nome ?? string.Empty).Trim();
        if (limpo.Length == 0)
            throw new RegraDeDominioVioladaException("Nome da seção é obrigatório.");
        if (limpo.Length > 100)
            throw new RegraDeDominioVioladaException(
                $"Nome da seção não pode exceder 100 caracteres (recebido: {limpo.Length}).");
        return limpo;
    }
}
