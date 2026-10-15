namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Trecho do caderno da loja (S54): conhecimento da empresa que o agente de atendimento usa. Trecho do
/// <see cref="Nucleo"/> entra sempre no prompt; os demais entram só como uma linha de índice e o texto vem
/// pela ferramenta <c>consultar_caderno</c>, pelo <see cref="Codigo"/>.
/// </summary>
public class TrechoCaderno
{
    public const int TituloTamanhoMaximo = 120;
    public const int TextoTamanhoMaximo = 4000;
    public const int PalavrasChaveTamanhoMaximo = 300;

    /// <summary>Soma do texto dos trechos ativos do núcleo, que vão em toda chamada ao modelo.</summary>
    public const int NucleoTamanhoMaximoTotal = 6000;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public string Titulo { get; private set; } = null!;
    public string Texto { get; private set; } = null!;
    public string PalavrasChave { get; private set; } = string.Empty;
    public bool Nucleo { get; private set; }
    public bool Arquivado { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    /// <summary>8 primeiros hexadecimais do <see cref="Id"/>: curto para o índice do prompt e estável.</summary>
    public string Codigo => Id.ToString("N")[..8];

    private TrechoCaderno() { }

    public static TrechoCaderno Criar(Guid empresaId, string titulo, string texto, string? palavrasChave, bool nucleo, DateTime agora)
    {
        if (empresaId == Guid.Empty) throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        var trecho = new TrechoCaderno { Id = Guid.NewGuid(), EmpresaId = empresaId, CriadoEm = agora };
        trecho.Editar(titulo, texto, palavrasChave, nucleo, agora);
        return trecho;
    }

    public void Editar(string titulo, string texto, string? palavrasChave, bool nucleo, DateTime agora)
    {
        Titulo = RespostaPronta.Obrigatorio(titulo, "Título", TituloTamanhoMaximo);
        Texto = RespostaPronta.Obrigatorio(texto, "Texto", TextoTamanhoMaximo);
        var palavras = NormalizarPalavrasChave(palavrasChave);
        if (palavras.Length > PalavrasChaveTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"Palavras-chave acima de {PalavrasChaveTamanhoMaximo} caracteres.");
        PalavrasChave = palavras;
        Nucleo = nucleo;
        AlteradoEm = agora;
    }

    public void Arquivar(bool arquivado, DateTime agora)
    {
        Arquivado = arquivado;
        AlteradoEm = agora;
    }

    /// <summary>Minúsculas, separadas por ", ", sem vazias nem repetidas; aceita vírgula ou ponto e vírgula.</summary>
    public static string NormalizarPalavrasChave(string? palavrasChave) =>
        string.Join(", ", (palavrasChave ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.ToLowerInvariant())
            .Distinct());
}
