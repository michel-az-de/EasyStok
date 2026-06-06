using System.Text;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Admin.ExportarTicketsCsv;

namespace EasyStock.Application.Tests.UseCases.Admin;

public class ExportarTicketsCsvUseCaseTests
{
    private readonly IAdminTicketRepository _repo = Substitute.For<IAdminTicketRepository>();
    private ExportarTicketsCsvUseCase Sut() => new(_repo);

    private void SetupRows(params TicketExportRow[] rows) =>
        _repo.ListarParaExportarAsync(Arg.Any<AdminTicketExportFiltro>(), Arg.Any<CancellationToken>())
            .Returns(rows);

    private static string[] Linhas(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3); // pula BOM
        return text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public async Task Exporta_BOM_header_e_uma_linha_por_row()
    {
        SetupRows(new TicketExportRow("T1", "Acme", TicketCategoria.Bug, TicketPrioridade.Alta,
            NivelAtendimento.N2, TicketStatus.Aberto, "Joao",
            new DateTime(2026, 6, 5, 14, 30, 0, DateTimeKind.Utc), null, null, false, false, null, null));
        var bytes = await Sut().ExecuteAsync(new ExportarTicketsCsvCommand());

        bytes.Take(3).Should().Equal((byte)0xEF, (byte)0xBB, (byte)0xBF);
        var linhas = Linhas(bytes);
        linhas.Should().HaveCount(2);
        linhas[0].Should().Be("Titulo;Empresa;Categoria;Prioridade;Nivel;Status;Atendente;CriadoEm;PrazoResposta;PrazoResolucao;SlaRespostaViolado;SlaResolucaoViolado;ResolvidoEm;NotaCsat");
    }

    [Fact]
    public async Task Formata_enums_datas_UTC_bool_e_csat()
    {
        SetupRows(new TicketExportRow("Bug login", "Acme", TicketCategoria.Bug, TicketPrioridade.Critica,
            NivelAtendimento.N3, TicketStatus.Resolvido, "Maria",
            new DateTime(2026, 6, 5, 14, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 5, 16, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 6, 14, 30, 0, DateTimeKind.Utc),
            true, false, new DateTime(2026, 6, 6, 10, 0, 0, DateTimeKind.Utc), 5));
        var bytes = await Sut().ExecuteAsync(new ExportarTicketsCsvCommand());

        Linhas(bytes)[1].Should().Be(
            "Bug login;Acme;Bug;Critica;N3;Resolvido;Maria;2026-06-05 14:30:00;2026-06-05 16:00:00;2026-06-06 14:30:00;Sim;Nao;2026-06-06 10:00:00;5");
    }

    [Fact]
    public async Task Nullable_vira_vazio_sem_atendente_sem_csat_nao_resolvido()
    {
        SetupRows(new TicketExportRow("Sem nada", null, TicketCategoria.Duvida, TicketPrioridade.Normal,
            NivelAtendimento.N1, TicketStatus.Aberto, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, null, false, false, null, null));
        var bytes = await Sut().ExecuteAsync(new ExportarTicketsCsvCommand());

        Linhas(bytes)[1].Should().Be("Sem nada;;Duvida;Normal;N1;Aberto;;2026-01-01 00:00:00;;;Nao;Nao;;");
    }

    [Fact]
    public async Task Encaminha_todos_os_filtros_e_ids_para_a_porta()
    {
        SetupRows();
        var ids = new[] { Guid.NewGuid() };

        await Sut().ExecuteAsync(new ExportarTicketsCsvCommand(
            TicketStatus.Aberto, TicketPrioridade.Alta, NivelAtendimento.N2, TicketCategoria.Bug,
            null, null, "violado", "login", ids));

        await _repo.Received(1).ListarParaExportarAsync(
            Arg.Is<AdminTicketExportFiltro>(f =>
                f.Status == TicketStatus.Aberto && f.Prioridade == TicketPrioridade.Alta &&
                f.Nivel == NivelAtendimento.N2 && f.Categoria == TicketCategoria.Bug &&
                f.SlaStatus == "violado" && f.Search == "login" && f.Ids == ids),
            Arg.Any<CancellationToken>());
    }
}
