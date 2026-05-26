namespace EasyStock.Application.Ports.Output.Lookup;

/// <summary>
/// Adapter para serviço externo de consulta de CEP → endereço (ViaCEP em prod,
/// NoOp em dev ou quando a feature está desligada).
///
/// <para>
/// Usado pelo <c>CalcularFreteUseCase</c> para enriquecer o CEP com o bairro
/// — útil quando a zona de frete é modelada por lista de bairros e o cliente
/// só informou o CEP. <strong>Best-effort</strong>: falha do provider
/// (timeout, 5xx, CEP não encontrado) NÃO derruba o checkout — retorna
/// <see langword="null"/> e o use case segue com bairro vazio.
/// </para>
///
/// <para>
/// <strong>Timeout</strong>: implementações HTTP devem ter cap próprio de 1s
/// para não bloquear a request de frete (que precisa devolver em &lt;500ms).
/// </para>
/// </summary>
public interface ICepLookupClient
{
    /// <summary>
    /// Consulta o CEP no provider externo. Retorna <see langword="null"/>
    /// quando o CEP não existe, o provider está indisponível, ou houve timeout.
    /// Nunca lança — encapsula falhas internamente.
    /// </summary>
    /// <param name="cep">CEP já normalizado (8 dígitos, sem máscara).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<CepLookupResult?> LookupAsync(string cep, CancellationToken ct = default);
}

/// <summary>
/// Resultado parcial da consulta de CEP. Todos os campos são best-effort —
/// provider pode devolver alguns vazios. Apenas <see cref="Bairro"/> é usado
/// pelo use case de frete; demais ficam disponíveis para iterações futuras.
/// </summary>
public sealed record CepLookupResult(
    string Cep,
    string? Logradouro,
    string? Bairro,
    string? Cidade,
    string? Uf);
