using EasyStock.Application.UseCases.Atendimento.Endereco;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.Services.Storefront;

public sealed record EnderecoCheckout(string Cep, string Logradouro, string Numero,
    string Bairro, string Cidade, string Uf, string? Complemento = null)
{
    public EnderecoCheckout Validar(string? cepEsperado = null, string? numeroEsperado = null)
    {
        var cep = CheckoutCoreService.NormalizarCep(Cep);
        if (cep.Length != 8 || (cepEsperado is not null && cep != CheckoutCoreService.NormalizarCep(cepEsperado)))
            throw new CepInvalidoException();
        var e = this with { Cep = cep, Logradouro = Limpar(Logradouro, 200), Numero = Limpar(Numero, 20),
            Bairro = Limpar(Bairro, 120), Cidade = Limpar(Cidade, 120), Uf = Limpar(Uf, 2).ToUpperInvariant(),
            Complemento = string.IsNullOrWhiteSpace(Complemento) ? null : Limpar(Complemento, 120) };
        if (e.Uf.Length != 2 || !e.Uf.All(char.IsLetter)
            || (!string.IsNullOrWhiteSpace(numeroEsperado) && e.Numero != numeroEsperado.Trim()))
            throw new RegraDeDominioVioladaException("Endereço diverge da cotação de entrega.");
        return e;
    }

    public EnderecoNormalizado Normalizado() => new(Cep, Logradouro, Numero, Complemento, Bairro, Cidade, Uf, null);
    public string Snapshot(string? observacoes) => string.Join(" | ", new[] { observacoes?.Trim(),
        $"[Entrega] {Logradouro}, {Numero}{(Complemento is null ? "" : $", {Complemento}")}, {Bairro}, {Cidade}/{Uf}, CEP {Cep}" }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    private static string Limpar(string? texto, int max)
    {
        var valor = texto?.Trim();
        if (string.IsNullOrEmpty(valor) || valor.Length > max || valor.Any(char.IsControl))
            throw new RegraDeDominioVioladaException("Informe um endereço de entrega completo e válido.");
        return valor;
    }
}
