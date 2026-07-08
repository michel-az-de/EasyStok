namespace EasyStock.Domain.Entities.Banners;

/// <summary>
/// Interação de um usuário com um <see cref="Banner"/> — "visto" (dispensou) ou
/// "confirmado" (Ok, recebi). Tabela GLOBAL, sem <c>EmpresaId</c> (molde
/// <c>FaqVisualizacao</c>): o usuário é único por <c>UsuarioId</c> e a confirmação
/// vale independente da empresa ativa da sessão — senão o banner obrigatório
/// reapareceria ao trocar de empresa. Idempotência via índice único
/// <c>(BannerId, UsuarioId, Tipo)</c>.
/// </summary>
public class BannerConfirmacao
{
    public Guid Id { get; private set; }
    public Guid BannerId { get; private set; }
    public Guid UsuarioId { get; private set; }
    public BannerInteracaoTipo Tipo { get; private set; }
    public DateTime RegistradoEm { get; private set; }

    public Banner? Banner { get; private set; }

    // EF Core ctor sem parâmetros.
    private BannerConfirmacao() { }

    public static BannerConfirmacao Criar(Guid bannerId, Guid usuarioId, BannerInteracaoTipo tipo)
    {
        if (bannerId == Guid.Empty)
            throw new RegraDeDominioVioladaException("BannerId é obrigatório.");
        if (usuarioId == Guid.Empty)
            throw new RegraDeDominioVioladaException("UsuarioId é obrigatório.");

        return new BannerConfirmacao
        {
            Id = Guid.NewGuid(),
            BannerId = bannerId,
            UsuarioId = usuarioId,
            Tipo = tipo,
            RegistradoEm = DateTime.UtcNow
        };
    }
}
