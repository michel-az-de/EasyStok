using System.Globalization;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Defaults;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EasyStock.Infra.Async.Pdf;

public sealed class FichaClientePdfRenderer : IFichaClientePdfRenderer
{
    private static readonly CultureInfo PtBr = new("pt-BR");
    static FichaClientePdfRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public Task<byte[]> RenderAsync(DossieClienteDto dossie, byte[]? foto, DateTime consultaEm, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dossie);
        ct.ThrowIfCancellationRequested();
        var pdf = Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(42);
            page.DefaultTextStyle(t => t.FontSize(11).FontColor("#352D25").DisableFontFeature(FontFeatures.StandardLigatures));
            page.Header().PaddingBottom(18).Row(row =>
            {
                if (foto is { Length: > 0 }) row.ConstantItem(64).Height(64).Image(foto).FitArea();
                row.RelativeItem().PaddingLeft(foto is { Length: > 0 } ? 16 : 0).Column(col =>
                {
                    col.Item().Text("Ficha do cliente").FontSize(11).FontColor("#64543D");
                    col.Item().Text(dossie.Cliente.Nome ?? "Cliente").FontSize(24).SemiBold();
                    col.Item().Text($"Cadastro {dossie.Cliente.Id} · Consulta em {Data(consultaEm)}").FontSize(8).FontColor("#64543D");
                });
            });
            page.Content().Column(col =>
            {
                col.Spacing(6);
                Campo(col, "Telefone", dossie.Cliente.Telefone);
                Campo(col, "E-mail", dossie.Cliente.Email);
                Campo(col, "Observações", dossie.Cliente.Observacoes);
                if (dossie.Bloqueado) Campo(col, "Cadastro bloqueado", dossie.Cliente.MotivoBloqueio ?? "Sim");
                if (dossie.Tags.Count > 0) Campo(col, "Tags", string.Join(" · ", dossie.Tags.Select(t => t.Tag)));
                foreach (var endereco in dossie.Enderecos)
                {
                    Campo(col, endereco.Padrao ? "Endereço principal" : "Endereço",
                        string.Join(", ", new[] { endereco.Logradouro, endereco.Numero, endereco.Complemento,
                            endereco.Bairro, endereco.Cidade, endereco.Estado, endereco.Cep, endereco.Pais }.Where(v => !string.IsNullOrWhiteSpace(v))));
                    Campo(col, "Referência", endereco.Referencia);
                }
                Titulo(col, "Compras");
                col.Item().Text($"{dossie.TotalPedidos} pedidos no cadastro · {dossie.TotalGasto.ToString("C", PtBr)} em compras");
                if (dossie.ItemFavorito is { } favorito) Campo(col, "Item mais pedido", favorito.Nome);
                Titulo(col, "Pedidos recentes");
                if (dossie.UltimosPedidos.Count == 0) col.Item().Text("Nenhum pedido registrado.");
                foreach (var pedido in dossie.UltimosPedidos)
                {
                    col.Item().PaddingTop(6).Text($"{Data(pedido.CriadoEm)} · {Estado(pedido.Status)} · {pedido.Total.ToString("C", PtBr)}").SemiBold();
                    col.Item().Text(string.Join(", ", pedido.Itens.Select(i => $"{i.Quantidade.ToString("0.###", PtBr)} × {i.Nome}")));
                }
                if (dossie.Notas.Count > 0)
                {
                    Titulo(col, "Notas internas");
                    foreach (var nota in dossie.Notas)
                    {
                        col.Item().PaddingTop(6).Text($"{nota.Autor} · {Data(nota.CriadoEm)}").FontSize(9).FontColor("#64543D");
                        col.Item().Text(nota.Texto).Italic();
                    }
                }
                if (dossie.ConversasRecentes.Count > 0)
                {
                    Titulo(col, "Conversas recentes");
                    foreach (var conversa in dossie.ConversasRecentes)
                        col.Item().Text($"{Canal(conversa.Canal.ToString())} · {Data(conversa.IniciadaEm)}");
                }
            });
            page.Footer().PaddingTop(16).Row(row =>
            {
                row.RelativeItem().Text("Uso interno · Dados do dossiê nesta consulta").FontSize(8).FontColor("#64543D");
                row.ConstantItem(60).AlignRight().Text(t => { t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); });
            });
        })).GeneratePdf();
        return Task.FromResult(pdf);
    }

    private static void Campo(ColumnDescriptor col, string nome, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return;
        col.Item().Text(t => { t.Span(nome + ": ").SemiBold(); t.Span(valor); });
    }
    private static void Titulo(ColumnDescriptor col, string titulo) => col.Item().PaddingTop(18).PaddingBottom(3).Text(titulo).FontSize(15).SemiBold();
    private static string Data(DateTime valor) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(valor, DateTimeKind.Utc), OperacionalFuso.Zona).ToString("dd/MM/yyyy HH:mm", PtBr);
    private static string Estado(string valor) => valor.ToLowerInvariant() switch
    {
        "aguardando" or "aguardando_pagamento" => "Aguardando pagamento", "preparando" => "Em preparo",
        "pronto" => "Pronto", "entregue" => "Entregue", "cancelado" => "Cancelado", _ => valor
    };
    private static string Canal(string valor) => valor switch { "ChatSite" => "Chat do site", "WhatsApp" => "WhatsApp", "Instagram" => "Instagram", "Email" => "E-mail", _ => valor };
}
