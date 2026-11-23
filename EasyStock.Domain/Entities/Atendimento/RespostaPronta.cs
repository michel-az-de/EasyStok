using System.Text.RegularExpressions;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Resposta pronta da biblioteca do console (S42, US-009): título, atalho único por empresa e texto
/// com variáveis renderizadas no servidor (<see cref="ModeloTextoAtendimento"/>).
/// </summary>
public partial class RespostaPronta
{
    public const int TituloTamanhoMaximo = 120;
    public const int AtalhoTamanhoMaximo = 40;
    public const int TextoTamanhoMaximo = 4096;
    public const int CategoriaTamanhoMaximo = 40;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public string Titulo { get; private set; } = null!;
    public string Atalho { get; private set; } = null!;
    public string Texto { get; private set; } = null!;
    /// <summary>S83: agrupa o seletor do chat; opcional, uma linha só. Registro antigo fica nulo.</summary>
    public string? Categoria { get; private set; }
    public bool Arquivada { get; private set; }
    public DateTime CriadaEm { get; private set; }
    public DateTime AlteradaEm { get; private set; }

    private RespostaPronta() { }

    public static RespostaPronta Criar(Guid empresaId, string titulo, string atalho, string texto, DateTime agora, string? categoria = null)
    {
        if (empresaId == Guid.Empty) throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        var resposta = new RespostaPronta { Id = Guid.NewGuid(), EmpresaId = empresaId, CriadaEm = agora };
        resposta.Editar(titulo, atalho, texto, agora, categoria);
        return resposta;
    }

    public void Editar(string titulo, string atalho, string texto, DateTime agora, string? categoria = null)
    {
        var novoTitulo = Obrigatorio(titulo, "Título", TituloTamanhoMaximo);
        var novoAtalho = NormalizarAtalho(atalho);
        var novoTexto = Obrigatorio(texto, "Texto", TextoTamanhoMaximo);
        var novaCategoria = NormalizarCategoria(categoria);
        if (novoTexto != Texto && ModeloTextoAtendimento.ChavesDesconhecidas(novoTexto) is { } desconhecidas)
            throw new RegraDeDominioVioladaException(desconhecidas);
        Titulo = novoTitulo;
        Atalho = novoAtalho;
        Texto = novoTexto;
        Categoria = novaCategoria;
        AlteradaEm = agora;
    }

    public void Arquivar(DateTime agora)
    {
        Arquivada = true;
        AlteradaEm = agora;
    }

    public void Desarquivar(DateTime agora)
    {
        Arquivada = false;
        AlteradaEm = agora;
    }

    public string Renderizar(IReadOnlyDictionary<string, string?> valores) =>
        ModeloTextoAtendimento.Renderizar(Texto, valores);

    /// <summary>Minúsculo, sem espaço; é a chave única por empresa.</summary>
    public static string NormalizarAtalho(string? atalho)
    {
        var limpo = atalho?.Trim().ToLowerInvariant() ?? string.Empty;
        if (limpo.Length == 0) throw new RegraDeDominioVioladaException("Atalho é obrigatório.");
        if (limpo.Length > AtalhoTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"Atalho acima de {AtalhoTamanhoMaximo} caracteres.");
        if (!AtalhoValido().IsMatch(limpo))
            throw new RegraDeDominioVioladaException("Atalho só aceita letras, números, '/', '-' e '_', sem espaço.");
        return limpo;
    }

    private static string? NormalizarCategoria(string? categoria)
    {
        var limpa = categoria?.Trim();
        if (string.IsNullOrEmpty(limpa)) return null;
        if (limpa.Length > CategoriaTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"Categoria acima de {CategoriaTamanhoMaximo} caracteres.");
        if (limpa.AsSpan().ContainsAny('\r', '\n'))
            throw new RegraDeDominioVioladaException("Categoria deve ter uma linha só.");
        return limpa;
    }

    internal static string Obrigatorio(string? valor, string campo, int maximo)
    {
        var limpo = valor?.Trim() ?? string.Empty;
        if (limpo.Length == 0) throw new RegraDeDominioVioladaException($"{campo} é obrigatório.");
        if (limpo.Length > maximo) throw new RegraDeDominioVioladaException($"{campo} acima de {maximo} caracteres.");
        return limpo;
    }

    [GeneratedRegex(@"^[\p{L}\p{N}/_\-]+$")]
    private static partial Regex AtalhoValido();
}
