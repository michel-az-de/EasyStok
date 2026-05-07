using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Async.Pdf;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Pdf;

/// <summary>
/// Smoke test do <see cref="FaturaPdfRenderer"/> com QuestPDF real (sem mock).
/// Garante que o template compila no pipeline de QuestPDF e que o byte stream
/// resultante e um PDF valido (assinatura "%PDF-").
/// </summary>
public class FaturaPdfRendererSmokeTests
{
    private static Fatura FaturaCompleta()
    {
        var faturado = new DadosFaturado(
            "Cliente Demo Ltda", Documento: "12345678000190",
            Email: "demo@cliente.com", Telefone: "(11) 99999-1234",
            Endereco: new Endereco("Rua das Flores", "100", "Sala 12", "Centro", "Sao Paulo", "SP", "01000-000"));

        var emissor = new DadosEmissor(
            "EasyStock SaaS", Documento: "98765432000100",
            RazaoSocial: "EasyStock Tecnologia LTDA",
            InscricaoMunicipal: "1234567",
            Endereco: new Endereco("Av. Paulista", "1500", null, "Bela Vista", "Sao Paulo", "SP", "01310-100"),
            Email: "billing@easystock.com");

        var f = Fatura.Criar(
            empresaId: Guid.NewGuid(),
            numero: "2026-000099",
            dadosFaturado: faturado,
            dadosEmissor: emissor,
            origem: OrigemFatura.Avulsa,
            dataEmissao: new DateTime(2026, 5, 7, 12, 0, 0, DateTimeKind.Utc),
            dataVencimento: new DateTime(2026, 5, 22, 12, 0, 0, DateTimeKind.Utc),
            observacoes: "Pagamento em ate 15 dias para evitar juros.");

        f.AdicionarItem("Plano Pro mensal", quantidade: 1, precoUnitario: 199.90m, tipo: TipoItemFatura.Recorrencia);
        f.AdicionarItem("Usuario adicional", quantidade: 3, precoUnitario: 19.90m);
        f.AdicionarItem("Cupom NOVO10", quantidade: 1, precoUnitario: 25.99m, tipo: TipoItemFatura.Desconto);
        f.AdicionarItem("Taxa de processamento", quantidade: 1, precoUnitario: 5.00m, tipo: TipoItemFatura.Taxa);
        f.Emitir();

        var pag = FaturaPagamento.CriarConfirmado(
            f.Id, "pix", 100m, "EfiPix", gatewayTransactionId: "TX12345");
        f.RegistrarPagamento(pag);
        return f;
    }

    [Fact]
    public async Task Render_ProduzPdfValidoComAssinaturaCorreta()
    {
        var renderer = new FaturaPdfRenderer();
        var fatura = FaturaCompleta();

        var bytes = await renderer.RenderAsync(fatura);

        bytes.Should().NotBeNullOrEmpty();
        bytes.Length.Should().BeGreaterThan(1000); // PDF realista de fatura tem >1KB
        // Assinatura PDF: %PDF-
        bytes[..5].Should().BeEquivalentTo(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D });
    }

    [Fact]
    public async Task Render_FaturaSemPagamentos_NaoLanca()
    {
        var renderer = new FaturaPdfRenderer();
        var faturado = new DadosFaturado("Min");
        var emissor = new DadosEmissor("Emi");
        var f = Fatura.Criar(Guid.NewGuid(), "2026-000001", faturado, emissor,
            OrigemFatura.Avulsa, DateTime.UtcNow, DateTime.UtcNow.AddDays(7));
        f.AdicionarItem("Item", 1, 10m);
        f.Emitir();

        var bytes = await renderer.RenderAsync(f);

        bytes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Render_RespeitaCancellationToken()
    {
        var renderer = new FaturaPdfRenderer();
        var f = Fatura.Criar(Guid.NewGuid(), "2026-000002",
            new DadosFaturado("X"), new DadosEmissor("Y"),
            OrigemFatura.Avulsa, DateTime.UtcNow, DateTime.UtcNow.AddDays(7));
        f.AdicionarItem("Item", 1, 1m);
        f.Emitir();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => renderer.RenderAsync(f, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
