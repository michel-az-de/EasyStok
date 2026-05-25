using System.Globalization;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// Layout do PDF de fatura. <see cref="IDocument"/> do QuestPDF — declarativo,
/// sem estado mutavel, threadsafe.
///
/// <para>
/// Estrutura: cabecalho com numero+status, dados emissor (esquerda) e
/// faturado (direita), tabela de itens, totais, formas de pagamento (se houver),
/// observacoes, rodape com data de emissao + hash curto da fatura.
/// </para>
///
/// <para>
/// Layout pensado para impressao A4 ou export digital. Usa fonte default
/// do QuestPDF (Liberation Sans) — basta para MVP, pode receber fontes
/// embedded em release futura sem mudar o template.
/// </para>
/// </summary>
public sealed class FaturaTemplate(Fatura fatura) : IDocument
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private static readonly Dictionary<string, string> StatusLabel = new()
    {
        [nameof(StatusFatura.Rascunho)] = "Rascunho",
        [nameof(StatusFatura.Emitida)] = "Emitida",
        [nameof(StatusFatura.ParcialmentePaga)] = "Parcialmente paga",
        [nameof(StatusFatura.Paga)] = "Paga",
        [nameof(StatusFatura.Vencida)] = "Vencida",
        [nameof(StatusFatura.Cancelada)] = "Cancelada"
    };

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Fatura {fatura.Numero}",
        Author = fatura.DadosEmissor.Nome,
        Subject = $"Fatura {fatura.Numero} para {fatura.DadosFaturado.Nome}",
        // Determinismo importante para snapshot tests — ContextoFatura define data
        // estavel; PDF byte-identical para mesma fatura.
        CreationDate = fatura.CriadoEm,
        ModifiedDate = fatura.AlteradoEm
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
                col.Item().Element(ComposeParties);
                col.Item().Element(ComposeItens);
                col.Item().AlignRight().Element(ComposeTotais);
                if (fatura.Pagamentos.Count > 0)
                    col.Item().Element(ComposePagamentos);
                if (!string.IsNullOrWhiteSpace(fatura.Observacoes))
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
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("FATURA").FontSize(22).Bold().FontColor(Colors.Indigo.Darken3);
                col.Item().Text(fatura.Numero).FontSize(14).SemiBold();
            });
            row.ConstantItem(160).AlignRight().Column(col =>
            {
                col.Item().Text("Status").FontSize(8).FontColor(Colors.Grey.Medium);
                col.Item().Text(StatusLabel.GetValueOrDefault(fatura.Status.ToString(), fatura.Status.ToString()))
                    .FontSize(11).Bold().FontColor(StatusColor(fatura.Status));
                col.Item().PaddingTop(4).Text("Emissao").FontSize(8).FontColor(Colors.Grey.Medium);
                col.Item().Text(fatura.DataEmissao.ToString("dd/MM/yyyy", PtBr));
                col.Item().PaddingTop(2).Text("Vencimento").FontSize(8).FontColor(Colors.Grey.Medium);
                col.Item().Text(fatura.DataVencimento.ToString("dd/MM/yyyy", PtBr));
            });
        });
    }

    private static string StatusColor(StatusFatura status) => status switch
    {
        StatusFatura.Paga => Colors.Green.Darken2,
        StatusFatura.Vencida => Colors.Red.Darken2,
        StatusFatura.Cancelada => Colors.Grey.Darken2,
        StatusFatura.ParcialmentePaga => Colors.Orange.Darken2,
        _ => Colors.Indigo.Darken2
    };

    private void ComposeParties(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().PaddingRight(15).Element(c => ComposeBoxEmissor(c, fatura.DadosEmissor));
            row.RelativeItem().PaddingLeft(15).Element(c => ComposeBoxFaturado(c, fatura.DadosFaturado));
        });
    }

    private static void ComposeBoxEmissor(IContainer container, DadosEmissor emissor)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col =>
        {
            col.Spacing(2);
            col.Item().Text("EMISSOR").FontSize(8).FontColor(Colors.Grey.Medium).Bold();
            col.Item().Text(emissor.RazaoSocial ?? emissor.Nome).Bold();
            if (!string.IsNullOrWhiteSpace(emissor.Documento))
                col.Item().Text($"CNPJ/CPF: {emissor.Documento}");
            if (!string.IsNullOrWhiteSpace(emissor.InscricaoEstadual))
                col.Item().Text($"IE: {emissor.InscricaoEstadual}");
            if (!string.IsNullOrWhiteSpace(emissor.InscricaoMunicipal))
                col.Item().Text($"IM: {emissor.InscricaoMunicipal}");
            ComposeEnderecoLinhas(col, emissor.Endereco);
            if (!string.IsNullOrWhiteSpace(emissor.Email))
                col.Item().Text(emissor.Email).FontSize(9).FontColor(Colors.Grey.Darken2);
            if (!string.IsNullOrWhiteSpace(emissor.Telefone))
                col.Item().Text(emissor.Telefone).FontSize(9).FontColor(Colors.Grey.Darken2);
        });
    }

    private static void ComposeBoxFaturado(IContainer container, DadosFaturado faturado)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col =>
        {
            col.Spacing(2);
            col.Item().Text("FATURADO").FontSize(8).FontColor(Colors.Grey.Medium).Bold();
            col.Item().Text(faturado.Nome).Bold();
            if (!string.IsNullOrWhiteSpace(faturado.Documento))
                col.Item().Text($"CNPJ/CPF: {faturado.Documento}");
            ComposeEnderecoLinhas(col, faturado.Endereco);
            if (!string.IsNullOrWhiteSpace(faturado.Email))
                col.Item().Text(faturado.Email).FontSize(9).FontColor(Colors.Grey.Darken2);
            if (!string.IsNullOrWhiteSpace(faturado.Telefone))
                col.Item().Text(faturado.Telefone).FontSize(9).FontColor(Colors.Grey.Darken2);
        });
    }

    private static void ComposeEnderecoLinhas(QuestPDF.Fluent.ColumnDescriptor col, Endereco? endereco)
    {
        if (endereco is null) return;
        var linha1 = string.Join(", ", new[] { endereco.Logradouro, endereco.Numero, endereco.Complemento }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(linha1))
            col.Item().Text(linha1).FontSize(9);
        var linha2 = string.Join(" - ", new[] { endereco.Bairro, endereco.Cidade, endereco.Uf }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(linha2))
            col.Item().Text(linha2).FontSize(9);
        if (!string.IsNullOrWhiteSpace(endereco.Cep))
            col.Item().Text($"CEP: {endereco.Cep}").FontSize(9);
    }

    private void ComposeItens(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(5);   // descricao
                c.RelativeColumn(1);   // qtd
                c.RelativeColumn(2);   // unit
                c.RelativeColumn(2);   // subtotal
            });

            table.Header(h =>
            {
                static IContainer S(IContainer c) =>
                    c.DefaultTextStyle(t => t.FontSize(9).Bold().FontColor(Colors.Grey.Medium))
                     .PaddingVertical(6).BorderBottom(1).BorderColor(Colors.Grey.Lighten1);
                h.Cell().Element(S).Text("DESCRICAO");
                h.Cell().Element(S).AlignRight().Text("QTD");
                h.Cell().Element(S).AlignRight().Text("UNIT");
                h.Cell().Element(S).AlignRight().Text("SUBTOTAL");
            });

            foreach (var item in fatura.Itens.OrderBy(i => i.Ordem))
            {
                static IContainer C(IContainer c) =>
                    c.PaddingVertical(5).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3);
                table.Cell().Element(C).Text(text =>
                {
                    text.Span(item.Descricao);
                    if (item.Tipo == TipoItemFatura.Desconto)
                        text.Span("  (desconto)").FontColor(Colors.Green.Darken1).FontSize(8);
                    else if (item.Tipo == TipoItemFatura.Taxa)
                        text.Span("  (taxa)").FontColor(Colors.Orange.Darken1).FontSize(8);
                });
                table.Cell().Element(C).AlignRight().Text(item.Quantidade.ToString("0.###", PtBr));
                table.Cell().Element(C).AlignRight().Text(item.PrecoUnitario.ToString("C", PtBr));
                table.Cell().Element(C).AlignRight().Text(item.Subtotal.ToString("C", PtBr));
            }
        });
    }

    private void ComposeTotais(IContainer container)
    {
        container.Width(260).Column(col =>
        {
            col.Spacing(3);
            col.Item().Row(r =>
            {
                r.RelativeItem().Text("Subtotal").FontColor(Colors.Grey.Darken2);
                r.RelativeItem().AlignRight().Text(fatura.SubTotal.ToString("C", PtBr));
            });
            if (fatura.Descontos > 0)
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text("Descontos").FontColor(Colors.Green.Darken2);
                    r.RelativeItem().AlignRight().Text($"-{fatura.Descontos.ToString("C", PtBr)}");
                });
            if (fatura.Acrescimos > 0)
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text("Acrescimos").FontColor(Colors.Orange.Darken2);
                    r.RelativeItem().AlignRight().Text(fatura.Acrescimos.ToString("C", PtBr));
                });
            col.Item().PaddingTop(4).BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(6).Row(r =>
            {
                r.RelativeItem().Text("TOTAL").FontSize(12).Bold();
                r.RelativeItem().AlignRight().Text(fatura.Total.ToString("C", PtBr)).FontSize(14).Bold();
            });
            if (fatura.Pagamentos.Count > 0 && fatura.TotalPago > 0 && fatura.TotalPago < fatura.Total)
            {
                col.Item().PaddingTop(2).Row(r =>
                {
                    r.RelativeItem().Text("Pago").FontColor(Colors.Green.Darken2);
                    r.RelativeItem().AlignRight().Text(fatura.TotalPago.ToString("C", PtBr));
                });
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text("Saldo").Bold();
                    r.RelativeItem().AlignRight().Text(fatura.Pendente.ToString("C", PtBr)).Bold();
                });
            }
        });
    }

    private void ComposePagamentos(IContainer container)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Column(col =>
        {
            col.Spacing(3);
            col.Item().Text("FORMAS DE PAGAMENTO").FontSize(8).Bold().FontColor(Colors.Grey.Medium);
            foreach (var pag in fatura.Pagamentos.OrderByDescending(p => p.PagoEm ?? p.CriadoEm))
            {
                col.Item().Row(r =>
                {
                    r.RelativeItem(2).Text(text =>
                    {
                        text.Span(pag.Metodo).Bold();
                        text.Span($"  via {pag.GatewayProvedor}").FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                    r.RelativeItem().AlignCenter().Text(pag.Status.ToString())
                        .FontSize(9).FontColor(StatusPagamentoColor(pag.Status));
                    r.RelativeItem().AlignRight().Text(pag.Valor.ToString("C", PtBr)).Bold();
                });
                if (!string.IsNullOrWhiteSpace(pag.Observacao))
                    col.Item().Text(pag.Observacao).FontSize(8).FontColor(Colors.Grey.Darken2);
            }
        });
    }

    private static string StatusPagamentoColor(StatusFaturaPagamento status) => status switch
    {
        StatusFaturaPagamento.Confirmado => Colors.Green.Darken2,
        StatusFaturaPagamento.Falhou => Colors.Red.Darken2,
        StatusFaturaPagamento.Estornado => Colors.Grey.Darken2,
        StatusFaturaPagamento.EstornoSolicitado => Colors.Orange.Darken2,
        _ => Colors.Indigo.Darken2
    };

    private void ComposeObservacoes(IContainer container)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col =>
        {
            col.Item().Text("OBSERVACOES").FontSize(8).Bold().FontColor(Colors.Grey.Medium);
            col.Item().Text(fatura.Observacoes ?? string.Empty).FontSize(10);
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
                text.Span($"  ·  Ref ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.Span(fatura.Id.ToString("N").Substring(0, 12)).FontSize(8).FontFamily(Fonts.CourierNew);
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
