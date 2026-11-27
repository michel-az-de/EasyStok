using System.Globalization;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Defaults;
using EasyStock.Domain.Sales;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// Ficha do cliente em A4 (#3387). Legível em preto e branco: a cor nunca é o único sinal, quem separa é
/// borda, rótulo em texto e peso da fonte. Paleta dos tokens do app.
/// </summary>
public sealed class FichaClientePdfRenderer : IFichaClientePdfRenderer
{
    private const string Tinta = "#352D25";
    private const string Suave = "#64543D";
    private const string Marca = "#A25803";
    private const string MarcaFundo = "#FFF0D8";
    private const string Linha = "#E6E1D8";
    private const string Zebra = "#F7F6F2";
    private const string AvisoTexto = "#7A5A00";
    private const string AvisoFundo = "#FBF0CC";
    private const string Erro = "#A8201A";
    private const string NaoInformado = "não informado";

    private static readonly CultureInfo PtBr = new("pt-BR");
    static FichaClientePdfRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public Task<byte[]> RenderAsync(DossieClienteDto dossie, byte[]? foto, DateTime consultaEm, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dossie);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Montar(dossie, foto, consultaEm).GeneratePdf());
    }

    /// <summary>Exposto ao projeto de teste para gerar imagens das páginas na conferência visual.</summary>
    internal static Document Montar(DossieClienteDto dossie, byte[]? foto, DateTime consultaEm)
    {
        var nome = string.IsNullOrWhiteSpace(dossie.Cliente.Nome) ? "Cliente" : dossie.Cliente.Nome.Trim();
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Tinta).DisableFontFeature(FontFeatures.StandardLigatures));
            page.Header().Column(col =>
            {
                col.Item().ShowOnce().Element(c => Cabecalho(c, dossie, nome, foto, consultaEm));
                col.Item().SkipOnce().PaddingBottom(8).Text($"Ficha de {nome} · continuação").FontSize(9).FontColor(Suave);
            });
            page.Content().Column(col => Corpo(col, dossie, consultaEm));
            page.Footer().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Text($"Uso interno · {nome}").FontSize(8.5f).FontColor(Suave);
                row.ConstantItem(60).AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(8.5f).FontColor(Suave));
                    t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
                });
            });
        }));
    }

    private static void Cabecalho(IContainer c, DossieClienteDto dossie, string nome, byte[]? foto, DateTime consultaEm)
    {
        c.PaddingBottom(14).Column(col =>
        {
            col.Item().Height(3).Background(Marca);
            col.Item().PaddingTop(14).Row(row =>
            {
                row.RelativeItem().Column(info =>
                {
                    info.Item().Text(nome).FontSize(22).SemiBold();
                    info.Item().PaddingTop(2).Text($"{Ou(TelefoneNoPapel(dossie.Cliente.Telefone))} · {Ou(dossie.Cliente.Email)}").FontSize(10);
                    var desde = dossie.PrimeiraCompraEm is { } primeira ? $"Cliente desde {NaFuso(primeira).ToString("MMMM 'de' yyyy", PtBr)} · " : "";
                    info.Item().PaddingTop(2).Text($"{desde}Emitida em {Data(consultaEm)}").FontSize(9).FontColor(Suave);
                });
                if (foto is { Length: > 0 })
                    row.ConstantItem(56).Height(56).Image(foto).FitArea();
                else
                    row.ConstantItem(56).Height(56).Background(MarcaFundo).AlignCenter().AlignMiddle()
                        .Text(Iniciais(nome)).FontSize(20).SemiBold().FontColor(Marca);
            });
        });
    }

    private static void Corpo(ColumnDescriptor col, DossieClienteDto dossie, DateTime consultaEm)
    {
        col.Spacing(0);
        Alerta(col, dossie);
        Indicadores(col, dossie, consultaEm);
        col.Item().PaddingTop(2).Row(row =>
        {
            row.RelativeItem(55).PaddingRight(9).Column(c => Contato(c, dossie));
            row.RelativeItem(45).PaddingLeft(9).Column(c => Notas(c, dossie));
        });
        Pedidos(col, dossie);
        Conversas(col, dossie);
    }

    /// <summary>Só aparece com cadastro bloqueado. O rótulo "Atenção" em negrito sobrevive à impressão em P&amp;B.</summary>
    private static void Alerta(ColumnDescriptor col, DossieClienteDto dossie)
    {
        if (!dossie.Bloqueado) return;
        // #3387: não há lista canônica de restrições alimentares (tags são livres); só o bloqueio vai na caixa, as tags ficam no contato.
        var motivo = string.IsNullOrWhiteSpace(dossie.Cliente.MotivoBloqueio) ? "sem motivo informado" : dossie.Cliente.MotivoBloqueio;
        col.Item().PaddingBottom(12).ShowEntire().Border(1).BorderColor(Erro).Background(AvisoFundo).Padding(8).Text(t =>
        {
            t.Span("Atenção  ").Bold().FontColor(Erro);
            t.Span($"Cadastro bloqueado: {motivo}").SemiBold().FontColor(AvisoTexto);
        });
    }

    private static void Indicadores(ColumnDescriptor col, DossieClienteDto dossie, DateTime consultaEm)
    {
        var ticket = dossie.TotalPedidos > 0 ? Moeda(dossie.TotalGasto / dossie.TotalPedidos) : "—";
        var (ultima, ultimaData) = UltimaCompra(dossie.UltimaCompraEm, consultaEm);
        col.Item().ShowEntire().Row(row =>
        {
            row.Spacing(8);
            Kpi(row, "Compras", dossie.TotalPedidos.ToString(PtBr), null);
            Kpi(row, "Total comprado", Moeda(dossie.TotalGasto), null);
            Kpi(row, "Ticket médio", ticket, null);
            Kpi(row, "Última compra", ultima, ultimaData);
        });

        var fatos = new List<string>();
        if (dossie.Ritmo is { } ritmo)
            fatos.Add($"{(ritmo.MedianaDias == 1 ? "Pede todo dia" : $"Pede a cada ~{ritmo.MedianaDias} dias")} (mediana dos últimos {ritmo.ComprasAnalisadas} dias com compra)");
        if (dossie.ItemFavorito is { } fav)
            fatos.Add($"Mais pedido: {fav.Nome} ({fav.Pedidos} {(fav.Pedidos == 1 ? "pedido" : "pedidos")})");
        if (dossie.Preferencias is { } pref)
            fatos.Add($"Aceita receber ofertas: {(pref.ConsentiuMarketing ? "Sim" : "Não")}");
        if (fatos.Count > 0)
            col.Item().PaddingTop(8).Text(string.Join(" · ", fatos)).FontSize(10);
    }

    private static void Kpi(RowDescriptor row, string rotulo, string valor, string? apoio) =>
        row.RelativeItem().Border(1).BorderColor(Linha).Padding(10).Column(c =>
        {
            c.Item().Text(rotulo).FontSize(8.5f).FontColor(Suave);
            c.Item().PaddingTop(2).Text(valor).FontSize(18).SemiBold();
            if (apoio is not null) c.Item().Text(apoio).FontSize(8.5f).FontColor(Suave);
        });

    private static void Contato(ColumnDescriptor col, DossieClienteDto dossie)
    {
        col.Item().Element(c => Titulo(c, "Contato e entrega"));
        foreach (var e in dossie.Enderecos.OrderByDescending(e => e.Padrao))
        {
            var linha = string.Join(", ", new[] { e.Logradouro, e.Numero, e.Complemento, e.Bairro, e.Cidade, e.Estado, e.Cep, e.Pais }
                .Where(v => !string.IsNullOrWhiteSpace(v)));
            col.Item().PaddingBottom(6).ShowEntire().Column(c =>
            {
                c.Item().Text(t => { t.Span(e.Padrao ? "Endereço principal: " : "Endereço: ").SemiBold(); t.Span(linha); });
                if (!string.IsNullOrWhiteSpace(e.Referencia)) c.Item().Text($"Referência: {e.Referencia}").FontSize(9).FontColor(Suave);
            });
        }
        if (dossie.Enderecos.Count == 0) col.Item().PaddingBottom(6).Text("Nenhum endereço cadastrado.").FontColor(Suave);
        Campo(col, "Observações", dossie.Cliente.Observacoes);
        if (dossie.Tags.Count > 0) Campo(col, "Tags", string.Join(" · ", dossie.Tags.Select(t => t.Tag.Replace('_', ' '))));
    }

    private static void Notas(ColumnDescriptor col, DossieClienteDto dossie)
    {
        col.Item().Element(c => Titulo(c, "Notas internas"));
        if (dossie.Notas.Count == 0) col.Item().Text("Nenhuma nota interna.").FontColor(Suave);
        foreach (var nota in dossie.Notas)
        {
            // #3387: nota curta nunca racha entre páginas; a muito longa não cabe numa página e precisa poder quebrar.
            var caixa = col.Item().PaddingBottom(6);
            if (nota.Texto.Length <= 700) caixa = caixa.ShowEntire();
            caixa.Border(1).BorderColor(Linha).Background(Zebra).Padding(8).Column(c =>
            {
                c.Item().Text($"{nota.Autor} · {Data(nota.CriadoEm)}").FontSize(8.5f).FontColor(Suave);
                c.Item().PaddingTop(2).Text(nota.Texto).FontSize(9.5f);
            });
        }
    }

    private static void Pedidos(ColumnDescriptor col, DossieClienteDto dossie)
    {
        col.Item().EnsureSpace(150).Element(c => Titulo(c, "Pedidos recentes"));
        if (dossie.UltimosPedidos.Count == 0)
        {
            col.Item().Text("Nenhum pedido registrado.").FontColor(Suave);
            return;
        }
        var n = dossie.UltimosPedidos.Count;
        var legenda = dossie.TotalPedidos <= n
            ? $"{n} {(n == 1 ? "pedido" : "pedidos")}"
            : $"Os {n} pedidos mais recentes · {dossie.TotalPedidos} compras no total";
        col.Item().PaddingBottom(4).Text(legenda).FontSize(9).FontColor(Suave);
        col.Item().Table(tabela =>
        {
            tabela.ColumnsDefinition(c =>
            {
                c.ConstantColumn(98); c.ConstantColumn(118); c.RelativeColumn(); c.ConstantColumn(70);
            });
            tabela.Header(h =>
            {
                CelulaCabecalho(h.Cell(), "Data", false);
                CelulaCabecalho(h.Cell(), "Situação", false);
                CelulaCabecalho(h.Cell(), "Itens", false);
                CelulaCabecalho(h.Cell(), "Total", true);
            });
            var i = 0;
            foreach (var p in dossie.UltimosPedidos)
            {
                var cancelado = StatusPedidoMapper.TryParse(p.Status, out var s) && s == StatusPedido.Cancelado;
                var cor = cancelado ? Suave : Tinta;
                var fundo = i++ % 2 == 1 ? Zebra : "#FFFFFF";
                var itens = string.Join(", ", p.Itens.Select(it => $"{it.Quantidade.ToString("0.###", PtBr)} × {it.Nome}"));
                Celula(tabela.Cell(), Data(p.CriadoEm), cor, fundo, false);
                Celula(tabela.Cell(), Estado(p.Status), cor, fundo, false, cancelado);
                Celula(tabela.Cell(), itens.Length == 0 ? "—" : itens, cor, fundo, false);
                Celula(tabela.Cell(), Moeda(p.Total), cor, fundo, true);
            }
        });
    }

    private static void CelulaCabecalho(IContainer c, string texto, bool direita)
    {
        var x = c.BorderBottom(1).BorderColor(Tinta).PaddingVertical(4).PaddingHorizontal(4);
        (direita ? x.AlignRight() : x).Text(texto).FontSize(9).SemiBold();
    }

    private static void Celula(IContainer c, string texto, string cor, string fundo, bool direita, bool negrito = false)
    {
        var x = c.Background(fundo).PaddingVertical(4).PaddingHorizontal(4);
        var t = (direita ? x.AlignRight() : x).Text(texto).FontSize(9.5f).FontColor(cor);
        if (negrito) t.SemiBold();
    }

    private static void Conversas(ColumnDescriptor col, DossieClienteDto dossie)
    {
        if (dossie.ConversasRecentes.Count == 0) return;
        col.Item().EnsureSpace(70).Element(c => Titulo(c, "Conversas recentes"));
        foreach (var conversa in dossie.ConversasRecentes)
            col.Item().PaddingBottom(2).Text($"{Canal(conversa.Canal.ToString())} · {Data(conversa.IniciadaEm)}").FontSize(9.5f);
    }

    private static void Titulo(IContainer c, string titulo) =>
        c.PaddingTop(14).PaddingBottom(6).Text(titulo).FontSize(12).SemiBold();

    private static void Campo(ColumnDescriptor col, string nome, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return;
        col.Item().PaddingBottom(6).Text(t => { t.Span(nome + ": ").SemiBold(); t.Span(valor); });
    }

    /// <summary>Mesma máscara da tela (mascaraTelefone do Console): <c>+5511999999999</c> vira <c>(11) 99999-9999</c>.
    /// Número de fora do Brasil ou fora do tamanho sai como foi gravado.</summary>
    private static string? TelefoneNoPapel(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone)) return telefone;
        var bruto = telefone.Trim();
        var digitos = new string(bruto.Where(char.IsAsciiDigit).ToArray());
        if (bruto.StartsWith('+') && !digitos.StartsWith("55")) return bruto;
        if (digitos.Length is 12 or 13 && digitos.StartsWith("55")) digitos = digitos[2..];
        if (digitos.Length is not (10 or 11)) return bruto;
        var linha = digitos.Length == 11 ? 5 : 4;
        return $"({digitos[..2]}) {digitos.Substring(2, linha)}-{digitos[(2 + linha)..]}";
    }

    private static string Ou(string? valor) => string.IsNullOrWhiteSpace(valor) ? NaoInformado : valor.Trim();
    private static string Moeda(decimal v) => v.ToString("C", PtBr);

    private static string Iniciais(string nome)
    {
        var letras = nome.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => char.IsLetter(p[0])).Take(2).Select(p => char.ToUpper(p[0], PtBr));
        var r = string.Concat(letras);
        return r.Length == 0 ? "?" : r;
    }

    private static DateTime NaFuso(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), OperacionalFuso.Zona);

    private static string Data(DateTime valor) => NaFuso(valor).ToString("dd/MM/yyyy HH:mm", PtBr);

    private static (string Valor, string? Data) UltimaCompra(DateTime? ultima, DateTime consultaEm)
    {
        if (ultima is not { } u) return ("—", null);
        var dias = Math.Max(0, (NaFuso(consultaEm).Date - NaFuso(u).Date).Days);
        var valor = dias == 0 ? "hoje" : dias == 1 ? "há 1 dia" : $"há {dias} dias";
        return (valor, NaFuso(u).ToString("dd/MM/yyyy", PtBr));
    }

    /// <summary>
    /// Rótulo da situação pelo vocabulário canônico do domínio (<see cref="StatusPedidoVocabulario"/>); código
    /// desconhecido nunca vaza como snake_case.
    /// </summary>
    private static string Estado(string valor)
    {
        if (StatusPedidoMapper.TryParse(valor, out var status)) return StatusPedidoVocabulario.RotuloLojista(status);
        var limpo = (valor ?? "").Replace('_', ' ').Trim();
        return limpo.Length == 0 ? "—" : char.ToUpper(limpo[0], PtBr) + limpo[1..];
    }

    private static string Canal(string valor) => valor switch { "ChatSite" => "Chat do site", "WhatsApp" => "WhatsApp", "Instagram" => "Instagram", "Email" => "E-mail", _ => valor };
}
