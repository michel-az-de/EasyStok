using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pdf;

namespace EasyStock.Application.UseCases.Etiquetas.Folha;

/// <summary>
/// Monta a folha A4 de etiquetas (Pimaco A4256 ou "quantas cabem") e gera o PDF vetorial (#2200). Recusa com 400
/// tudo que sairia ilegível ou não escanearia: fonte abaixo do piso, variável sem resolver, código sem zona de
/// silêncio, layout que não cabe na posição.
/// </summary>
public sealed class GerarFolhaEtiquetasPdfUseCase(IFolhaEtiquetasPdfRenderer renderer, IQrCodeEncoder qr)
{
    public const string CodigoErro = "ETIQUETA_PDF_INVALIDA";
    public const string ModeloPimaco = "pimaco-a4256";
    public const string ModeloQuantasCabem = "quantas-cabem";
    public const int MaxEtiquetas = 500;
    public const int MaxPaginas = 50;
    public const decimal MargemPadraoMm = 5m;
    public const decimal EspacoPadraoMm = 3m;
    public const decimal MargemMaximaMm = 50m;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private const decimal Tol = 0.05m;
    private const int MaxErrosNaMensagem = 20;
    private const int MaxErrosNoDetalhe = 100;

    public async Task<byte[]> ExecuteAsync(GerarFolhaEtiquetasPdfCommand command, CancellationToken ct = default)
    {
        var plano = Planejar(command);
        ct.ThrowIfCancellationRequested();
        return await renderer.RenderAsync(plano, ct);
    }

    /// <summary>Valida o pedido e posiciona cada etiqueta na grade, sem desenhar.</summary>
    /// <exception cref="UseCaseValidationException">Com <see cref="CodigoErro"/> e a lista de motivos.</exception>
    public PlanoFolhaEtiquetas Planejar(GerarFolhaEtiquetasPdfCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var erros = new List<string>();

        var folha = command.Folha;
        var modelo = folha?.Modelo?.Trim().ToLowerInvariant();
        if (modelo is not (ModeloPimaco or ModeloQuantasCabem))
            erros.Add($"folha.modelo deve ser '{ModeloPimaco}' ou '{ModeloQuantasCabem}'.");
        var orientacao = string.IsNullOrWhiteSpace(folha?.Orientacao) ? null : folha.Orientacao.Trim().ToLowerInvariant();
        if (orientacao is not (null or "horizontal" or "vertical"))
            erros.Add("folha.orientacao deve ser 'horizontal' ou 'vertical'.");
        var margem = folha?.MargemMm ?? MargemPadraoMm;
        var espaco = folha?.EspacoMm ?? EspacoPadraoMm;
        if (margem is < 0 or > MargemMaximaMm) erros.Add($"folha.margem_mm deve ficar entre 0 e {MargemMaximaMm} mm.");
        if (espaco is < 0 or > MargemMaximaMm) erros.Add($"folha.espaco_mm deve ficar entre 0 e {MargemMaximaMm} mm.");
        if (command.Pular < 0) erros.Add("pular não pode ser negativo.");

        var pedidas = command.Etiquetas;
        if (pedidas is null || pedidas.Count == 0) erros.Add("etiquetas não pode estar vazio.");
        else if (pedidas.Count > MaxEtiquetas) erros.Add($"no máximo {MaxEtiquetas} etiquetas por folha.");
        if (erros.Count > 0) throw Falha(erros);

        var etiquetas = new List<(EtiquetaPlano Plano, int Copias, string Rotulo)>(pedidas!.Count);
        var total = 0;
        for (var i = 0; i < pedidas.Count; i++)
        {
            var rotulo = $"etiqueta {i + 1}";
            var pedida = pedidas[i];
            var copias = pedida?.Copias ?? 1;
            if (copias is < 1 or > MaxEtiquetas) erros.Add($"{rotulo}: copias deve ficar entre 1 e {MaxEtiquetas}.");
            else total += copias;

            var layout = Ler(pedida, rotulo, erros);
            if (layout is null) continue;
            if (orientacao is not null && layout.Size?.Orientation is { } o && o != orientacao)
                erros.Add($"{rotulo}: layout {o}, mas a folha pede {orientacao}.");
            var plano = LayoutParaPdf.Converter(layout, qr, erros, rotulo);
            if (plano is not null) etiquetas.Add((plano, copias, rotulo));
        }
        if (total > MaxEtiquetas) erros.Add($"no máximo {MaxEtiquetas} etiquetas por pedido; vieram {total}.");
        if (erros.Count > 0) throw Falha(erros);

        var grade = Grade(modelo!, etiquetas, margem, espaco, erros);
        if (grade is null) throw Falha(erros);

        var celula = grade.Celulas[0];
        var encaixes = etiquetas.Select(e => Encaixar(e.Plano, celula, e.Rotulo, erros)).ToList();
        if (command.Pular >= grade.PorPagina)
            erros.Add($"pular deve ser menor que as {grade.PorPagina} posições da folha.");
        var paginas = (command.Pular + total + grade.PorPagina - 1) / grade.PorPagina;
        if (paginas > MaxPaginas) erros.Add($"no máximo {MaxPaginas} páginas; o pedido daria {paginas}.");
        if (erros.Count > 0) throw Falha(erros);

        var fila = new List<(EtiquetaPlano Plano, Encaixe Encaixe)>(total);
        for (var i = 0; i < etiquetas.Count; i++)
            fila.AddRange(Enumerable.Repeat((etiquetas[i].Plano, encaixes[i]!), etiquetas[i].Copias));

        var resultado = new List<PaginaPlano>(paginas);
        for (var p = 0; p < paginas; p++)
        {
            var celulas = new List<CelulaPlano>(grade.PorPagina);
            for (var c = 0; c < grade.PorPagina; c++)
            {
                var posicao = grade.Celulas[c];
                var indice = p * grade.PorPagina + c - command.Pular;
                if (indice < 0 || indice >= fila.Count)
                {
                    celulas.Add(new CelulaPlano(posicao, null, posicao.XMm, posicao.YMm, false));
                    continue;
                }
                var (plano, encaixe) = fila[indice];
                celulas.Add(new CelulaPlano(posicao, plano, posicao.XMm + encaixe.DxMm, posicao.YMm + encaixe.DyMm, encaixe.Girar));
            }
            resultado.Add(new PaginaPlano(celulas));
        }
        return new PlanoFolhaEtiquetas(GradeFolha.A4LarguraMm, GradeFolha.A4AlturaMm, command.Guias, resultado);
    }

    private sealed record Encaixe(decimal DxMm, decimal DyMm, bool Girar);

    private static LayoutJsonDocument? Ler(EtiquetaNaFolha? pedida, string rotulo, List<string> erros)
    {
        if (pedida is null) { erros.Add($"{rotulo}: vazia."); return null; }
        var json = pedida.Layout;
        try
        {
            return json.ValueKind switch
            {
                JsonValueKind.Object => json.Deserialize<LayoutJsonDocument>(),
                JsonValueKind.String => JsonSerializer.Deserialize<LayoutJsonDocument>(json.GetString()!),
                _ => Ausente(),
            };
        }
        catch (JsonException ex)
        {
            erros.Add($"{rotulo}: layout não é um LayoutJson v1 válido ({ex.Message})");
            return null;
        }

        LayoutJsonDocument? Ausente()
        {
            erros.Add($"{rotulo}: layout ausente.");
            return null;
        }
    }

    private static GradeFolha? Grade(string modelo, List<(EtiquetaPlano Plano, int Copias, string Rotulo)> etiquetas,
        decimal margem, decimal espaco, List<string> erros)
    {
        if (modelo == ModeloPimaco) return GradeFolha.PimacoA4256();

        var primeira = etiquetas[0].Plano;
        var diferente = etiquetas.FirstOrDefault(e =>
            Math.Abs(e.Plano.WMm - primeira.WMm) > Tol || Math.Abs(e.Plano.HMm - primeira.HMm) > Tol);
        if (diferente.Plano is not null)
        {
            erros.Add(string.Create(PtBr, $"{diferente.Rotulo}: '{ModeloQuantasCabem}' exige todas as etiquetas do mesmo tamanho ({primeira.WMm:0.##} x {primeira.HMm:0.##} mm)."));
            return null;
        }
        var grade = GradeFolha.QuantasCabem(primeira.WMm, primeira.HMm, margem, espaco);
        if (grade is null)
            erros.Add(string.Create(PtBr, $"etiqueta de {primeira.WMm:0.##} x {primeira.HMm:0.##} mm não cabe na A4 com margem de {margem:0.##} mm."));
        return grade;
    }

    /// <summary>Centraliza na posição; gira 90° quando só cabe deitada (ex.: layout vertical na Pimaco).</summary>
    private static Encaixe? Encaixar(EtiquetaPlano etiqueta, CelulaGrade celula, string rotulo, List<string> erros)
    {
        if (etiqueta.WMm <= celula.WMm + Tol && etiqueta.HMm <= celula.HMm + Tol)
            return new Encaixe((celula.WMm - etiqueta.WMm) / 2, (celula.HMm - etiqueta.HMm) / 2, false);
        if (etiqueta.HMm <= celula.WMm + Tol && etiqueta.WMm <= celula.HMm + Tol)
            return new Encaixe((celula.WMm - etiqueta.HMm) / 2, (celula.HMm - etiqueta.WMm) / 2, true);
        erros.Add(string.Create(PtBr, $"{rotulo}: layout de {etiqueta.WMm:0.##} x {etiqueta.HMm:0.##} mm não cabe na posição de {celula.WMm:0.##} x {celula.HMm:0.##} mm."));
        return null;
    }

    private static UseCaseValidationException Falha(List<string> erros) =>
        new(CodigoErro, string.Join("; ", erros.Take(MaxErrosNaMensagem)), new { erros = erros.Take(MaxErrosNoDetalhe).ToList() });
}
