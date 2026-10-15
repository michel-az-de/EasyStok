namespace EasyStock.Application.UseCases.Operacao.Impressao;

/// <summary>
/// Endereço de entrega em uma linha, igual no canhoto (S20) e no impresso do pedido (S49):
/// "rua, complemento - bairro, cidade - CEP 01001-000". O complemento do cadastro vence o apartamento do
/// pedido. Sem nenhuma parte, <c>null</c>.
/// </summary>
public static class EnderecoImpresso
{
    public static string? Montar(
        string? endereco, string? complemento, string? apt, string? bairro, string? cidade, string? cep)
    {
        var partes = new List<string>();
        var rua = string.Join(", ", new[] { Limpo(endereco), Limpo(complemento) ?? Limpo(apt) }
            .Where(p => p is not null));
        if (rua.Length > 0) partes.Add(rua);
        var local = string.Join(", ", new[] { Limpo(bairro), Limpo(cidade) }.Where(p => p is not null));
        if (local.Length > 0) partes.Add(local);
        if (Limpo(cep) is { } c)
        {
            var digitos = new string(c.Where(char.IsDigit).ToArray());
            partes.Add("CEP " + (digitos.Length == 8 ? $"{digitos[..5]}-{digitos[5..]}" : c));
        }
        return partes.Count == 0 ? null : string.Join(" - ", partes);
    }

    private static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
