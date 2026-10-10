using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Monta a saudação que o webhook envia antes do agente na primeira mensagem de uma conversa nova
/// (S05, RN-01): texto de <see cref="ConfiguracaoAtendimento"/>, link do cardápio e frase de espera,
/// numa única mensagem. Sem LLM, para caber nos 5 s.
/// </summary>
public sealed class SaudacaoAtendimento(IStorefrontRepository storefrontRepository, IConfiguration configuration)
{
    /// <summary><c>App:BaseUrl</c> é a vitrine pública.</summary>
    public const string BaseUrlPadrao = "https://casadababa.com";

    public const string CaminhoCardapio = "/cardapio";

    public async Task<string> MontarAsync(
        Guid empresaId, ConfiguracaoAtendimento configuracao, IdentificacaoCliente identificacao, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(identificacao);

        var link = await ResolverLinkCardapioAsync(empresaId, ct);
        var primeiroNome = identificacao.EhNovo ? null : PrimeiroNome(identificacao.Cliente.Nome);
        return Montar(configuracao, primeiroNome, link);
    }

    /// <summary>
    /// Cliente conhecido e com nome → <see cref="ConfiguracaoAtendimento.SaudacaoRetorno"/> com <c>{nome}</c>;
    /// senão <see cref="ConfiguracaoAtendimento.SaudacaoPrimeiroContato"/>. Texto configurado sem
    /// <c>{link}</c> recebe o link numa linha própria: o cardápio sempre vai.
    /// </summary>
    public static string Montar(ConfiguracaoAtendimento configuracao, string? primeiroNome, string linkCardapio)
    {
        var modelo = string.IsNullOrWhiteSpace(primeiroNome)
            ? configuracao.SaudacaoPrimeiroContato
            : configuracao.SaudacaoRetorno;

        var saudacao = modelo.Replace("{nome}", primeiroNome ?? string.Empty, StringComparison.Ordinal);
        saudacao = saudacao.Contains("{link}", StringComparison.Ordinal)
            ? saudacao.Replace("{link}", linkCardapio, StringComparison.Ordinal)
            : $"{saudacao}\n{linkCardapio}";

        return string.IsNullOrWhiteSpace(configuracao.FraseEspera)
            ? saudacao
            : $"{saudacao}\n\n{configuracao.FraseEspera}";
    }

    /// <summary>Link público do cardápio; também é a legenda de <c>enviar_cardapio_imagem</c> (S06).</summary>
    public async Task<string> ResolverLinkCardapioAsync(Guid empresaId, CancellationToken ct = default)
    {
        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        var baseUrl = !string.IsNullOrWhiteSpace(storefront?.DominioCustom)
            ? $"https://{storefront.DominioCustom.Trim().TrimEnd('/')}"
            : configuration["App:BaseUrl"]?.TrimEnd('/') ?? BaseUrlPadrao;
        return baseUrl + CaminhoCardapio;
    }

    private static string? PrimeiroNome(string? nome)
    {
        // Lead criado sem nome de perfil não é saudado como "Cliente".
        if (string.IsNullOrWhiteSpace(nome) || nome == IdentificarClientePorTelefoneUseCase.NomePadraoLead) return null;
        return nome.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    }
}
