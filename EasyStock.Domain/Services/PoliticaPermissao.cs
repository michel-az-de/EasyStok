namespace EasyStock.Domain.Services;

/// <summary>
/// Permissão efetiva de um usuário numa empresa. Com permissões explícitas no perfil, valem só elas;
/// sem nenhuma, vale o fallback pelo nível. Usada para o usuário logado (claims do JWT) e para avaliar
/// outro usuário no servidor, como o destino de uma transferência de conversa (S41).
/// </summary>
public static class PoliticaPermissao
{
    public static bool Tem(NivelAcesso nivel, IReadOnlyCollection<Permissao> explicitas, Permissao permissao)
    {
        if (explicitas.Count > 0)
            return explicitas.Contains(permissao);

        return nivel switch
        {
            NivelAcesso.SuperAdmin => true,
            NivelAcesso.Admin => permissao is not Permissao.ConfigurarSla,
            NivelAcesso.Gerente => permissao is not Permissao.GerenciarUsuarios
                and not Permissao.ConfigurarSla,
            NivelAcesso.Operador => permissao is Permissao.GerenciarEstoque or Permissao.GerenciarProdutos
                or Permissao.VisualizarTickets or Permissao.ResponderTickets
                or Permissao.ResponderTicketsInternos or Permissao.AtenderConversas,
            _ => permissao is Permissao.VisualizarRelatorios or Permissao.VisualizarTickets
        };
    }
}
