namespace EasyStock.Application.UseCases.Storefront.Frete;

/// <summary>
/// Resultado do <see cref="CalcularFreteUseCase"/> quando há cobertura.
///
/// <para>
/// Campos pensados para serialização direta no controller — o nome dos
/// JSON properties é definido pela política JsonNamingPolicy.SnakeCaseLower
/// em <c>Program.cs</c>, então este record usa PascalCase.
/// </para>
/// </summary>
/// <param name="ZonaId">Id da <c>FreteZona</c> que casou — útil para auditoria/checkout.</param>
/// <param name="Valor">Valor do frete em centavos (inteiro — evita float em dinheiro).</param>
/// <param name="ValorFormatado">Mesmo valor já formatado em pt-BR para UI ("R$ 15,00").</param>
/// <param name="EtaLabel">Texto legível do tempo estimado ("30 min", "1h30").</param>
/// <param name="ZonaLabel">Nome humano da zona ("Centro", "Butantã proximidade").</param>
public sealed record FreteCalculadoDto(
    Guid ZonaId,
    int Valor,
    string ValorFormatado,
    string EtaLabel,
    string ZonaLabel);
