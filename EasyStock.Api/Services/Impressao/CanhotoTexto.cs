using System.Globalization;
using System.Text;
using EasyStock.Application.UseCases.Operacao.Impressao;

namespace EasyStock.Api.Services.Impressao;

/// <summary>
/// Canhoto em texto puro para impressora térmica ESC/POS (S20): 42 colunas (bobina de 80 mm em fonte A),
/// só ASCII imprimível (sem acentos: a tabela de caracteres da impressora varia por modelo). Um item por
/// linha lógica com quantidade, porção, molho e observação; o que passa de 42 colunas quebra com recuo.
/// O bridge envia o texto como está e acrescenta o corte de papel.
/// </summary>
public static class CanhotoTexto
{
    public const int Colunas = 42;
    private const string Recuo = "   ";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Formatar(CanhotoDto canhoto)
    {
        ArgumentNullException.ThrowIfNull(canhoto);
        var linhas = new List<string>();
        var c = canhoto.Cabecalho;
        var duplo = new string('=', Colunas);
        var simples = new string('-', Colunas);

        linhas.Add(duplo);
        if (!string.IsNullOrWhiteSpace(c.NomeCasa)) Centralizar(linhas, Ascii(c.NomeCasa).ToUpperInvariant());
        Centralizar(linhas, "PEDIDO #" + Ascii(c.Numero));
        linhas.Add(duplo);
        Campo(linhas, "Cliente", c.Cliente);
        Campo(linhas, "Tel", c.Telefone);
        Campo(linhas, "End", c.Endereco);
        Campo(linhas, "Entrega", c.AgendadoPara is { } a ? a.ToString("dd/MM HH:mm", PtBr) : "para ja");
        Campo(linhas, "Pago em", c.PagoEm?.ToString("dd/MM HH:mm", PtBr));

        foreach (var g in canhoto.Grupos)
        {
            linhas.Add(simples);
            linhas.Add(Ascii(g.Titulo).ToUpperInvariant());
            foreach (var i in g.Itens)
            {
                var sb = new StringBuilder();
                sb.Append(Quantidade(i.Quantidade)).Append("x ").Append(i.Nome);
                if (i.Porcao is not null) sb.Append(" (").Append(i.Porcao).Append(')');
                if (i.Molho is not null) sb.Append(" - molho: ").Append(i.Molho);
                if (i.Observacao is not null) sb.Append(" - obs: ").Append(i.Observacao);
                Quebrar(linhas, Ascii(sb.ToString()), Recuo);
            }
        }

        if (canhoto.Observacoes is not null)
        {
            linhas.Add(simples);
            linhas.Add("OBS DO PEDIDO:");
            Quebrar(linhas, Ascii(canhoto.Observacoes), string.Empty);
        }

        linhas.Add(duplo);
        Quebrar(linhas, Ascii(canhoto.Rodape), string.Empty);
        return string.Join('\n', linhas) + "\n";
    }

    /// <summary>Quantidade sem zeros à direita (<c>2</c>, <c>1,5</c>).</summary>
    public static string Quantidade(decimal qtd) => qtd.ToString("0.###", PtBr);

    /// <summary>Remove acentos e troca o que não é ASCII imprimível (travessão, aspas curvas, quebra de linha).</summary>
    public static string Ascii(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var ch in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch switch
            {
                >= ' ' and <= '~' => ch,
                '–' or '—' or '−' => '-',
                '‘' or '’' or '´' => '\'',
                '“' or '”' => '"',
                'ª' => 'a',
                'º' or '°' => 'o',
                '\r' or '\n' or '\t' or ' ' => ' ',
                _ => '?',
            });
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static void Campo(List<string> linhas, string rotulo, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return;
        Quebrar(linhas, $"{rotulo}: {Ascii(valor)}", Recuo);
    }

    private static void Centralizar(List<string> linhas, string texto)
    {
        if (texto.Length >= Colunas)
            Quebrar(linhas, texto, string.Empty);
        else
            linhas.Add(new string(' ', (Colunas - texto.Length) / 2) + texto);
    }

    /// <summary>
    /// Quebra por palavra em <see cref="Colunas"/>; a continuação ganha <paramref name="recuo"/>. Palavra maior
    /// que a linha é cortada.
    /// </summary>
    private static void Quebrar(List<string> linhas, string texto, string recuo)
    {
        var atual = new StringBuilder();
        var continuacao = false;
        foreach (var palavra in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var resto = palavra;
            while (resto.Length > 0)
            {
                var inicio = atual.Length == 0;
                var prefixo = inicio ? (continuacao ? recuo : string.Empty) : " ";
                var livre = Colunas - atual.Length - prefixo.Length;
                if (resto.Length <= livre)
                {
                    atual.Append(prefixo).Append(resto);
                    resto = string.Empty;
                    continue;
                }

                if (!inicio && (resto.Length <= Colunas - recuo.Length || livre <= 1))
                {
                    linhas.Add(atual.ToString());
                    atual.Clear();
                    continuacao = true;
                    continue;
                }

                atual.Append(prefixo).Append(resto[..livre]);
                resto = resto[livre..];
                linhas.Add(atual.ToString());
                atual.Clear();
                continuacao = true;
            }
        }
        if (atual.Length > 0) linhas.Add(atual.ToString());
    }
}
