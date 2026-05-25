namespace EasyStock.Domain.Enums;

/// <summary>
/// Unidade de medida usada em <see cref="Entities.Produto"/>, receita (composicao) e lote.
/// Persistida como string (HasConversion) — adicionar valor novo nao requer migration.
/// </summary>
public enum UnidadeMedida
{
    /// <summary>Miligrama (grupo Massa).</summary>
    Mg = 1,
    /// <summary>Grama (grupo Massa).</summary>
    G = 2,
    /// <summary>Quilograma (grupo Massa).</summary>
    Kg = 3,
    /// <summary>Mililitro (grupo Volume).</summary>
    Ml = 4,
    /// <summary>Litro (grupo Volume).</summary>
    L = 5,
    /// <summary>Unidade (grupo Contagem).</summary>
    Un = 6,
    /// <summary>Duzia — converte para Un via fator fixo 12 (grupo Contagem).</summary>
    Dz = 7,
    /// <summary>Caixa — nao converte (tamanho variavel). Operador escolhe explicitamente.</summary>
    Cx = 8
}
