using System.Globalization;
using System.Text;

namespace EasyStock.Domain.ValueObjects.Pix;

/// <summary>
/// Quem recebe o Pix direto da loja (#2233): chave, nome e cidade como o BR Code exige (campos 59 e 60, até 25 e 15
/// caracteres, sem acento). O texto é normalizado na criação para o cadastro mostrar o que o banco do cliente vai ver.
/// </summary>
public sealed record RecebedorPix
{
    public const int NomeTamanhoMaximo = 25;
    public const int CidadeTamanhoMaximo = 15;

    public ChavePix Chave { get; }
    public string Nome { get; }
    public string Cidade { get; }

    private RecebedorPix(ChavePix chave, string nome, string cidade)
    {
        Chave = chave;
        Nome = nome;
        Cidade = cidade;
    }

    public static RecebedorPix Criar(ChavePix chave, string? nome, string? cidade)
    {
        ArgumentNullException.ThrowIfNull(chave);
        return new RecebedorPix(chave,
            Texto(nome, NomeTamanhoMaximo, "Nome do recebedor"),
            Texto(cidade, CidadeTamanhoMaximo, "Cidade do recebedor"));
    }

    /// <summary>Esconde a chave (<see cref="ChavePix.ToString"/>).</summary>
    public override string ToString() => $"RecebedorPix {{ Chave = {Chave}, Nome = {Nome}, Cidade = {Cidade} }}";

    private static string Texto(string? valor, int maximo, string campo)
    {
        var semAcento = new StringBuilder();
        foreach (var c in (valor ?? string.Empty).Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) semAcento.Append(c);
        var limpo = string.Join(' ', semAcento.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (limpo.Length == 0 || limpo.Length > maximo)
            throw new RegraDeDominioVioladaException($"{campo} deve ter de 1 a {maximo} caracteres.");
        if (!limpo.All(c => c is >= ' ' and <= '~'))
            throw new RegraDeDominioVioladaException($"{campo} só aceita letras sem acento, números e pontuação simples.");
        return limpo;
    }
}
