namespace EasyStock.Domain.Integration;

/// <summary>
/// Ambiente de execução da integração. Sandbox e Production têm credenciais
/// distintas e endpoints distintos — a tabela <c>credencial_integracao</c>
/// guarda chaves separadas por ambiente.
/// </summary>
public enum AmbienteIntegracao
{
    /// <summary>Sandbox / homologação. Não realiza efeitos reais.</summary>
    Sandbox = 1,

    /// <summary>Produção. Cobra cartão real, emite NFe real, etc.</summary>
    Production = 2,
}
