namespace EasyStock.Domain.Services;

public sealed record PerfilCasaDaBaba(string Nome, NivelAcesso Nivel, string? ModuloInicial, Permissao[] Permissoes);

public static class PerfisCasaDaBaba
{
    // Um perfil personalizado que amplia a cozinha não herda a permanência do tablet.
    public static bool PermiteSessaoPersistente(Perfil perfil, Guid empresaId)
    {
        var cozinha = Iniciais.Single(p => p.Nome == "Cozinha");
        return perfil.EmpresaId == empresaId && empresaId != Guid.Empty
            && string.Equals(perfil.Nome, cozinha.Nome, StringComparison.OrdinalIgnoreCase)
            && perfil.Nivel == cozinha.Nivel
            && cozinha.Permissoes.ToHashSet().SetEquals(perfil.Permissoes.Select(p => p.Permissao));
    }

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
