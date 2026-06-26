using System.Linq.Expressions;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Predicado canônico de "ticket em aberto" para o back-office (issue 692). Centralizado para
/// que Dashboard, Diagnóstico e Operação usem a MESMA definição: em aberto = qualquer status
/// que não seja Resolvido nem Fechado (Aberto + EmAtendimento + AguardandoCliente). Antes,
/// Dashboard e Diagnóstico contavam só Status==Aberto, divergindo da Operação/Fleet — o que
/// gerava números diferentes para o mesmo conceito entre telas.
/// </summary>
public static class AdminTicketFilters
{
    public static readonly Expression<Func<AdminTicket, bool>> EmAberto =
        t => t.Status != TicketStatus.Fechado && t.Status != TicketStatus.Resolvido;
}
