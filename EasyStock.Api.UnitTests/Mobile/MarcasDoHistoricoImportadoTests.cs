using System.Reflection;
using EasyStock.Api.Mobile.Services;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #2813: o entregue do histórico entra quitado pela marca <c>Pedido.QuitadoHistoricoEm</c>, e o lançamento manual fica
/// só como arquivo pela marca <c>CashEntry.NaoPromoverEm</c>. Só o import grava as marcas: nenhum DTO, request, comando
/// ou mutação de sync as carrega, e só o <c>LegacySnapshotImportService</c> chama o método que marca o pedido.
/// </summary>
public sealed class MarcasDoHistoricoImportadoTests
{
    [Fact]
    public async Task AReceber_do_entregue_quitado_no_historico_e_zero_e_o_comum_segue_o_total()
    {
        var quitado = EntregueSemPagamento();
        quitado.MarcarQuitadoHistorico(DateTime.UtcNow);
        var comum = EntregueSemPagamento();

        (await AReceberDoPedidoMobile.CalcularAsync(quitado, null, CancellationToken.None)).Should().Be(0m);
        (await AReceberDoPedidoMobile.CalcularAsync(comum, null, CancellationToken.None))
            .Should().Be(45.90m, "sem a marca o entregue sem pagamento segue a receber");
    }

    [Fact]
    public void Marcas_so_existem_nos_dois_agregados_e_em_nenhum_dto_request_ou_comando()
    {
        var assemblies = new[]
        {
            typeof(Pedido).Assembly,
            typeof(EasyStock.Application.UseCases.RegistrarPagamentoPedido.RegistrarPagamentoPedidoCommand).Assembly,
            typeof(LegacySnapshotImportService).Assembly,
        };
        const BindingFlags todos = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var donos = assemblies.SelectMany(a => a.GetTypes())
            .Where(t => t.GetProperties(todos).Any(p => EhMarca(p.Name))
                || t.GetConstructors(todos).Any(c => c.GetParameters().Any(x => EhMarca(x.Name!))))
            .Select(t => t.Name).Distinct().Order().ToList();

        donos.Should().BeEquivalentTo(new[] { nameof(CashEntry), nameof(Pedido) });
    }

    [Fact]
    public void So_o_import_do_historico_chama_MarcarQuitadoHistorico() =>
        ArquivosQueChamam(".MarcarQuitadoHistorico(").Should().Equal("LegacySnapshotImportService.cs");

    [Fact]
    public void So_o_import_do_historico_chama_MarcarNaoPromover() =>
        ArquivosQueChamam(".MarcarNaoPromover(").Should().Equal("LegacySnapshotImportService.cs");

    [Fact]
    public void MarcarNaoPromover_guarda_a_data_original_e_recusa_o_lancamento_ja_promovido()
    {
        var original = new DateTime(2026, 4, 12, 15, 30, 0, DateTimeKind.Utc);
        var arquivo = new CashEntry { Id = "cash-arquivo", Type = "income", Amount = 10m, Description = "Entrada sintetica", CreatedAt = original };
        var promovido = new CashEntry { Id = "cash-promovido", Type = "income", Amount = 10m, Description = "Entrada sintetica",
            CreatedAt = original, ErpMovimentoCaixaId = Guid.NewGuid() };

        arquivo.MarcarNaoPromover(original);
        var marcarPromovido = () => promovido.MarcarNaoPromover(original);

        arquivo.NaoPromoverEm.Should().Be(original, "a marca guarda a data original, não a de hoje");
        marcarPromovido.Should().Throw<InvalidOperationException>("a marca não esconde um movimento que já existe no caixa");
        promovido.NaoPromoverEm.Should().BeNull();
    }

    private static List<string> ArquivosQueChamam(string chamada) =>
        Directory.EnumerateDirectories(RaizDoRepositorio(), "EasyStock.*")
            .Where(d => !Path.GetFileName(d).Contains("Tests", StringComparison.Ordinal))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains(chamada, StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f)!)
            .ToList();

    private static bool EhMarca(string nome) =>
        nome.StartsWith("QuitadoHistorico", StringComparison.OrdinalIgnoreCase)
        || nome.StartsWith("NaoPromover", StringComparison.OrdinalIgnoreCase);

    private static Pedido EntregueSemPagamento()
    {
        var pedido = Pedido.Criar(Guid.NewGuid());
        pedido.Status = "entregue";
        pedido.Total = Dinheiro.FromDecimal(45.90m);
        return pedido;
    }

    private static string RaizDoRepositorio()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "EasyStock.Domain"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada a partir de " + AppContext.BaseDirectory);
    }
}
