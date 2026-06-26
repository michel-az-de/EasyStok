using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Repositories;

/// <summary>
/// Issue 692: "tickets em aberto" precisa ter a MESMA definição em Dashboard, Diagnóstico e
/// Operação. O predicado canônico <see cref="AdminTicketFilters.EmAberto"/> conta tudo que
/// não é Resolvido nem Fechado. Antes, Dashboard/Diagnóstico contavam só Aberto e divergiam.
/// </summary>
public class AdminTicketFiltersTests
{
    [Theory]
    [InlineData(TicketStatus.Aberto, true)]
    [InlineData(TicketStatus.EmAtendimento, true)]
    [InlineData(TicketStatus.AguardandoCliente, true)]
    [InlineData(TicketStatus.Resolvido, false)]
    [InlineData(TicketStatus.Fechado, false)]
    public void EmAberto_conta_tudo_exceto_resolvido_e_fechado(TicketStatus status, bool esperado)
    {
        var emAberto = AdminTicketFilters.EmAberto.Compile();

        emAberto(new AdminTicket { Status = status }).Should().Be(esperado);
    }
}
