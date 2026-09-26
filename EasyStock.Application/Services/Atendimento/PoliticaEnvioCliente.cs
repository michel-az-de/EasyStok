using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Porta de entrada da regra de consentimento (S38) para quem envia ao cliente final por iniciativa
/// própria: mensagem programada (S39) e campanhas (S28–S31).
/// </summary>
public sealed class PoliticaEnvioCliente(IConsentimentoContatoRepository repository)
{
    public async Task<bool> PodeEnviarAsync(
        Guid empresaId, Guid clienteId, CanalConversa canal, FinalidadeContato finalidade, CancellationToken ct = default)
    {
        var consentimentos = await repository.ListarDoClienteAsync(empresaId, clienteId, ct);
        return PoliticaConsentimento.PodeEnviar(consentimentos, canal, finalidade);
    }
}
