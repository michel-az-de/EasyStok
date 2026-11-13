using EasyStock.Application.UseCases.Atendimento.Endereco;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.Services.Storefront;

public sealed record EnderecoCheckout(string Cep, string Logradouro, string Numero,
    string Bairro, string Cidade, string Uf, string? Complemento = null)
{
    public EnderecoCheckout Validar(string? cepEsperado = null, string? numeroEsperado = null)
    {
        var cep = EnderecoBr.NormalizarCep(Cep);
        if (cep.Length != 8 || (cepEsperado is not null && cep != EnderecoBr.NormalizarCep(cepEsperado)))
            throw new CepInvalidoException();
        var e = this with { Cep = cep, Logradouro = Limpar(Logradouro, 200), Numero = Limpar(Numero, 20),
            Bairro = Limpar(Bairro, 100), Cidade = Limpar(Cidade, 100), Uf = Limpar(Uf, 2).ToUpperInvariant(),
            Complemento = string.IsNullOrWhiteSpace(Complemento) ? null : Limpar(Complemento, 100) };
        if (e.Uf.Length != 2 || !e.Uf.All(char.IsLetter)
            || (!string.IsNullOrWhiteSpace(numeroEsperado) && e.Numero != numeroEsperado.Trim()))
            throw new RegraDeDominioVioladaException("Endereço diverge da cotação de entrega.");
        return e;
    }

    public EnderecoNormalizado Normalizado() => new(Cep, Logradouro, Numero, Complemento, Bairro, Cidade, Uf, null);
    public string Snapshot(string? observacoes) => string.Join(" | ", new[] { observacoes?.Trim(),
        $"[Entrega] {Logradouro}, {Numero}{(Complemento is null ? "" : $", {Complemento}")}, {Bairro}, {Cidade}/{Uf}, CEP {Cep}" }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    private const string Separador = " | ";
    private const string MarcaRetrato = "[Entrega] ";

    /// <summary>
    /// #2007 (item 11): o que o cliente escreveu, sem o retrato "[Entrega] ..." que o <see cref="Snapshot"/> grava para a
    /// equipe. Nulo quando só havia o retrato.
    /// </summary>
    public static string? SemRetratoDaEntrega(string? observacoes)
    {
        var partes = Partes(observacoes).Where(p => !p.StartsWith(MarcaRetrato, StringComparison.Ordinal)).ToList();
        return partes.Count == 0 ? null : string.Join(Separador, partes);
    }

    /// <summary>
    /// As observações <paramref name="novas"/> do cliente com o retrato da entrega que <paramref name="anteriores"/>
    /// tinham: aplicar uma alteração pelo link não apaga nem duplica o endereço gravado no checkout.
    /// </summary>
    public static string? ComRetratoDaEntregaDe(string? anteriores, string? novas)
    {
        var partes = Partes(SemRetratoDaEntrega(novas))
            .Concat(Partes(anteriores).Where(p => p.StartsWith(MarcaRetrato, StringComparison.Ordinal))).ToList();
        return partes.Count == 0 ? null : string.Join(Separador, partes);
    }

    private static IEnumerable<string> Partes(string? observacoes) =>
        (observacoes ?? string.Empty).Split(Separador, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Limpar(string? texto, int max)
    {
        var valor = texto?.Trim();
        if (string.IsNullOrEmpty(valor) || valor.Length > max || valor.Any(char.IsControl))
            throw new RegraDeDominioVioladaException("Informe um endereço de entrega completo e válido.");
        return valor;
    }
}
