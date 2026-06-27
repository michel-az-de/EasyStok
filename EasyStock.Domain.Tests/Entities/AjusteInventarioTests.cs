using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

public class AjusteInventarioTests
{
    [Fact]
    public void Criar_inicia_sem_linhas_e_agregados_zerados()
    {
        var aj = AjusteInventario.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        aj.Linhas.Should().BeEmpty();
        aj.TotalMutados.Should().Be(0);
        aj.TotalCriados.Should().Be(0);
        aj.TotalZerados.Should().Be(0);
        aj.CustoTotalPerda.Should().Be(0m);
    }

    [Fact]
    public void Linha_calcula_delta_com_sinal()
    {
        var aj = AjusteInventario.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var falta = aj.AdicionarLinha(Guid.NewGuid(), Guid.NewGuid(),
            Quantidade.From(5), Quantidade.From(3), Dinheiro.FromDecimal(10m), TipoAjusteLinha.Falta);
        var sobra = aj.AdicionarLinha(Guid.NewGuid(), Guid.NewGuid(),
            Quantidade.From(2), Quantidade.From(6), Dinheiro.FromDecimal(10m), TipoAjusteLinha.Sobra);

        falta.Delta.Should().Be(-2m);
        sobra.Delta.Should().Be(4m);
        aj.Linhas.Should().HaveCount(2);
    }

    [Fact]
    public void Agregados_contam_por_tipo()
    {
        var aj = AjusteInventario.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        aj.AdicionarLinha(Guid.NewGuid(), Guid.NewGuid(), Quantidade.From(5), Quantidade.From(3), Dinheiro.FromDecimal(10m), TipoAjusteLinha.Falta);
        aj.AdicionarLinha(Guid.NewGuid(), Guid.NewGuid(), Quantidade.From(2), Quantidade.From(6), Dinheiro.FromDecimal(10m), TipoAjusteLinha.Sobra);
        aj.AdicionarLinha(Guid.NewGuid(), Guid.NewGuid(), Quantidade.Zero, Quantidade.From(4), Dinheiro.FromDecimal(10m), TipoAjusteLinha.LoteNovo);
        aj.AdicionarLinha(Guid.NewGuid(), Guid.NewGuid(), Quantidade.From(8), Quantidade.Zero, Dinheiro.FromDecimal(10m), TipoAjusteLinha.LoteZerado);

        aj.TotalMutados.Should().Be(2);
        aj.TotalCriados.Should().Be(1);
        aj.TotalZerados.Should().Be(1);
    }

    [Fact]
    public void CustoTotalPerda_soma_so_deltas_negativos_vezes_custo()
    {
        var aj = AjusteInventario.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        // falta de 2 a R$10,00 = R$20,00 de perda
        aj.AdicionarLinha(Guid.NewGuid(), Guid.NewGuid(), Quantidade.From(5), Quantidade.From(3), Dinheiro.FromDecimal(10m), TipoAjusteLinha.Falta);
        // zerado de 8 a R$2,50 = R$20,00 de perda
        aj.AdicionarLinha(Guid.NewGuid(), Guid.NewGuid(), Quantidade.From(8), Quantidade.Zero, Dinheiro.FromDecimal(2.50m), TipoAjusteLinha.LoteZerado);
        // sobra: NAO conta como perda
        aj.AdicionarLinha(Guid.NewGuid(), Guid.NewGuid(), Quantidade.From(1), Quantidade.From(9), Dinheiro.FromDecimal(10m), TipoAjusteLinha.Sobra);

        aj.CustoTotalPerda.Should().Be(40m); // 2*10,00 + 8*2,50
    }
}
