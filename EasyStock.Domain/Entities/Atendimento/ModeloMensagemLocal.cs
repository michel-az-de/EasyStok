using System.Text.RegularExpressions;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Mensagem padrão local (#3204): modelo de mensagem do WhatsApp guardado no EasyStok, já no formato
/// que a Meta aprova (nome, idioma, categoria e parâmetros <c>{{n}}</c>). Nome + idioma são únicos por empresa.
/// </summary>
public partial class ModeloMensagemLocal
{
    public const int NomeTamanhoMaximo = 512;
    public const int IdiomaTamanhoMaximo = 5;
    public const int CategoriaTamanhoMaximo = 20;
    public const int CabecalhoTamanhoMaximo = 60;
    public const int CorpoTamanhoMaximo = 1024;
    public const int RodapeTamanhoMaximo = 60;

    /// <summary>Única categoria aceita nesta rodada; promoção ainda não entra.</summary>
    public const string CategoriaUtility = "UTILITY";

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public string Nome { get; private set; } = null!;
    public string Idioma { get; private set; } = null!;
    public string Categoria { get; private set; } = null!;
    public string? Cabecalho { get; private set; }
    public string Corpo { get; private set; } = null!;
    public string? Rodape { get; private set; }
    public bool Arquivado { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    /// <summary>Maior número de parâmetro do corpo (eles vão de 1 a N, sem buraco).</summary>
    public int QuantidadeParametros => Numeros(Corpo).DefaultIfEmpty(0).Max();

    private ModeloMensagemLocal() { }

    public static ModeloMensagemLocal Criar(Guid empresaId, string nome, string idioma, string categoria,
        string? cabecalho, string corpo, string? rodape, DateTime agora)
    {
        if (empresaId == Guid.Empty) throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        var modelo = new ModeloMensagemLocal { Id = Guid.NewGuid(), EmpresaId = empresaId, CriadoEm = agora };
        modelo.Editar(nome, idioma, categoria, cabecalho, corpo, rodape, agora);
        return modelo;
    }

    public void Editar(string nome, string idioma, string categoria, string? cabecalho, string corpo, string? rodape, DateTime agora)
    {
        var novoNome = NormalizarNome(nome);
        var novoIdioma = NormalizarIdioma(idioma);
        var novaCategoria = ValidarCategoria(categoria);
        var novoCabecalho = Opcional(cabecalho, "Cabeçalho", CabecalhoTamanhoMaximo);
        var novoCorpo = ValidarCorpo(corpo);
        var novoRodape = Opcional(rodape, "Rodapé", RodapeTamanhoMaximo);
        Nome = novoNome;
        Idioma = novoIdioma;
        Categoria = novaCategoria;
        Cabecalho = novoCabecalho;
        Corpo = novoCorpo;
        Rodape = novoRodape;
        AlteradoEm = agora;
    }

    public void Arquivar(DateTime agora)
    {
        Arquivado = true;
        AlteradoEm = agora;
    }

    public void Desarquivar(DateTime agora)
    {
        Arquivado = false;
        AlteradoEm = agora;
    }

    /// <summary>Minúsculo, só letras sem acento, números e '_' (regra da Meta); é parte da chave única por empresa.</summary>
    public static string NormalizarNome(string? nome)
    {
        var limpo = nome?.Trim().ToLowerInvariant() ?? string.Empty;
        if (limpo.Length == 0) throw new RegraDeDominioVioladaException("Nome é obrigatório.");
        if (!NomeValido().IsMatch(limpo))
            throw new RegraDeDominioVioladaException(
                $"Nome só aceita letras minúsculas sem acento, números e '_', sem espaço, até {NomeTamanhoMaximo} caracteres.");
        return limpo;
    }

    /// <summary>Obrigatório e sem padrão assumido: <c>pt_BR</c>, <c>en</c>, <c>es_AR</c>.</summary>
    public static string NormalizarIdioma(string? idioma)
    {
        var limpo = idioma?.Trim() ?? string.Empty;
        if (limpo.Length == 0) throw new RegraDeDominioVioladaException("Idioma é obrigatório (por exemplo, pt_BR).");
        if (!IdiomaValido().IsMatch(limpo))
            throw new RegraDeDominioVioladaException("Idioma inválido: use o código da Meta, como pt_BR, en ou es_AR.");
        return limpo;
    }

    private static string ValidarCategoria(string? categoria)
    {
        var limpa = Obrigatorio(categoria, "Categoria", CategoriaTamanhoMaximo);
        if (limpa != CategoriaUtility)
            throw new RegraDeDominioVioladaException(
                "Mensagem de promoção ainda não é aceita em mensagem padrão local; use a categoria UTILITY (aviso do pedido).");
        return limpa;
    }

    private static string ValidarCorpo(string? corpo)
    {
        var limpo = Obrigatorio(corpo, "Corpo", CorpoTamanhoMaximo);
        // Fora dos parâmetros não sobra chave nenhuma colada: "{{{1}}}" e "}}" solto também são recusados.
        var resto = ParametroValido().Replace(limpo, "\0");
        if (resto.Contains("{{", StringComparison.Ordinal) || resto.Contains("}}", StringComparison.Ordinal)
            || resto.Contains("\0{", StringComparison.Ordinal) || resto.Contains("}\0", StringComparison.Ordinal)
            || resto.Contains("{\0", StringComparison.Ordinal) || resto.Contains("\0}", StringComparison.Ordinal))
            throw new RegraDeDominioVioladaException("Corpo: use '{{' só nos parâmetros {{1}}, {{2}}, e assim por diante.");

        var numeros = Numeros(limpo).Distinct().Order().ToList();
        if (numeros.Where((n, i) => n != i + 1).Any())
            throw new RegraDeDominioVioladaException("Corpo: os parâmetros devem seguir em sequência, {{1}}, {{2}}, {{3}}, sem pular número.");

        if (ParametroNoInicio().IsMatch(limpo) || ParametroNoFim().IsMatch(limpo))
            throw new RegraDeDominioVioladaException("Corpo: a mensagem não pode começar nem terminar com um parâmetro; ponha um texto antes do primeiro e depois do último.");
        return limpo;
    }

    private static string? Opcional(string? valor, string campo, int maximo)
    {
        var limpo = valor?.Trim() ?? string.Empty;
        if (limpo.Length == 0) return null;
        if (limpo.Length > maximo) throw new RegraDeDominioVioladaException($"{campo} acima de {maximo} caracteres.");
        if (limpo.Contains('\n') || limpo.Contains('\r'))
            throw new RegraDeDominioVioladaException($"{campo} não pode ter mais de uma linha.");
        if (limpo.Contains("{{", StringComparison.Ordinal))
            throw new RegraDeDominioVioladaException($"{campo} não aceita parâmetros '{{{{'.");
        return limpo;
    }

    private static string Obrigatorio(string? valor, string campo, int maximo)
    {
        var limpo = valor?.Trim() ?? string.Empty;
        if (limpo.Length == 0) throw new RegraDeDominioVioladaException($"{campo} é obrigatório.");
        if (limpo.Length > maximo) throw new RegraDeDominioVioladaException($"{campo} acima de {maximo} caracteres.");
        return limpo;
    }

    private static IEnumerable<int> Numeros(string corpo) =>
        ParametroValido().Matches(corpo).Select(m => int.Parse(m.Groups[1].Value));

    [GeneratedRegex(@"^[a-z0-9_]{1,512}$")]
    private static partial Regex NomeValido();

    [GeneratedRegex(@"^[a-z]{2}(_[A-Z]{2})?$")]
    private static partial Regex IdiomaValido();

    [GeneratedRegex(@"\{\{([1-9][0-9]?)\}\}")]
    private static partial Regex ParametroValido();

    [GeneratedRegex(@"^\{\{([1-9][0-9]?)\}\}")]
    private static partial Regex ParametroNoInicio();

    [GeneratedRegex(@"\{\{([1-9][0-9]?)\}\}$")]
    private static partial Regex ParametroNoFim();
}
