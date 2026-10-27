namespace EasyStock.Domain.Services;

/// <summary>Parte do assistente de primeiro acesso (#2070) em que o passo aparece.</summary>
public enum ParteDaConfiguracaoInicial
{
    /// <summary>Parte 1: o mínimo para a loja receber pedidos.</summary>
    Essencial = 1,

    /// <summary>Parte 2: mais ajustes, cada um pulável.</summary>
    Ajustes = 2,

    /// <summary>Revisão final, com o botão de concluir.</summary>
    Revisao = 3,
}

/// <summary>Um passo do catálogo: id kebab-case (o mesmo da rota do console), parte e ordem global (1 = primeiro).</summary>
public sealed record PassoDoCatalogo(string Id, ParteDaConfiguracaoInicial Parte, int Ordem);

/// <summary>
/// Catálogo fechado dos passos da configuração inicial da loja (#2070). Só estes ids existem: o servidor recusa
/// marcar outro, e o console monta o assistente nesta ordem.
/// </summary>
public static class ConfiguracaoInicial
{
    public const string BoasVindas = "boas-vindas";
    public const string Horarios = "horarios";
    public const string SiteEImpressos = "site-e-impressos";
    public const string Cardapio = "cardapio";
    public const string Janelas = "janelas";
    public const string Frete = "frete";
    public const string Equipe = "equipe";
    public const string DadosDaLoja = "dados-da-loja";
    public const string Atendimento = "atendimento";
    public const string Respostas = "respostas";
    public const string Agente = "agente";
    public const string Producao = "producao";
    public const string Estoque = "estoque";
    public const string Entregadores = "entregadores";
    public const string Impressora = "impressora";
    public const string Avisos = "avisos";
    public const string Pronto = "pronto";

    /// <summary>Tamanho da coluna <c>Passo</c>; todo id do catálogo cabe nela.</summary>
    public const int TamanhoMaximoDoId = 40;

    public static IReadOnlyList<PassoDoCatalogo> Passos { get; } = Numerar(
        (BoasVindas, ParteDaConfiguracaoInicial.Essencial),
        (Horarios, ParteDaConfiguracaoInicial.Essencial),
        (SiteEImpressos, ParteDaConfiguracaoInicial.Essencial),
        (Cardapio, ParteDaConfiguracaoInicial.Essencial),
        (Janelas, ParteDaConfiguracaoInicial.Essencial),
        (Frete, ParteDaConfiguracaoInicial.Essencial),
        (Equipe, ParteDaConfiguracaoInicial.Essencial),
        (DadosDaLoja, ParteDaConfiguracaoInicial.Ajustes),
        (Atendimento, ParteDaConfiguracaoInicial.Ajustes),
        (Respostas, ParteDaConfiguracaoInicial.Ajustes),
        (Agente, ParteDaConfiguracaoInicial.Ajustes),
        (Producao, ParteDaConfiguracaoInicial.Ajustes),
        (Estoque, ParteDaConfiguracaoInicial.Ajustes),
        (Entregadores, ParteDaConfiguracaoInicial.Ajustes),
        (Impressora, ParteDaConfiguracaoInicial.Ajustes),
        (Avisos, ParteDaConfiguracaoInicial.Ajustes),
        (Pronto, ParteDaConfiguracaoInicial.Revisao));

    private static readonly Dictionary<string, PassoDoCatalogo> PorId =
        Passos.ToDictionary(p => p.Id, StringComparer.Ordinal);

    /// <summary>O passo do catálogo com este id, ou nulo quando o id não existe.</summary>
    public static PassoDoCatalogo? Encontrar(string? id) =>
        id is not null && PorId.TryGetValue(id, out var passo) ? passo : null;

    private static PassoDoCatalogo[] Numerar(params (string Id, ParteDaConfiguracaoInicial Parte)[] passos) =>
        passos.Select((p, i) => new PassoDoCatalogo(p.Id, p.Parte, i + 1)).ToArray();
}
