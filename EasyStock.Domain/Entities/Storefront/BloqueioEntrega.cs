using EasyStock.Domain.Exceptions;

namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Bloqueio pontual de entrega — feriados, férias da Babá, dias específicos (ADR-0011).
///
/// <para>
/// Aplicado em cima das <see cref="JanelaEntrega"/> recorrentes:
/// <list type="bullet">
///   <item><c>JanelaEspecificaId = null</c> → bloqueia o <strong>dia inteiro</strong> (todas as janelas dessa data).</item>
///   <item><c>JanelaEspecificaId != null</c> → bloqueia apenas aquela janela na data.</item>
/// </list>
/// </para>
///
/// <para>
/// <strong>Motivo</strong> é obrigatório — vira mensagem visível ao admin
/// ("Feriado", "Férias 15-30/jul") e pode aparecer em logs de "por que não
/// vendi hoje?".
/// </para>
/// </summary>
public class BloqueioEntrega
{
    public Guid Id { get; private set; }
    public Guid StorefrontId { get; private set; }
    public DateOnly Data { get; private set; }

    /// <summary>Null = bloqueia o dia inteiro. Não-null = bloqueia apenas essa janela na data.</summary>
    public Guid? JanelaEspecificaId { get; private set; }

    public string Motivo { get; private set; } = null!;

    public DateTime CriadoEm { get; private set; }

    // EF Core ctor sem parâmetros
    private BloqueioEntrega() { }

    /// <summary>
    /// Factory. <paramref name="janelaEspecificaId"/> null = dia inteiro;
    /// não-null = apenas aquela janela na data.
    /// </summary>
    public static BloqueioEntrega Criar(
        Guid storefrontId,
        DateOnly data,
        string motivo,
        Guid? janelaEspecificaId = null)
    {
        if (storefrontId == Guid.Empty)
            throw new RegraDeDominioVioladaException("StorefrontId é obrigatório.");

        if (string.IsNullOrWhiteSpace(motivo))
            throw new RegraDeDominioVioladaException("Motivo do bloqueio é obrigatório.");

        if (janelaEspecificaId == Guid.Empty)
            throw new RegraDeDominioVioladaException(
                "JanelaEspecificaId não pode ser Guid.Empty — use null para bloquear dia inteiro.");

        return new BloqueioEntrega
        {
            Id = Guid.NewGuid(),
            StorefrontId = storefrontId,
            Data = data,
            JanelaEspecificaId = janelaEspecificaId,
            Motivo = motivo.Trim(),
            CriadoEm = DateTime.UtcNow,
        };
    }
}
