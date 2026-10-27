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

    /// <summary>Nome antigo do perfil de nível Admin (#2070): o seed renomeia a linha para <see cref="Administracao"/>.</summary>
    public const string NomeAntigoDaAdministracao = "Dona";

    public const string Administracao = "Administração";

    // #2070: "Dona" na tela vem só da marca do vínculo (UsuarioEmpresa.EhDona); o perfil de nível Admin é a Administração.
    public static IReadOnlyList<PerfilCasaDaBaba> Iniciais { get; } =
    [
        new(Administracao, NivelAcesso.Admin, null,
        [
            .. Enum.GetValues<Modulo>().Select(AcessoModulos.PermissaoDe),
            Permissao.GerenciarUsuarios, Permissao.GerenciarProdutos, Permissao.GerenciarEstoque,
            Permissao.VisualizarRelatorios, Permissao.VisualizarContasAPagar, Permissao.GerenciarContasAPagar,
            Permissao.VisualizarContasAReceber, Permissao.GerenciarContasAReceber,
            Permissao.GerenciarCategoriasFinanceiras, Permissao.GerenciarCentrosCusto, Permissao.AtenderConversas
        ]),
        new("Gerência", NivelAcesso.Gerente, null,
        [
            .. Enum.GetValues<Modulo>().Where(m => m != Modulo.Configuracoes).Select(AcessoModulos.PermissaoDe),
            Permissao.AtenderConversas, Permissao.GerenciarProdutos, Permissao.GerenciarEstoque, Permissao.VisualizarRelatorios
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
