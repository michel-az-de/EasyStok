using System.Text.Json;
using EasyStock.Application.Ports.Output.Integrations;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Domain.Storefront.Frete;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.UseCases.Storefront.Entrega;

// ── Contratos ──────────────────────────────────────────────────────────

/// <summary>Faixa de preço: cobre a distância de rota até <c>AteMetros</c>, inclusive.</summary>
public sealed record FreteRaioFaixaInput(int AteMetros, int ValorCentavos);

/// <summary>
/// Frete por raio da loja. A cozinha vem como endereço (CEP e número): a coordenada é sempre resolvida
/// no servidor (ADR-0017), nunca aceita do cliente. Sem CEP e sem número, vale a cozinha já gravada.
/// </summary>
public sealed record FreteRaioInput(
    string? Cep, string? Numero, int RaioMaxMetros, int FaixaGratisMetros, double FatorRota,
    IReadOnlyList<FreteRaioFaixaInput> Faixas);

public sealed record FreteRaioFaixaResult(string Id, int AteMetros, int ValorCentavos);

public sealed record FreteRaioResult(
    int RaioMaxMetros, int FaixaGratisMetros, double FatorRota, IReadOnlyList<FreteRaioFaixaResult> Faixas);

/// <summary>
/// O que está gravado, para a tela reexibir (I2.2). O endereço da cozinha não é guardado, só a
/// coordenada: <c>Configurado</c> diz se ela existe.
/// </summary>
public sealed record FreteRaioConfigResult(
    bool Configurado, int? RaioMaxMetros, int? FaixaGratisMetros, double? FatorRota,
    IReadOnlyList<FreteRaioFaixaResult> Faixas);

public sealed record TestarFreteRaioInput(string Cep, string Numero);

/// <summary>
/// O que o checkout faria com o endereço. <c>Modo</c> é <c>raio</c> ou <c>zona</c>; em <c>zona</c> o raio
/// não decidiu (ver <c>Motivo</c>) e a tabela de zonas é quem cobra. <c>DistanciaEstimada</c> indica
/// linha reta × fator, sem rota medida.
/// </summary>
public sealed record FreteRaioTesteResult(
    string Modo, int? DistanciaMetros, int? DistanciaRotaMetros, bool DistanciaEstimada,
    string? FaixaId, int? ValorCentavos, bool Gratis, bool ForaDeCobertura, string? Motivo)
{
    internal static FreteRaioTesteResult Zona(string motivo) =>
        new("zona", null, null, false, null, null, false, false, motivo);
}

/// <summary>Não há geocodificador ligado no servidor: 422, a loja não tem como corrigir pelo formulário.</summary>
public sealed class GeocodificadorIndisponivelException()
    : Exception("A localização de endereços não está ligada neste servidor. Fale com o suporte para ativar o frete por distância.");

/// <summary>Endereço (CEP e número) pronto para o geocodificador, como o checkout monta.</summary>
internal static class EnderecoParaGeocode
{
    public static async Task<GeocodeQuery> MontarAsync(ICepLookupClient ceps, string? cep, string? numero, CancellationToken ct)
    {
        var cepNormalizado = new string((cep ?? string.Empty).Where(char.IsDigit).ToArray());
        if (cepNormalizado.Length != 8)
            throw new UseCaseValidationException("CEP inválido. Informe os 8 dígitos.");
        if (string.IsNullOrWhiteSpace(numero))
            throw new UseCaseValidationException("Informe o número do endereço.");

        var endereco = await ceps.LookupAsync(cepNormalizado, ct);
        return new GeocodeQuery(
            Logradouro: endereco?.Logradouro,
            Numero: numero.Trim(),
            Bairro: endereco?.Bairro,
            Cidade: endereco?.Cidade,
            Uf: endereco?.Uf,
            Cep: cepNormalizado);
    }
}

// ── Configurar ─────────────────────────────────────────────────────────

/// <summary>
/// I2.1: grava cozinha, raio e faixas do frete por distância. Só grava com geocodificação confiável do
/// endereço da cozinha; qualquer recusa deixa a configuração anterior intacta. I2.2: relê o gravado e,
/// sem endereço no pedido, mantém a cozinha que já está lá.
/// </summary>
public sealed class ConfigurarFreteRaioUseCase(
    IStorefrontRepository lojas, ICepLookupClient ceps, IGeocodingClient geocoding, IUnitOfWork unitOfWork)
{
    private const int MaximoDeFaixas = 20;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<FreteRaioConfigResult> ObterAsync(Guid empresaId, CancellationToken ct = default)
    {
        var loja = await LojaDoTenant.ObterAsync(lojas, empresaId, ct);
        var config = FreteRaioConfigFactory.TentarCriar(loja);
        return config is null
            ? new FreteRaioConfigResult(false, null, null, null, [])
            : new FreteRaioConfigResult(
                true, config.RaioMaxMetros, config.FaixaGratisMetros, config.FatorRota,
                config.Faixas.Select(f => new FreteRaioFaixaResult(f.Id, f.AteMetros, f.ValorCentavos)).ToList());
    }

    public async Task<FreteRaioResult> ExecuteAsync(Guid empresaId, FreteRaioInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var loja = await LojaDoTenant.ObterAsync(lojas, empresaId, ct);
        var faixas = ValidarFaixas(input);
        var (lat, lng) = await CozinhaAsync(loja, input, ct);

        loja.ConfigurarFreteRaio(
            lat, lng, input.FatorRota, input.FaixaGratisMetros, input.RaioMaxMetros,
            JsonSerializer.Serialize(faixas, JsonOpts));
        await lojas.UpdateAsync(loja, ct);
        await unitOfWork.CommitAsync();

        return new FreteRaioResult(input.RaioMaxMetros, input.FaixaGratisMetros, input.FatorRota, faixas);
    }

    /// <summary>Endereço novo é geocodificado; sem endereço, vale a coordenada que a loja já tem.</summary>
    private async Task<(double Lat, double Lng)> CozinhaAsync(StorefrontEntity loja, FreteRaioInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Cep) && string.IsNullOrWhiteSpace(input.Numero))
            return loja is { CozinhaLat: { } lat, CozinhaLng: { } lng }
                ? (lat, lng)
                : throw new UseCaseValidationException("Informe o CEP e o número da cozinha.");

        if (!geocoding.Disponivel)
            throw new GeocodificadorIndisponivelException();

        var cozinha = await geocoding.GeocodificarAsync(
            await EnderecoParaGeocode.MontarAsync(ceps, input.Cep, input.Numero, ct), ct);
        return cozinha is { Confiavel: true }
            ? (cozinha.Lat, cozinha.Lng)
            : throw new UseCaseValidationException(
                "Não conseguimos localizar o endereço da cozinha com precisão. Confira o CEP e o número.");
    }

    /// <summary>
    /// Faixas em ordem crescente e sem repetição (cada uma cobre do fim da anterior até o seu limite),
    /// começando depois da distância grátis e alcançando o raio máximo, para não sobrar buraco sem preço.
    /// </summary>
    private static List<FreteRaioFaixaResult> ValidarFaixas(FreteRaioInput input)
    {
        if (input.RaioMaxMetros <= 0)
            throw new UseCaseValidationException("O raio máximo de entrega deve ser maior que zero.");
        if (input.FaixaGratisMetros < 0 || input.FaixaGratisMetros > input.RaioMaxMetros)
            throw new UseCaseValidationException("A distância com frete grátis deve ficar entre zero e o raio máximo.");
        if (!(input.FatorRota >= 1 && input.FatorRota <= 3))
            throw new UseCaseValidationException("O fator de rota deve ficar entre 1 e 3.");

        var faixas = input.Faixas ?? [];
        if (faixas.Count > MaximoDeFaixas)
            throw new UseCaseValidationException($"Use no máximo {MaximoDeFaixas} faixas de frete.");

        var alcance = input.FaixaGratisMetros;
        foreach (var faixa in faixas)
        {
            if (faixa.ValorCentavos < 0)
                throw new UseCaseValidationException("O valor de uma faixa de frete não pode ser negativo.");
            if (faixa.AteMetros <= alcance)
                throw new UseCaseValidationException(
                    "As faixas de frete devem estar em ordem crescente de distância, sem repetir, e começar depois da distância com frete grátis.");
            alcance = faixa.AteMetros;
        }

        if (alcance < input.RaioMaxMetros)
            throw new UseCaseValidationException("As faixas de frete precisam alcançar o raio máximo de entrega.");

        return faixas.Select(f => new FreteRaioFaixaResult($"ate-{f.AteMetros}m", f.AteMetros, f.ValorCentavos)).ToList();
    }
}

// ── Testar ─────────────────────────────────────────────────────────────

/// <summary>
/// I2.1: "testar um endereço". Usa o mesmo <see cref="FreteRaioCotador"/> do checkout, então o valor
/// mostrado à loja é o que o cliente pagaria. Não grava nada.
/// </summary>
public sealed class TestarFreteRaioUseCase(
    IStorefrontRepository lojas, ICepLookupClient ceps, IGeocodingClient geocoding, IRotaClient rotas,
    ICotacaoFreteExterna? cotacaoExterna = null)
{
    public async Task<FreteRaioTesteResult> ExecuteAsync(Guid empresaId, TestarFreteRaioInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var loja = await LojaDoTenant.ObterAsync(lojas, empresaId, ct);
        var destino = await EnderecoParaGeocode.MontarAsync(ceps, input.Cep, input.Numero, ct);

        if (!geocoding.Disponivel)
            throw new GeocodificadorIndisponivelException();

        var config = FreteRaioConfigFactory.TentarCriar(loja);
        if (config is null)
            return FreteRaioTesteResult.Zona("O frete por distância ainda não foi configurado. Vale a tabela de zonas.");

        var cotacao = await FreteRaioCotador.CotarAsync(config, destino, geocoding, rotas, ct,
            cotacaoExterna is null ? null : new CotacaoExternaDaLoja(cotacaoExterna, loja.EmpresaId, loja.TituloPublico));
        if (cotacao is null)
            return FreteRaioTesteResult.Zona("Não localizamos este endereço com precisão. No pedido, vale a tabela de zonas.");

        var r = cotacao.Resultado;
        return new FreteRaioTesteResult(
            Modo: "raio",
            DistanciaMetros: r.DistanciaMetros,
            DistanciaRotaMetros: r.DistanciaRotaMetros,
            DistanciaEstimada: cotacao.Rota is null,
            FaixaId: cotacao.Externa?.Provedor ?? r.FaixaId,
            ValorCentavos: r.ForaDeCobertura ? null : cotacao.ValorCobradoCentavos,
            Gratis: r.Gratis,
            ForaDeCobertura: r.ForaDeCobertura,
            Motivo: null);
    }
}
