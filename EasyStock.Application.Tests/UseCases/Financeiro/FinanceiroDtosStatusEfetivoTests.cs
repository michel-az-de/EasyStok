using EasyStock.Application.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;

namespace EasyStock.Application.Tests.UseCases.Financeiro;

/// <summary>
/// #1506: o status efetivo exibido usava o dia UTC; das 21h as 24h de Brasilia a parcela que
/// vence hoje aparecia "Vencida". Vencimento e data civil gravada como meia-noite UTC.
/// </summary>
public class FinanceiroDtosStatusEfetivoTests
{
    [Fact]
    public void Parcela_que_vence_hoje_em_Brasilia_nao_aparece_vencida()
    {
        var hoje = HorarioBrasil.HojeInstanteUtc();
        var conta = ContaReceber.Criar(Guid.NewGuid(), null, Guid.NewGuid(), "Cliente", hoje);
        conta.AdicionarParcela(1, 100m, hoje);
        conta.Emitir();

        ContaReceberResult.De(conta).Status.Should().NotBe("Vencida");
    }

    [Fact]
    public void Parcela_que_venceu_ontem_em_Brasilia_aparece_vencida()
    {
        var ontem = HorarioBrasil.CivilComoInstanteUtc(HorarioBrasil.Hoje().AddDays(-1));
        var conta = ContaReceber.Criar(Guid.NewGuid(), null, Guid.NewGuid(), "Cliente", ontem);
        conta.AdicionarParcela(1, 100m, ontem);
        conta.Emitir();

        ContaReceberResult.De(conta).Status.Should().Be("Vencida");
    }
}
