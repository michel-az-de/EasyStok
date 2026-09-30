using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Endereco;

/// <summary>Endereço como o agente extraiu do texto livre do cliente (S14). Só o CEP é obrigatório.</summary>
public sealed record ValidarEnderecoInput(
    Guid EmpresaId,
    string? Cep,
    string? Logradouro = null,
    string? Numero = null,
    string? Complemento = null,
    string? Bairro = null,
    string? Cidade = null,
    string? Uf = null,
    string? Referencia = null);

/// <summary>Endereço normalizado: CEP em 8 dígitos e, quando o ViaCEP responde, logradouro, bairro e cidade dele.</summary>
public sealed record EnderecoNormalizado(
    string Cep,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cidade,
    string? Uf,
    string? Referencia)
{
    /// <summary>Tem o mínimo para entregar: CEP, logradouro, número, bairro e cidade.</summary>
    public bool Completo =>
        Cep.Length == 8 && !string.IsNullOrWhiteSpace(Logradouro) && !string.IsNullOrWhiteSpace(Numero)
        && !string.IsNullOrWhiteSpace(Bairro) && !string.IsNullOrWhiteSpace(Cidade);
}

/// <summary>
/// Veredito do endereço. <see cref="Motivo"/>: <c>fora_area</c>, <c>cep_invalido</c> ou <c>sem_loja</c>;
/// nulo quando está dentro da área.
/// </summary>
public sealed record ValidarEnderecoResult(
    bool DentroDaArea,
    EnderecoNormalizado EnderecoNormalizado,
    decimal? TaxaEntrega,
    int? TempoEstimadoMin,
    string? Motivo,
    string? MensagemForaArea);

/// <summary>
/// S14 (US-011..013, RN-09..11): decide se o endereço está na área de entrega. A extração dos campos é
/// do LLM; aqui só normaliza e decide. ViaCEP completa os campos (best-effort: indisponível não bloqueia)
/// e o <see cref="CalcularFreteUseCase"/> decide zona/raio, taxa e tempo. Fora da área nunca lança:
/// devolve <c>DentroDaArea=false</c> com a mensagem configurada da loja.
/// </summary>
public sealed class ValidarEnderecoUseCase(
    IStorefrontRepository storefrontRepository,
    CalcularFreteUseCase calcularFrete,
    ICepLookupClient cepLookupClient,
    IConfiguracaoAtendimentoRepository configuracaoRepository,
    ILogger<ValidarEnderecoUseCase> logger)
{
    public const string MotivoForaArea = "fora_area";
    public const string MotivoCepInvalido = "cep_invalido";
    public const string MotivoSemLoja = "sem_loja";

    public async Task<ValidarEnderecoResult> ExecuteAsync(ValidarEnderecoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);

        var cep = new string((input.Cep ?? string.Empty).Where(char.IsDigit).ToArray());
        if (cep.Length != 8)
            return Fora(Normalizar(input, cep, null), MotivoCepInvalido, null);

        var endereco = Normalizar(input, cep, await ConsultarCepAsync(cep, ct));

        var storefront = await storefrontRepository.GetByEmpresaAsync(input.EmpresaId, ct);
        if (storefront is null || !storefront.Ativo)
            return Fora(endereco, MotivoSemLoja, null);

        try
        {
            var frete = await calcularFrete.ExecuteAsync(new CalcularFreteInput(storefront.Slug, cep, endereco.Numero), ct);
            return new ValidarEnderecoResult(true, endereco, frete.Valor / 100m, frete.TempoEstimadoMinutos, null, null);
        }
        catch (CepSemCoberturaException)
        {
            var configuracao = await configuracaoRepository.GetOrDefaultAsync(input.EmpresaId);
            return Fora(endereco, MotivoForaArea, configuracao.MensagemForaArea);
        }
    }

    private async Task<CepLookupResult?> ConsultarCepAsync(string cep, CancellationToken ct)
    {
        try
        {
            return await cepLookupClient.LookupAsync(cep, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Consulta de CEP falhou; seguindo só com o que o cliente informou. cep={Cep}", cep);
            return null;
        }
    }

    private static EnderecoNormalizado Normalizar(ValidarEnderecoInput input, string cep, CepLookupResult? viaCep) => new(
        cep,
        Primeiro(viaCep?.Logradouro, input.Logradouro),
        Limpo(input.Numero),
        Limpo(input.Complemento),
        Primeiro(viaCep?.Bairro, input.Bairro),
        Primeiro(viaCep?.Cidade, input.Cidade),
        Primeiro(viaCep?.Uf, input.Uf)?.ToUpperInvariant(),
        Limpo(input.Referencia));

    private static string? Primeiro(string? preferido, string? alternativo) => Limpo(preferido) ?? Limpo(alternativo);

    private static string? Limpo(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static ValidarEnderecoResult Fora(EnderecoNormalizado endereco, string motivo, string? mensagem) =>
        new(false, endereco, null, null, motivo, mensagem);
}
