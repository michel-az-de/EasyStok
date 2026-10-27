using EasyStock.Domain.Services;

namespace EasyStock.Application.UseCases.PrimeirosPassos;

/// <summary>
/// <c>PUT api/configuracao-inicial/passos/{passo}</c> (#2070): marca o passo como conferido ou para depois, por upsert.
/// Só marca; nunca grava configuração. Qualquer administrador da empresa marca.
/// </summary>
public sealed class MarcarPassoConfiguracaoInicialUseCase(
    IConfiguracaoInicialRepository passos,
    ICurrentUserAccessor usuario,
    TimeProvider relogio)
{
    public const string CodigoPassoDesconhecido = "PASSO_DESCONHECIDO";
    public const string CodigoSituacaoInvalida = "SITUACAO_INVALIDA";

    public async Task ExecuteAsync(string passo, string? situacao, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(usuario.EmpresaId);
        if (ConfiguracaoInicial.Encontrar(passo) is null)
            throw new UseCaseValidationException(CodigoPassoDesconhecido, $"Passo desconhecido: '{passo}'.");
        var lida = SituacoesDoPasso.Ler(situacao)
            ?? throw new UseCaseValidationException(CodigoSituacaoInvalida,
                $"Situação inválida: use '{SituacoesDoPasso.TextoConferido}' ou '{SituacoesDoPasso.TextoDepois}'.");

        var marca = PassoConfiguracaoInicial.Marcar(
            usuario.EmpresaId, passo, lida, usuario.UsuarioId, relogio.GetUtcNow().UtcDateTime);
        await passos.MarcarAsync(marca, ct);
    }
}
