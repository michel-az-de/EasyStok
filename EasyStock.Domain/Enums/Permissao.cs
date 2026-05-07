namespace EasyStock.Domain.Enums
{
    public enum Permissao
    {
        GerenciarLojas,
        GerenciarUsuarios,
        GerenciarProdutos,
        GerenciarEstoque,
        GerenciarFornecedores,
        VisualizarRelatorios,
        GerarRelatorioVendas,
        AcessarInteligencia,
        VisualizarTickets,
        ResponderTickets,
        GerenciarTickets,
        ResponderTicketsInternos,
        EncaminharTicketNivel,
        RevelarPiiCliente,
        GerarBugFix,
        ConfigurarSla,

        // Modulo Financeiro (F1+)
        VisualizarFaturas,
        EmitirFatura,
        GerenciarFaturas,
        CancelarFatura,
        EstornarPagamento,
        ReenviarFaturaCliente
    }
}
