using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Reporting;
using EasyStock.Application.UseCases.Reports;
using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Tests.UseCases.Reports;

/// <summary>
/// #2668: o download anônimo só serve o artefato quando a run bate com tudo o que o token assinado diz, e a empresa do
/// token vira o tenant da requisição. Qualquer divergência dá o mesmo null (404 no controller).
/// </summary>
public sealed class BaixarArtefatoRelatorioUseCaseTests
{
    private const string Chave = "tenants/e/reports/2026/11/run.csv";
    private readonly IRelatorioDownloadLink _link = Substitute.For<IRelatorioDownloadLink>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly IReportRunRepository _repo = Substitute.For<IReportRunRepository>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly Guid _empresa = Guid.NewGuid();

    private BaixarArtefatoRelatorioUseCase UseCase() => new(_link, _tenant, _repo, _storage);

    private ReportRun Run(Guid? empresa = null, bool concluida = true, ReportFormat formato = ReportFormat.Csv)
    {
        var run = ReportRun.Enqueue(empresa ?? _empresa, Guid.NewGuid(), "fiscal.livro/saidas", ReportCategoria.Fiscal,
            ReportContexto.Tenant, "{}", "hash", formato, "1.0.0", TimeSpan.FromDays(7));
        if (!concluida) return run;
        run.TryStart(TimeSpan.FromMinutes(5));
        run.MarkSucceeded(Chave, 10, "sha", 1);
        return run;
    }

    private void Autoriza(ReportRun run, Guid? empresa = null, string chave = Chave)
    {
        _link.Validar("tk").Returns(new RelatorioDownloadAutorizado(empresa ?? _empresa, run.Id, chave));
        _repo.GetByIdAsync(run.Id, Arg.Any<CancellationToken>()).Returns(run);
    }

    [Fact]
    public async Task Token_valido_serve_o_artefato_com_tipo_e_nome_do_que_foi_gravado()
    {
        var run = Run(formato: ReportFormat.Xlsx);
        Autoriza(run);
        var conteudo = new MemoryStream([1, 2, 3]);
        _storage.DownloadStreamAsync(Chave, Arg.Any<CancellationToken>()).Returns(conteudo);

        var artefato = await UseCase().ExecuteAsync("tk", default);

        artefato.Should().NotBeNull();
        artefato!.Conteudo.Should().BeSameAs(conteudo);
        artefato.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        artefato.NomeArquivo.Should().Be($"fiscal-livro-saidas_{run.Id:N}.xlsx");
        _tenant.Received(1).SetCurrentTenant(_empresa);
    }

    [Fact]
    public async Task Token_invalido_nao_toca_banco_nem_storage()
    {
        (await UseCase().ExecuteAsync("adulterado", default)).Should().BeNull();

        await _repo.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        _tenant.DidNotReceiveWithAnyArgs().SetCurrentTenant(default);
    }

    [Fact]
    public async Task Run_de_outra_empresa_nao_sai()
    {
        var run = Run(empresa: Guid.NewGuid());
        Autoriza(run);

        (await UseCase().ExecuteAsync("tk", default)).Should().BeNull();
        await _storage.DidNotReceiveWithAnyArgs().DownloadStreamAsync(default!, default);
    }

    [Fact]
    public async Task Chave_diferente_da_gravada_nao_sai()
    {
        var run = Run();
        Autoriza(run, chave: "tenants/e/reports/2026/11/outra.csv");

        (await UseCase().ExecuteAsync("tk", default)).Should().BeNull();
        await _storage.DidNotReceiveWithAnyArgs().DownloadStreamAsync(default!, default);
    }

    [Fact]
    public async Task Run_sem_artefato_concluido_nao_sai()
    {
        var run = Run(concluida: false);
        Autoriza(run);

        (await UseCase().ExecuteAsync("tk", default)).Should().BeNull();
    }

    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(DirectoryNotFoundException))]
    public async Task Chave_fora_da_raiz_ou_artefato_podado_vira_null(Type erro)
    {
        var run = Run();
        Autoriza(run);
        _storage.DownloadStreamAsync(Chave, Arg.Any<CancellationToken>())
            .Returns<Stream>(_ => throw (Exception)Activator.CreateInstance(erro)!);

        (await UseCase().ExecuteAsync("tk", default)).Should().BeNull();
    }

    [Fact]
    public void Nome_do_arquivo_so_tem_caracteres_seguros_para_o_cabecalho()
    {
        var run = ReportRun.Enqueue(_empresa, Guid.NewGuid(), "a.b c\"d\r\n/e", ReportCategoria.Vendas,
            ReportContexto.Tenant, "{}", "hash", ReportFormat.Pdf, "1.0.0", TimeSpan.FromDays(7));

        ReportArtefato.NomeArquivo(run).Should().MatchRegex("^[A-Za-z0-9_-]+\\.pdf$");
    }
}
