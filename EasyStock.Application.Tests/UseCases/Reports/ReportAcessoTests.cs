using EasyStock.Application.Ports.Output;
using EasyStock.Application.Reporting;
using EasyStock.Application.UseCases.Reports;

namespace EasyStock.Application.Tests.UseCases.Reports;

/// <summary>
/// #1508: preview e data executavam o relatorio sem conferir a permissao da definicao, que so o
/// enqueue conferia. Os tres caminhos agora passam pela mesma regra (<see cref="ReportAcesso"/>).
/// </summary>
public sealed class ReportAcessoTests
{
    private const string Chave = "vendas.por-periodo";

    private static ReportRegistry Registry()
    {
        var definicao = Substitute.For<IReportDefinition>();
        definicao.Key.Returns(Chave);
        definicao.PermissaoRequerida.Returns("relatorios.vendas.consultar");
        return new ReportRegistry([definicao]);
    }

    private static ICurrentUserAccessor Usuario(NivelAcesso nivel, bool veRelatorios = true)
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.Nivel.Returns(nivel);
        usuario.EmpresaId.Returns(Guid.NewGuid());
        usuario.TemPermissao(Arg.Any<Permissao>()).Returns(veRelatorios);
        return usuario;
    }

    [Fact]
    public async Task Preview_SemPermissaoDoRelatorio_RecusaAntesDeExecutar()
    {
        var servicos = Substitute.For<IServiceProvider>();
        var useCase = new PreviewReportUseCase(Registry(), servicos, Usuario(NivelAcesso.Operador, veRelatorios: false));

        var act = () => useCase.ExecuteAsync(new PreviewReportQuery(Chave, "{}"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        servicos.DidNotReceiveWithAnyArgs().GetService(default!);
    }

    [Fact]
    public async Task Data_SemPermissaoDoRelatorio_RecusaAntesDeExecutar()
    {
        var servicos = Substitute.For<IServiceProvider>();
        var useCase = new GetReportDataUseCase(Registry(), servicos, Usuario(NivelAcesso.Operador, veRelatorios: false));

        var act = () => useCase.ExecuteAsync(new GetReportDataQuery(Chave, "{}"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        servicos.DidNotReceiveWithAnyArgs().GetService(default!);
    }

    [Fact]
    public async Task Preview_RelatorioInexistente_ContinuaNaoEncontrado()
    {
        var useCase = new PreviewReportUseCase(Registry(), Substitute.For<IServiceProvider>(), Usuario(NivelAcesso.Admin));

        var resultado = await useCase.ExecuteAsync(new PreviewReportQuery("nao.existe", "{}"), CancellationToken.None);

        resultado.Should().BeNull();
    }

    [Theory]
    [InlineData(NivelAcesso.SuperAdmin, true)]
    [InlineData(NivelAcesso.Admin, false)]
    [InlineData(NivelAcesso.Gerente, false)]
    [InlineData(NivelAcesso.Operador, false)]
    public void Regra_EhAMesmaDoEnqueue(NivelAcesso nivel, bool permitido)
    {
        var definicao = Registry().Get(Chave);

        var act = () => ReportAcesso.Garantir(Usuario(nivel), definicao);

        if (permitido) act.Should().NotThrow();
        else act.Should().Throw<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData(NivelAcesso.SuperAdmin, false, true)]
    [InlineData(NivelAcesso.Admin, true, true)]      // Dona: mantem o preview que ja usava
    [InlineData(NivelAcesso.Operador, false, false)] // Atendimento/Cozinha
    public void Leitura_ExigeVisualizarRelatorios(NivelAcesso nivel, bool veRelatorios, bool permitido)
    {
        var act = () => ReportAcesso.GarantirLeitura(Usuario(nivel, veRelatorios), Registry().Get(Chave));

        if (permitido) act.Should().NotThrow();
        else act.Should().Throw<UnauthorizedAccessException>();
    }
}
