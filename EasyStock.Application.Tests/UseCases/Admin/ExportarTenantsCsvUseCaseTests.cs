using System.Text;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Admin.ExportarTenantsCsv;

namespace EasyStock.Application.Tests.UseCases.Admin;

public class ExportarTenantsCsvUseCaseTests
{
    private readonly IAdminTenantsQueries _queries = Substitute.For<IAdminTenantsQueries>();
    private ExportarTenantsCsvUseCase Sut() => new(_queries);

    private void SetupRows(params TenantExportRow[] rows) =>
        _queries.ListarParaExportarAsync(Arg.Any<TenantExportFiltro>(), Arg.Any<CancellationToken>())
            .Returns(rows);

    private static TenantExportRow Row(
        string nome = "Acme", string? doc = "123", string? plano = "Pro",
        decimal? preco = 49.9m, StatusAssinatura? status = StatusAssinatura.Ativa,
        int usuarios = 3, int lojas = 1, DateTime? criado = null, DateTime? renov = null)
        => new(nome, doc, plano, preco, status, usuarios, lojas,
            criado ?? new DateTime(2026, 6, 5, 14, 30, 0, DateTimeKind.Utc),
            renov ?? new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

    private static string[] Linhas(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3); // pula BOM
        return text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public async Task Exporta_com_BOM_header_e_uma_linha_por_row()
    {
        SetupRows(Row(nome: "A"), Row(nome: "B"));
        var bytes = await Sut().ExecuteAsync(new ExportarTenantsCsvCommand());

        bytes.Take(3).Should().Equal((byte)0xEF, (byte)0xBB, (byte)0xBF);
        var linhas = Linhas(bytes);
        linhas.Should().HaveCount(3); // header + 2 linhas
        linhas[0].Should().Be("Nome;Documento;Plano;PrecoMensal;Status;Usuarios;Lojas;CriadoEm;DataRenovacao");
    }

    [Fact]
    public async Task Formata_data_UTC_ISO_preco_ptBR_e_status_label()
    {
        SetupRows(Row(nome: "Acme", doc: "999", plano: "Pro", preco: 49.9m,
            status: StatusAssinatura.Suspensa, usuarios: 7, lojas: 2));
        var bytes = await Sut().ExecuteAsync(new ExportarTenantsCsvCommand());

        Linhas(bytes)[1].Should().Be("Acme;999;Pro;49,90;Suspensa;7;2;2026-06-05 14:30:00;2026-07-01");
    }

    [Fact]
    public async Task Campos_nulos_viram_vazio_nunca_a_palavra_null()
    {
        SetupRows(new TenantExportRow("Acme", null, null, null, null, 0, 0,
            new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), null));
        var bytes = await Sut().ExecuteAsync(new ExportarTenantsCsvCommand());

        Linhas(bytes)[1].Should().Be("Acme;;;;;0;0;2026-06-05 00:00:00;");
    }

    [Fact]
    public async Task Escapa_nome_com_separador()
    {
        SetupRows(new TenantExportRow("Bar; Grill", "1", "Pro", 10m, StatusAssinatura.Ativa, 1, 1,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null));
        var bytes = await Sut().ExecuteAsync(new ExportarTenantsCsvCommand());

        Linhas(bytes)[1].Should().StartWith("\"Bar; Grill\";");
    }

    [Fact]
    public async Task Preserva_a_ordem_devolvida_pela_porta()
    {
        SetupRows(Row(nome: "Zeta"), Row(nome: "Alfa"), Row(nome: "Meio"));
        var bytes = await Sut().ExecuteAsync(new ExportarTenantsCsvCommand());
        var linhas = Linhas(bytes);

        linhas[1].Should().StartWith("Zeta;");
        linhas[2].Should().StartWith("Alfa;");
        linhas[3].Should().StartWith("Meio;");
    }

    [Fact]
    public async Task Encaminha_search_status_e_ids_para_a_porta()
    {
        SetupRows(Row());
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await Sut().ExecuteAsync(new ExportarTenantsCsvCommand("busca", StatusAssinatura.Suspensa, ids));

        await _queries.Received(1).ListarParaExportarAsync(
            Arg.Is<TenantExportFiltro>(f =>
                f.Search == "busca" && f.Status == StatusAssinatura.Suspensa && f.Ids == ids),
            Arg.Any<CancellationToken>());
    }
}
