namespace EasyStock.Application.Ports.Output.Integration.Conexao;

/// <summary>Último teste de uma chave global, por empresa e provider.</summary>
public sealed record EstadoTesteIntegracao(DateTime TestadoEm, bool Ok, string Mensagem);

/// <summary>
/// Último teste das chaves GLOBAIS (F16, #1246). A chave da loja guarda o resultado na própria
/// linha de <c>credencial_integracao</c>; a global não tem linha por loja, então o resultado fica
/// na memória do processo. Depois de um restart o vigia repõe em até 15 min.
/// </summary>
public interface IEstadoTesteIntegracaoStore
{
    EstadoTesteIntegracao? Obter(Guid empresaId, string provider);

    void Registrar(Guid empresaId, string provider, EstadoTesteIntegracao estado);
}
