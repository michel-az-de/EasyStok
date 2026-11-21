namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// #2515: canal sem botão interativo (conexão provisória WPPConnect) manda as opções numeradas no texto. O histórico do
/// atendimento guarda o menu assim em todo canal, para que o "2" do turno seguinte tenha a que se referir.
/// </summary>
public static class MenuNumeradoWhatsApp
{
    public static string Texto(string corpo, IEnumerable<string> titulos) =>
        corpo.TrimEnd() + "\n" + string.Join("\n", titulos.Select((titulo, i) => $"{i + 1}. {titulo}"));
}

/// <summary>
/// #2515: menu numerado pendente por contato na conexão provisória. A resposta "2" (ou o título da opção) volta ao
/// atendimento como o toque no segundo botão, como na Meta e na ponte WAHA (#2072). Vale 24 h e só para a resposta seguinte.
/// </summary>
public interface IMenusNumeradosWppConnect
{
    Task GuardarAsync(string sessao, string contato, IReadOnlyList<(string Id, string Titulo)> opcoes, CancellationToken ct = default);

    /// <summary>A opção que o texto escolhe no menu pendente, ou <c>null</c>. Escolha válida consome o menu.</summary>
    Task<(string Id, string Titulo)?> EscolhaAsync(string sessao, string contato, string? texto, CancellationToken ct = default);
}
