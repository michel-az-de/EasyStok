using System.Text;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// S54: bloco do caderno da loja no prompt do agente. O núcleo vai inteiro; os demais trechos entram como
/// uma linha de índice e o texto vem por <c>consultar_caderno</c>. Vazio quando não há trecho ativo, para o
/// prompt de quem não usa o caderno ficar igual. A ordem segue a do repositório (título), estável para o cache.
/// </summary>
public static class CadernoParaAgente
{
    public static string Montar(IReadOnlyList<TrechoCaderno> trechos)
    {
        var ativos = trechos.Where(t => !t.Arquivado).ToList();
        if (ativos.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("Caderno da loja (escrito pela loja; vale mais que o seu conhecimento geral):");

        foreach (var t in ativos.Where(t => t.Nucleo))
        {
            sb.AppendLine();
            sb.AppendLine($"{t.Titulo}:");
            sb.AppendLine(t.Texto);
        }

        var indice = ativos.Where(t => !t.Nucleo).ToList();
        if (indice.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Índice do caderno. Antes de responder sobre um destes temas, use consultar_caderno com o código. " +
                          "Se o tema não estiver no caderno nem nas ferramentas, não invente: passe para a dona.");
            foreach (var t in indice)
                sb.AppendLine(string.IsNullOrEmpty(t.PalavrasChave)
                    ? $"- [{t.Codigo}] {t.Titulo}"
                    : $"- [{t.Codigo}] {t.Titulo} · {t.PalavrasChave}");
        }

        return sb.ToString().TrimEnd();
    }
}
