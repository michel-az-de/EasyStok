using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Caixa;

namespace EasyStock.Application.Tests.UseCases.Caixa;

/// <summary>
/// #1562: Console e Web fecham o Caixa sem loja, e esse fechamento soma os recebimentos de todas as lojas.
/// A guarda precisa enxergá-lo para o pedido que tem loja. O inverso não vale: o fechamento de uma loja não
/// soma pedido sem loja.
/// </summary>
public class CaixaFechadoGuardTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Loja = Guid.NewGuid();
    // 15h UTC = meio-dia em Brasília: o dia operacional não depende do fuso de quem roda o teste.
    private static readonly DateTime Instante = new(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Dia = HorarioBrasil.DataOperacional(Instante);

    private readonly ICaixaRepository _caixa = Substitute.For<ICaixaRepository>();

    private FechamentoCaixa Fechar(Guid? lojaId, DateTime fechadoEm)
    {
        var fechamento = FechamentoCaixa.Criar(Empresa, Dia, 0, 0, 25, 0, 0, lojaId);
        fechamento.FechadoEm = fechadoEm;
        _caixa.GetFechamentoDoDiaAsync(Empresa, Dia, lojaId).Returns(fechamento);
        return fechamento;
    }

    [Fact]
    public async Task Dia_fechado_pela_empresa_recusa_operacao_de_pedido_com_loja()
    {
        Fechar(lojaId: null, Instante.AddHours(1));

        await FluentActions.Invoking(() => CaixaFechadoGuard.ExigirDiaAbertoAsync(_caixa, Empresa, Instante, Loja, "dia fechado"))
            .Should().ThrowAsync<UseCaseValidationException>().WithMessage("dia fechado");
    }

    [Fact]
    public async Task Dia_fechado_so_por_uma_loja_nao_recusa_operacao_de_pedido_sem_loja()
    {
        Fechar(Loja, Instante.AddHours(1));

        await FluentActions.Invoking(() => CaixaFechadoGuard.ExigirDiaAbertoAsync(_caixa, Empresa, Instante, null, "dia fechado"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task Dia_fechado_por_outra_loja_nao_recusa()
    {
        Fechar(Guid.NewGuid(), Instante.AddHours(1));

        await FluentActions.Invoking(() => CaixaFechadoGuard.ExigirDiaAbertoAsync(_caixa, Empresa, Instante, Loja, "dia fechado"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task Recebimento_de_pedido_com_loja_retratado_no_fechamento_da_empresa_e_recusado()
    {
        Fechar(lojaId: null, Instante.AddHours(1));

        await FluentActions.Invoking(() => CaixaFechadoGuard.ExigirRecebimentoForaDoRetratoAsync(_caixa, Empresa, Instante, Loja))
            .Should().ThrowAsync<UseCaseValidationException>().WithMessage(CaixaFechadoGuard.RecebimentoEmDiaFechado);
    }

    [Fact]
    public async Task Recebimento_lancado_depois_do_fechamento_da_empresa_segue_livre()
    {
        Fechar(lojaId: null, Instante.AddMinutes(-30));

        await FluentActions.Invoking(() => CaixaFechadoGuard.ExigirRecebimentoForaDoRetratoAsync(_caixa, Empresa, Instante, Loja))
            .Should().NotThrowAsync();
    }

    // Os dois fechamentos existem: basta um deles ter retratado o recebimento.
    [Fact]
    public async Task Recebimento_fora_do_retrato_da_loja_mas_dentro_do_da_empresa_e_recusado()
    {
        Fechar(Loja, Instante.AddMinutes(-30));
        Fechar(lojaId: null, Instante.AddHours(1));

        await FluentActions.Invoking(() => CaixaFechadoGuard.ExigirRecebimentoForaDoRetratoAsync(_caixa, Empresa, Instante, Loja))
            .Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task Recebimento_de_pedido_sem_loja_ignora_fechamento_de_uma_loja()
    {
        Fechar(Loja, Instante.AddHours(1));

        await FluentActions.Invoking(() => CaixaFechadoGuard.ExigirRecebimentoForaDoRetratoAsync(_caixa, Empresa, Instante, null))
            .Should().NotThrowAsync();
    }
}
