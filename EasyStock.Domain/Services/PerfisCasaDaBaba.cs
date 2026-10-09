namespace EasyStock.Domain.Services;

public sealed record PerfilCasaDaBaba(string Nome, NivelAcesso Nivel, string? ModuloInicial, Permissao[] Permissoes);

public static class PerfisCasaDaBaba
{
    public static IReadOnlyList<PerfilCasaDaBaba> Iniciais { get; } =
    [
        new("Dona", NivelAcesso.Admin, null,
        [
            .. Enum.GetValues<Modulo>().Select(AcessoModulos.PermissaoDe),
            Permissao.GerenciarUsuarios, Permissao.GerenciarProdutos, Permissao.GerenciarEstoque,
            Permissao.VisualizarRelatorios, Permissao.VisualizarContasAPagar, Permissao.GerenciarContasAPagar,
            Permissao.VisualizarContasAReceber, Permissao.GerenciarContasAReceber,
            Permissao.GerenciarCategoriasFinanceiras, Permissao.GerenciarCentrosCusto, Permissao.AtenderConversas
        ]),
        new("Atendimento", NivelAcesso.Operador, "atendimento",
        [
            Permissao.AcessarModuloCardapio, Permissao.AcessarModuloAtendimento, Permissao.AcessarModuloCozinha,
            Permissao.AcessarModuloCaixa, Permissao.AcessarModuloEntregas,
            Permissao.AtenderConversas, Permissao.GerenciarProdutos
        ]),
        new("Cozinha", NivelAcesso.Operador, "cozinha",
        [Permissao.AcessarModuloProducao, Permissao.AcessarModuloCozinha, Permissao.GerenciarEstoque])
    ];
}
