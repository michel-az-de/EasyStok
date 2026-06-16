using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Cardapio;

/// <summary>Tela de gestão do cardápio (galeria). Agrupa os itens por categoria para a view.</summary>
public class CardapioIndexViewModel
{
    /// <summary>false = a empresa ainda não tem vitrine (estado dedicado, não é erro).</summary>
    public bool TemVitrine { get; set; }
    public string? Slug { get; set; }
    public string TituloVitrine { get; set; } = string.Empty;
    public bool VitrineAtiva { get; set; }

    /// <summary>Link público de impressão do cardápio (nova aba). Null se não houver PublicApiUrl.</summary>
    public string? ImprimirUrl { get; set; }

    public int Total { get; set; }
    public List<CardapioCategoriaGrupo> Grupos { get; set; } = new();
}

public class CardapioCategoriaGrupo
{
    public string Categoria { get; set; } = "Sem categoria";
    public List<CardapioItemApi> Itens { get; set; } = new();
}

/// <summary>Formulário de criar/editar item. Modo "avulso" (item próprio) ou "vinculado" (produto do estoque).</summary>
public class CardapioItemFormViewModel
{
    public Guid? Id { get; set; }
    public string Modo { get; set; } = "avulso";   // avulso | vinculado

    public Guid? ProdutoId { get; set; }
    public string? ProdutoNome { get; set; }        // exibição do produto vinculado (read-only no editar)

    public string? NomePublico { get; set; }
    public decimal? PrecoStorefront { get; set; }
    public string? CategoriaTexto { get; set; }
    public string? DescricaoPublica { get; set; }

    // Detalhes para o cliente (opcionais).
    public string? Ingredientes { get; set; }
    public string? Alergenos { get; set; }
    public string? SugestaoMolho { get; set; }
    public string? TempoPreparo { get; set; }
    public string? PesoExibicao { get; set; }

    public bool Publicar { get; set; }
    public string? FotoUrl { get; set; }
    public bool Disponivel { get; set; } = true;

    public bool EhEdicao => Id.HasValue;
}
