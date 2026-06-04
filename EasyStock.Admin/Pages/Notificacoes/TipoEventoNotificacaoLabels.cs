using System.Text;

namespace EasyStock.Admin.Pages.Notificacoes;

/// <summary>
/// Rotulos legiveis para os tipos de evento de notificacao. Fonte unica dos dropdowns
/// de Templates (filtro em Index + seletor em Edit), gerados por humanizacao do nome.
///
/// BUG-008: o filtro hardcoded listava so 9 de 36 eventos e o seletor de criacao 6.
///
/// NOTA DE ARQUITETURA: o EasyStock.Admin e um proxy HTTP SEM ProjectReference para
/// EasyStock.Domain, entao nao enxerga o enum
/// EasyStock.Domain.Enums.Notifications.TipoEventoNotificacao. A lista abaixo e uma
/// COPIA local dos nomes do enum (ordem de declaracao) e precisa ser mantida em
/// sincronia ao adicionar eventos. Melhoria futura (sem copia/sem drift): expor um
/// endpoint na API com o catalogo de eventos e popular os dropdowns via fetch.
/// </summary>
public static class TipoEventoNotificacaoLabels
{
    private static readonly string[] Eventos =
    {
        "ProdutoVencendo", "ProdutoVencido", "TarefaPendente", "ResetSenha",
        "AssinaturaExpirando", "AssinaturaExpirada", "BroadcastSuperAdmin",
        "ConfirmacaoEmail", "AlertaEstoqueCritico",
        "TicketCriado", "TicketRespondidoCliente", "TicketRespondidoAdmin",
        "TicketStatusAlterado", "TicketAtribuido", "TicketEncaminhadoNivel",
        "SlaProximoVencer", "SlaViolado", "BugFixCriado",
        "FaturaCriada", "FaturaVencendo", "FaturaPaga", "FaturaVencida",
        "PagamentoConfirmado", "PagamentoFalhou", "ConviteCsat",
        "PedidoAgendadoHoje", "PedidoAgendadoEm1Hora", "PedidoAgendadoEm10Minutos",
        "RelatorioPronto", "RelatorioFalhou", "RelatorioExpirado",
        "ContaPagarVencendo", "ContaPagarVencida", "ContaReceberVencendo",
        "ContaReceberVencida", "ParcelaRecebida"
    };

    /// <summary>Todos os eventos como (valor do enum, rotulo humanizado), ordenados pelo rotulo.</summary>
    public static IReadOnlyList<(string Valor, string Rotulo)> Opcoes { get; } =
        Eventos
            .Select(e => (Valor: e, Rotulo: Humanizar(e)))
            .OrderBy(x => x.Rotulo, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Quebra um identificador PascalCase em frase legivel ("FaturaPaga" =&gt; "Fatura paga").</summary>
    public static string Humanizar(string pascal)
    {
        if (string.IsNullOrEmpty(pascal)) return pascal;

        var sb = new StringBuilder(pascal.Length + 8);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (i > 0)
            {
                var p = pascal[i - 1];
                var fronteira =
                    (char.IsUpper(c) && !char.IsUpper(p)) ||                                            // letra/digito -> Maiuscula (aB)
                    (char.IsDigit(c) != char.IsDigit(p)) ||                                             // letra <-> digito (a1 / 1a)
                    (char.IsUpper(c) && char.IsUpper(p) && i + 1 < pascal.Length && char.IsLower(pascal[i + 1])); // fim de sigla (ABc)
                if (fronteira) sb.Append(' ');
            }
            sb.Append(char.ToLowerInvariant(c));
        }

        var s = sb.ToString();
        return char.ToUpperInvariant(s[0]) + s[1..];
    }
}
