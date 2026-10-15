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
/// <param name="TempoEstimadoMinutos">Minutos numéricos do <paramref name="EtaLabel"/>, para uso interno
/// (S14, agente de atendimento). Fora do JSON público: o contrato do storefront não muda.</param>
/// <param name="GeocodeConfiavel">O frete saiu do raio, com o endereço localizado com precisão (I2.2).
/// <see langword="false"/> no frete por zona.</param>
/// <param name="DistanciaMetros">Distância de rota usada na faixa (medida, ou linha reta × fator). Só no raio.</param>
/// <param name="FaixaId">Faixa do raio que definiu o valor (<c>gratis</c> na distância grátis). Só no raio.</param>
/// <param name="Gratis">Dentro da distância com frete grátis do raio.</param>
/// <param name="ForaDeCobertura">Sempre <see langword="false"/> aqui: fora de cobertura segue respondendo 422.</param>
public sealed record FreteCalculadoDto(
    Guid ZonaId,
    int Valor,
    string ValorFormatado,
    string EtaLabel,
    string ZonaLabel,
    [property: System.Text.Json.Serialization.JsonIgnore] int? TempoEstimadoMinutos = null,
    bool GeocodeConfiavel = false,
    int? DistanciaMetros = null,
    string? FaixaId = null,
    bool Gratis = false,
    bool ForaDeCobertura = false);
