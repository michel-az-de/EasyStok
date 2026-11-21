namespace EasyStock.Application.UseCases.Etiquetas.Folha;

/// <summary>Folha pronta para desenhar: tudo em mm, já validado; o renderer só posiciona.</summary>
public sealed record PlanoFolhaEtiquetas(decimal PaginaWMm, decimal PaginaHMm, bool Guias, IReadOnlyList<PaginaPlano> Paginas);

public sealed record PaginaPlano(IReadOnlyList<CelulaPlano> Celulas);

/// <summary>
/// Uma posição da grade. A etiqueta, quando há, fica centrada na célula com o canto superior esquerdo em
/// (<paramref name="EtiquetaXMm"/>, <paramref name="EtiquetaYMm"/>); com <paramref name="Girar"/>, ela gira 90° no
/// sentido horário e ocupa <c>HMm</c> x <c>WMm</c> na página.
/// </summary>
public sealed record CelulaPlano(CelulaGrade Grade, EtiquetaPlano? Etiqueta, decimal EtiquetaXMm, decimal EtiquetaYMm, bool Girar);

/// <summary>Etiqueta no próprio sistema de coordenadas (origem no canto superior esquerdo, sem girar).</summary>
public sealed record EtiquetaPlano(decimal WMm, decimal HMm, IReadOnlyList<ElementoPlano> Elementos);

public abstract record ElementoPlano(decimal XMm, decimal YMm, decimal WMm, decimal HMm);

public enum AlinhamentoTexto { Esquerda, Centro, Direita }

/// <summary>Uma linha de texto, centrada na altura da caixa e cortada com reticências se passar da largura.</summary>
public sealed record TextoPlano(decimal XMm, decimal YMm, decimal WMm, decimal HMm,
    string Texto, decimal TamanhoPt, int Peso, AlinhamentoTexto Alinhamento) : ElementoPlano(XMm, YMm, WMm, HMm);

/// <summary>Área do símbolo, sem a zona de silêncio. <paramref name="Matriz"/>[linha, coluna], true = módulo escuro.</summary>
public sealed record QrPlano(decimal XMm, decimal YMm, decimal LadoMm, bool[,] Matriz) : ElementoPlano(XMm, YMm, LadoMm, LadoMm);

/// <summary>Área das barras, sem a zona de silêncio. <paramref name="Modulos"/>: 1 = barra, 0 = espaço.</summary>
public sealed record BarrasPlano(decimal XMm, decimal YMm, decimal WMm, decimal HMm, string Modulos) : ElementoPlano(XMm, YMm, WMm, HMm);

/// <summary>Divisória: traço horizontal no meio da caixa.</summary>
public sealed record LinhaPlano(decimal XMm, decimal YMm, decimal WMm, decimal HMm) : ElementoPlano(XMm, YMm, WMm, HMm);
