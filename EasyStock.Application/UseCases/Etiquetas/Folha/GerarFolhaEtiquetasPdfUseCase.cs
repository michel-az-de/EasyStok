using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pdf;

namespace EasyStock.Application.UseCases.Etiquetas.Folha;

/// <summary>
/// Monta a folha A4 de etiquetas (Pimaco A4256 ou "quantas cabem") e gera o PDF vetorial (#2200). Recusa com 400
/// tudo que sairia ilegível ou não escanearia: fonte abaixo do piso, variável sem resolver, código sem zona de
/// silêncio, layout que não cabe na posição. Também recusa, antes de gastar CPU, o pedido grande demais: grade com
/// posições demais, texto demais, códigos distintos demais.
/// </summary>
public sealed class GerarFolhaEtiquetasPdfUseCase(IFolhaEtiquetasPdfRenderer renderer, IQrCodeEncoder qr)
{
    public const string CodigoErro = "ETIQUETA_PDF_INVALIDA";
    public const string ModeloPimaco = "pimaco-a4256";
    public const string ModeloQuantasCabem = "quantas-cabem";
    public const int MaxEtiquetas = 500;
    public const int MaxPaginas = 50;

    /// <summary>QR e códigos de barras diferentes por pedido; cópias e conteúdos repetidos contam uma vez.</summary>
    public const int MaxCodigosDistintos = 200;

    /// <summary>Soma dos caracteres dos elementos de texto de todas as etiquetas, já com as cópias.</summary>
    public const int MaxCaracteresDeTexto = 200_000;

    public const decimal MargemPadraoMm = 5m;
    public const decimal EspacoPadraoMm = 3m;
    public const decimal MargemMaximaMm = 50m;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private const decimal Tol = 0.05m;
    private const int MaxErrosNaMensagem = 20;
    private const int MaxErrosNoDetalhe = 100;
    private const int MaxEcoDaExcecao = 200;

    public async Task<byte[]> ExecuteAsync(GerarFolhaEtiquetasPdfCommand command, CancellationToken ct = default)
    {
        var plano = Planejar(command, ct);
        ct.ThrowIfCancellationRequested();
        return await renderer.RenderAsync(plano, ct);
    }

    /// <summary>
    /// Valida o pedido e posiciona cada etiqueta na grade, sem desenhar. Primeiro as conferências baratas (tamanho,
    /// encaixe, páginas, tetos de carga); só depois, e uma vez por conteúdo, os QR e as barras.
    /// </summary>
    /// <exception cref="UseCaseValidationException">Com <see cref="CodigoErro"/> e a lista de motivos.</exception>
    public PlanoFolhaEtiquetas Planejar(GerarFolhaEtiquetasPdfCommand command, CancellationToken ct = default)
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

        // Fase 1, barata: lê os layouts e confere tamanho, carga, encaixe e páginas. Nenhum QR nem barra sai daqui.
        var lidas = new List<(LayoutJsonDocument Layout, Medida Medida)>(pedidas!.Count);
        var distintos = new HashSet<(string?, string?)>();
        var total = 0;
        long caracteres = 0;
        for (var i = 0; i < pedidas.Count; i++)
        {
            var rotulo = $"etiqueta {i + 1}";
            var pedida = pedidas[i];
            var copias = pedida?.Copias ?? 1;
            var copiasValidas = copias is >= 1 and <= MaxEtiquetas;
            if (!copiasValidas) erros.Add($"{rotulo}: copias deve ficar entre 1 e {MaxEtiquetas}.");
            else total += copias;

            var layout = Ler(pedida, rotulo, erros);
            if (layout is null) continue;
            if (orientacao is not null && layout.Size?.Orientation is { } o && o != orientacao)
                erros.Add($"{rotulo}: layout {o}, mas a folha pede {orientacao}.");
            if (!LayoutParaPdf.TamanhoValido(layout, erros, rotulo)) continue;

            var vezes = copiasValidas ? copias : 0;
            lidas.Add((layout, new Medida(layout.Size!.WMm, layout.Size.HMm, vezes, rotulo)));
            caracteres += (long)vezes * LayoutParaPdf.CaracteresDeTexto(layout);
            LayoutParaPdf.CodigosDe(layout, distintos);
        }
        if (total > MaxEtiquetas) erros.Add($"no máximo {MaxEtiquetas} etiquetas por pedido; vieram {total}.");
        if (caracteres > MaxCaracteresDeTexto)
            erros.Add(string.Create(PtBr, $"no máximo {MaxCaracteresDeTexto:N0} caracteres de texto por pedido, somadas as cópias; vieram {caracteres:N0}."));
        if (distintos.Count > MaxCodigosDistintos)
            erros.Add($"no máximo {MaxCodigosDistintos} códigos distintos (QR e barras) por pedido; vieram {distintos.Count}.");
        if (erros.Count > 0) throw Falha(erros);

        var medidas = lidas.Select(l => l.Medida).ToList();
        var grade = Grade(modelo!, medidas, margem, espaco, erros);
        if (grade is null) throw Falha(erros);

        var celula = grade.Celulas[0];
        var encaixes = medidas.Select(m => Encaixar(m, celula, erros)).ToList();
        if (command.Pular >= grade.PorPagina)
            erros.Add($"pular deve ser menor que as {grade.PorPagina} posições da folha.");
        var paginas = ((long)command.Pular + total + grade.PorPagina - 1) / grade.PorPagina;
        if (paginas > MaxPaginas) erros.Add($"no máximo {MaxPaginas} páginas; o pedido daria {paginas}.");
        if (erros.Count > 0) throw Falha(erros);

        // Fase 2: o pedido cabe. Gera QR e barras, cada conteúdo uma vez, e confere os pisos de leitura.
        var codigos = new CodigosDoPedido(qr);
        var planos = new List<EtiquetaPlano?>(lidas.Count);
        foreach (var (layout, medida) in lidas)
        {
            ct.ThrowIfCancellationRequested();
            planos.Add(LayoutParaPdf.Converter(layout, codigos, erros, medida.Rotulo));
        }
        if (erros.Count > 0) throw Falha(erros);

        var fila = new List<(EtiquetaPlano Plano, Encaixe Encaixe)>(total);
        for (var i = 0; i < planos.Count; i++)
            fila.AddRange(Enumerable.Repeat((planos[i]!, encaixes[i]!), medidas[i].Copias));

        var resultado = new List<PaginaPlano>((int)paginas);
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

    private sealed record Medida(decimal WMm, decimal HMm, int Copias, string Rotulo);

    private sealed record Encaixe(decimal DxMm, decimal DyMm, bool Girar);

    private static LayoutJsonDocument? Ler(EtiquetaNaFolha? pedida, string rotulo, List<string> erros)
    {
        if (pedida is null) { erros.Add($"{rotulo}: vazia."); return null; }
        var json = pedida.Layout;
        try
        {
            return json.ValueKind switch
            {
                JsonValueKind.Object => Desserializar(json, rotulo, erros),
                JsonValueKind.String => DesserializarTexto(json.GetString()!, rotulo, erros),
                _ => Ausente(),
            };
        }
        catch (JsonException ex)
        {
            erros.Add($"{rotulo}: layout não é um LayoutJson v1 válido ({LayoutParaPdf.Eco(ex.Message, MaxEcoDaExcecao)})");
            return null;
        }

        LayoutJsonDocument? Ausente()
        {
            erros.Add($"{rotulo}: layout ausente.");
            return null;
        }
    }

    private static LayoutJsonDocument? DesserializarTexto(string texto, string rotulo, List<string> erros)
    {
        using var documento = JsonDocument.Parse(texto);
        return Desserializar(documento.RootElement, rotulo, erros);
    }

    /// <summary>Confere o tamanho do array de elementos antes de desserializar cada um deles.</summary>
    private static LayoutJsonDocument? Desserializar(JsonElement json, string rotulo, List<string> erros)
    {
        if (json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty("elements", out var elementos)
            && elementos.ValueKind == JsonValueKind.Array
            && elementos.GetArrayLength() > LayoutParaPdf.MaxElementos)
        {
            erros.Add($"{rotulo}: no máximo {LayoutParaPdf.MaxElementos} elementos.");
            return null;
        }
        return json.Deserialize<LayoutJsonDocument>();
    }

    private static GradeFolha? Grade(string modelo, List<Medida> etiquetas, decimal margem, decimal espaco, List<string> erros)
    {
        if (modelo == ModeloPimaco) return GradeFolha.PimacoA4256();

        var primeira = etiquetas[0];
        var diferente = etiquetas.FirstOrDefault(e =>
            Math.Abs(e.WMm - primeira.WMm) > Tol || Math.Abs(e.HMm - primeira.HMm) > Tol);
        if (diferente is not null)
        {
            erros.Add(string.Create(PtBr, $"{diferente.Rotulo}: '{ModeloQuantasCabem}' exige todas as etiquetas do mesmo tamanho ({primeira.WMm:0.##} x {primeira.HMm:0.##} mm)."));
            return null;
        }
        if (primeira.WMm < GradeFolha.LadoMinimoMm || primeira.HMm < GradeFolha.LadoMinimoMm)
        {
            erros.Add(string.Create(PtBr, $"etiqueta de {primeira.WMm:0.##} x {primeira.HMm:0.##} mm: '{ModeloQuantasCabem}' exige no mínimo {GradeFolha.LadoMinimoMm:0.##} mm de lado."));
            return null;
        }
        var (colunas, linhas) = GradeFolha.Dimensionar(primeira.WMm, primeira.HMm, margem, espaco);
        if (colunas * linhas > GradeFolha.MaxCelulasPorPagina)
        {
            erros.Add(string.Create(PtBr, $"etiqueta de {primeira.WMm:0.##} x {primeira.HMm:0.##} mm daria {colunas * linhas} posições por folha; no máximo {GradeFolha.MaxCelulasPorPagina}."));
            return null;
        }
        var grade = GradeFolha.QuantasCabem(primeira.WMm, primeira.HMm, margem, espaco);
        if (grade is null)
            erros.Add(string.Create(PtBr, $"etiqueta de {primeira.WMm:0.##} x {primeira.HMm:0.##} mm não cabe na A4 com margem de {margem:0.##} mm."));
        return grade;
    }

    /// <summary>Centraliza na posição; gira 90° quando só cabe deitada (ex.: layout vertical na Pimaco).</summary>
    private static Encaixe? Encaixar(Medida etiqueta, CelulaGrade celula, List<string> erros)
    {
        if (etiqueta.WMm <= celula.WMm + Tol && etiqueta.HMm <= celula.HMm + Tol)
            return new Encaixe((celula.WMm - etiqueta.WMm) / 2, (celula.HMm - etiqueta.HMm) / 2, false);
        if (etiqueta.HMm <= celula.WMm + Tol && etiqueta.WMm <= celula.HMm + Tol)
            return new Encaixe((celula.WMm - etiqueta.HMm) / 2, (celula.HMm - etiqueta.WMm) / 2, true);
        erros.Add(string.Create(PtBr, $"{etiqueta.Rotulo}: layout de {etiqueta.WMm:0.##} x {etiqueta.HMm:0.##} mm não cabe na posição de {celula.WMm:0.##} x {celula.HMm:0.##} mm."));
        return null;
    }

    private static UseCaseValidationException Falha(List<string> erros) =>
        new(CodigoErro, string.Join("; ", erros.Take(MaxErrosNaMensagem)), new { erros = erros.Take(MaxErrosNoDetalhe).ToList() });
}
