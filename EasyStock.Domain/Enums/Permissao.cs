namespace EasyStock.Domain.Enums
{
    public enum Permissao
    {
        // Compatibilidade de leitura dos perfis de outras empresas; a Casa da Baba arquiva o legado.
        GerenciarLojas = 0,
        GerenciarUsuarios = 1,
        GerenciarProdutos = 2,
        GerenciarEstoque = 3,
        GerenciarFornecedores = 4,
        VisualizarRelatorios = 5,
        GerarRelatorioVendas = 6,
        AcessarInteligencia,
        VisualizarTickets,
        ResponderTickets,
        GerenciarTickets,
        ResponderTicketsInternos,
        EncaminharTicketNivel,
        RevelarPiiCliente,
        GerarBugFix,
        ConfigurarSla,
        VisualizarFaturas,
        EmitirFatura,
        GerenciarFaturas,
        CancelarFatura,
        GerenciarFaq,
        VisualizarMetricasHelpdesk,
        VisualizarContasAPagar = 22,
        GerenciarContasAPagar,
        VisualizarContasAReceber,
        GerenciarContasAReceber,
        GerenciarCategoriasFinanceiras,
        GerenciarCentrosCusto,
        AtenderConversas,
        AcessarModuloCardapio,
        AcessarModuloProducao,
        AcessarModuloAtendimento,
        AcessarModuloCozinha,
        AcessarModuloCaixa,
        AcessarModuloCampanhas,
        AcessarModuloConfiguracoes,
        AcessarModuloEntregas
    }
}
