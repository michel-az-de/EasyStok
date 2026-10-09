namespace EasyStock.Domain.Services;

public static class PermissoesLegadas
{
    public static IReadOnlySet<Permissao> Valores { get; } = new HashSet<Permissao>
    {
        Permissao.GerenciarLojas, Permissao.GerenciarFornecedores, Permissao.GerarRelatorioVendas,
        Permissao.AcessarInteligencia, Permissao.VisualizarTickets, Permissao.ResponderTickets,
        Permissao.GerenciarTickets, Permissao.ResponderTicketsInternos, Permissao.EncaminharTicketNivel,
        Permissao.RevelarPiiCliente, Permissao.GerarBugFix, Permissao.ConfigurarSla,
        Permissao.VisualizarFaturas, Permissao.EmitirFatura, Permissao.GerenciarFaturas,
        Permissao.CancelarFatura, Permissao.GerenciarFaq, Permissao.VisualizarMetricasHelpdesk
    };
}
