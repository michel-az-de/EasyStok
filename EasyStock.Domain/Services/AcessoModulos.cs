namespace EasyStock.Domain.Services;

public static class AcessoModulos
{
    public static Permissao PermissaoDe(Modulo modulo) => modulo switch
    {
        Modulo.Cardapio => Permissao.AcessarModuloCardapio,
        Modulo.Producao => Permissao.AcessarModuloProducao,
        Modulo.Atendimento => Permissao.AcessarModuloAtendimento,
        Modulo.Cozinha => Permissao.AcessarModuloCozinha,
        Modulo.Caixa => Permissao.AcessarModuloCaixa,
        Modulo.Campanhas => Permissao.AcessarModuloCampanhas,
        Modulo.Configuracoes => Permissao.AcessarModuloConfiguracoes,
        Modulo.Entregas => Permissao.AcessarModuloEntregas,
        _ => throw new ArgumentOutOfRangeException(nameof(modulo))
    };

    // O Console já publica o módulo de caixa com este identificador.
    public static string IdDe(Modulo modulo) => modulo == Modulo.Caixa ? "financeiro" : modulo.ToString().ToLowerInvariant();

    public static bool EhPermissaoDeModulo(Permissao permissao) =>
        Enum.GetValues<Modulo>().Any(m => PermissaoDe(m) == permissao);

    public static bool Fallback(NivelAcesso nivel, Permissao permissao) => nivel switch
    {
        NivelAcesso.SuperAdmin or NivelAcesso.Admin => true,
        NivelAcesso.Gerente => permissao != Permissao.AcessarModuloConfiguracoes,
        NivelAcesso.Operador => permissao is Permissao.AcessarModuloAtendimento or Permissao.AcessarModuloCozinha
            or Permissao.AcessarModuloCaixa or Permissao.AcessarModuloEntregas,
        _ => false
    };
}
