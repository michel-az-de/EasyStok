namespace EasyStock.Domain.Entities;

/// <summary>
/// Favoritos do menu lateral ("Meu dia") por usuario + loja (ADR-0032, fatia 4).
/// <see cref="FavoritosMenu"/> e a lista ORDENADA de chaves de rota do MenuDefinition.
/// Ausencia de linha = usuario nunca personalizou (o front aplica o seed por perfil).
/// Lista vazia = sem favoritos (a secao "Meu dia" some). Isolado por <see cref="EmpresaId"/>
/// (tenant) e unico por (UsuarioId, LojaId).
/// </summary>
public class PreferenciaMenuUsuario
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid LojaId { get; set; }
    public Guid EmpresaId { get; set; }
    public List<string> FavoritosMenu { get; set; } = new();
    public DateTime AtualizadaEm { get; set; }

    public Usuario? Usuario { get; set; }
    public Loja? Loja { get; set; }
    public Empresa? Empresa { get; set; }

    public static PreferenciaMenuUsuario Criar(
        Guid usuarioId, Guid lojaId, Guid empresaId, IEnumerable<string> favoritos)
    {
        return new PreferenciaMenuUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            LojaId = lojaId,
            EmpresaId = empresaId,
            FavoritosMenu = favoritos.ToList(),
            AtualizadaEm = DateTime.UtcNow,
        };
    }

    /// <summary>Substitui a lista por uma NOVA instancia (o ValueComparer do EF detecta a troca).</summary>
    public void DefinirFavoritos(IEnumerable<string> favoritos)
    {
        FavoritosMenu = favoritos.ToList();
        AtualizadaEm = DateTime.UtcNow;
    }
}
