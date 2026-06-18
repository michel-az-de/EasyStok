using System.Globalization;
using EasyStock.Application.Ports.Output.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// Layout do extrato de fechamento de caixa (issue #642), camada 1: cabeçalho timbrado
/// (logo + empresa/loja), painel de totais e tabela de movimentos do dia. Espelha o estilo
/// premium de <see cref="FaturaTemplate"/>. <see cref="IDocument"/> declarativo e threadsafe.
/// </summary>
internal sealed class FechamentoCaixaExtratoTemplate(FechamentoCaixaExtratoPdfData data) : IDocument
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Extrato de caixa {data.Data:dd/MM/yyyy}",
        Author = data.EmpresaNome,
        Subject = $"Extrato de fechamento de caixa de {data.Data:dd/MM/yyyy}",
        // Datas estáveis (= fechamento) → PDF determinístico para o mesmo snapshot.
        CreationDate = data.FechadoEm ?? data.Data.ToDateTime(TimeOnly.MinValue),
        ModifiedDate = data.FechadoEm ?? data.Data.ToDateTime(TimeOnly.MinValue)
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Grey.Darken4));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(15);
                col.Item().Element(ComposeIdentidade);
                col.Item().AlignRight().Element(ComposeTotais);
                col.Item().Element(ComposeMovimentos);
                if (!string.IsNullOrWhiteSpace(data.Observacoes))
                    col.Item().Element(ComposeObservacoes);
            });
            page.Footer().Element(ComposeFooter);
        });
    }

    // ────────────────────────────────────────────────────────────────────
    private void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            if (data.LogoPng is { Length: > 0 } logo)
                row.ConstantItem(64).PaddingRight(12).AlignMiddle().Image(logo).FitArea();

            row.RelativeItem().Column(col =>
            {
                col.Item().Text("EXTRATO DE CAIXA").FontSize(22).Bold().FontColor(Colors.Indigo.Darken3);
                col.Item().Text(data.Data.ToDateTime(TimeOnly.MinValue)
                    .ToString("dddd, dd 'de' MMMM 'de' yyyy", PtBr)).FontSize(11).SemiBold();
            });
            row.ConstantItem(160).AlignRight().Column(col =>
            {
                col.Item().Text("Data").FontSize(8).FontColor(Colors.Grey.Medium);
                col.Item().Text(data.Data.ToString("dd/MM/yyyy", PtBr)).Bold();
                if (data.FechadoEm is { } fechadoEm)
                {
                    col.Item().PaddingTop(4).Text("Fechado em").FontSize(8).FontColor(Colors.Grey.Medium);
                    col.Item().Text(fechadoEm.ToString("dd/MM/yyyy HH:mm 'UTC'", PtBr)).FontSize(9);
                }
                if (!string.IsNullOrWhiteSpace(data.FechadoPorNome))
                {
                    col.Item().PaddingTop(2).Text("Operador").FontSize(8).FontColor(Colors.Grey.Medium);
                    col.Item().Text(data.FechadoPorNome!).FontSize(9);
                }
            });
        });
    }

    private void ComposeIdentidade(IContainer container)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col =>
        {
            col.Spacing(2);
            col.Item().Text("EMPRESA").FontSize(8).FontColor(Colors.Grey.Medium).Bold();
            col.Item().Text(data.EmpresaNome).Bold();
            if (!string.IsNullOrWhiteSpace(data.EmpresaDocumento))
                col.Item().Text($"CNPJ/CPF: {data.EmpresaDocumento}");
            if (!string.IsNullOrWhiteSpace(data.LojaNome))
            {
                col.Item().PaddingTop(4).Text("LOJA").FontSize(8).FontColor(Colors.Grey.Medium).Bold();
                col.Item().Text(data.LojaNome!);
                if (!string.IsNullOrWhiteSpace(data.LojaEndereco))
                    col.Item().Text(data.LojaEndereco!).FontSize(9).FontColor(Colors.Grey.Darken2);
            }
        });
    }

    private void ComposeTotais(IContainer container)
    {
        container.Width(260).Column(col =>
        {
            col.Spacing(3);
            Linha(col, "Saldo inicial", data.SaldoInicial, Colors.Grey.Darken2);
            Linha(col, "Vendas", data.TotalVendas, Colors.Grey.Darken2);
            Linha(col, "Pagamentos de pedidos", data.TotalPagamentosPedidos, Colors.Grey.Darken2);
            Linha(col, "Entradas extras", data.TotalEntradasExtras, Colors.Green.Darken2);
            Linha(col, "Saídas extras", -data.TotalSaidasExtras, Colors.Red.Darken2);
            col.Item().PaddingTop(4).BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(6).Row(r =>
            {
                r.RelativeItem().Text("SALDO FINAL").FontSize(12).Bold();
                r.RelativeItem().AlignRight().Text(data.SaldoFinal.ToString("C", PtBr)).FontSize(14).Bold();
            });
        });
    }

    private static void Linha(ColumnDescriptor col, string rotulo, decimal valor, string cor) =>
        col.Item().Row(r =>
        {
            r.RelativeItem().Text(rotulo).FontColor(cor);
            r.RelativeItem().AlignRight().Text(valor.ToString("C", PtBr));
        });

    private void ComposeMovimentos(IContainer container)
    {
        container.Column(outer =>
        {
            outer.Item().PaddingBottom(4).Text($"Movimentos do dia ({data.Movimentos.Count})")
                .FontSize(11).Bold().FontColor(Colors.Grey.Darken3);

            if (data.Movimentos.Count == 0)
            {
                outer.Item().Text("Nenhum movimento registrado.").FontSize(9).FontColor(Colors.Grey.Medium);
                return;
            }

            outer.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1);   // hora
                    c.RelativeColumn(2);   // tipo
                    c.RelativeColumn(4);   // descricao
                    c.RelativeColumn(2);   // metodo
                    c.RelativeColumn(2);   // valor
                });

                table.Header(h =>
                {
                    static IContainer S(IContainer c) =>
                        c.DefaultTextStyle(t => t.FontSize(9).Bold().FontColor(Colors.Grey.Medium))
                         .PaddingVertical(6).BorderBottom(1).BorderColor(Colors.Grey.Lighten1);
                    h.Cell().Element(S).Text("HORA");
                    h.Cell().Element(S).Text("TIPO");
                    h.Cell().Element(S).Text("DESCRIÇÃO");
                    h.Cell().Element(S).Text("MÉTODO");
                    h.Cell().Element(S).AlignRight().Text("VALOR");
                });

                foreach (var m in data.Movimentos.OrderBy(m => m.DataMovimento))
                {
                    static IContainer C(IContainer c) =>
                        c.PaddingVertical(5).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3);
                    var corValor = m.Estornado ? Colors.Grey.Medium
                                 : m.SinalNoCaixa < 0 ? Colors.Red.Darken2 : Colors.Grey.Darken4;
                    var sinal = m.SinalNoCaixa < 0 ? "−" : "+";

                    table.Cell().Element(C).Text(m.DataMovimento.ToString("HH:mm", PtBr)).FontSize(9);
                    table.Cell().Element(C).Text(TipoLabel(m.Tipo)).FontSize(9);
                    table.Cell().Element(C).Text(text =>
                    {
                        text.Span(m.Descricao ?? "—").FontSize(9);
                        if (m.Estornado) text.Span("  (estornado)").FontSize(8).FontColor(Colors.Grey.Medium);
                    });
                    table.Cell().Element(C).Text(m.Metodo ?? "—").FontSize(9).FontColor(Colors.Grey.Darken2);
                    table.Cell().Element(C).AlignRight()
                        .Text($"{sinal}{m.Valor.ToString("C", PtBr)}").FontSize(9).FontColor(corValor);
                }
            });
        });
    }

    private static string TipoLabel(string tipo) => tipo switch
    {
        "abertura" => "Abertura",
        "fechamento" => "Fechamento",
        "entrada" => "Entrada",
        "saida" => "Saída",
        _ => tipo
    };

    private void ComposeObservacoes(IContainer container)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col =>
        {
            col.Item().Text("OBSERVAÇÕES").FontSize(8).Bold().FontColor(Colors.Grey.Medium);
            col.Item().Text(data.Observacoes ?? string.Empty).FontSize(10);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.BorderTop(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(text =>
            {
                text.Span("Documento gerado em ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.Span(DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm 'UTC'", PtBr)).FontSize(8);
            });
            row.AutoItem().AlignRight().Text(text =>
            {
                text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                text.Span(" / ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }
}
