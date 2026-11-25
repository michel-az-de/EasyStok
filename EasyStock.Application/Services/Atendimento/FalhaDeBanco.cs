namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #3340: falha do banco (o PostgreSQL recusou ou a conexão caiu) numa ferramenta do Assistente. O turno é interrompido e a
/// retomada o refaz; escalar para a dona não resolve nada que o banco volte a aceitar. Qualquer outra exceção de ferramenta
/// (a Meta fora do ar, timeout de rede) é falha do turno: a conversa vai à equipe.
/// </summary>
internal static class FalhaDeBanco
{
    public static bool Eh(Exception ex)
    {
        for (var atual = ex; atual is not null; atual = atual.InnerException)
            if (atual is System.Data.Common.DbException) return true;
        return false;
    }
}
