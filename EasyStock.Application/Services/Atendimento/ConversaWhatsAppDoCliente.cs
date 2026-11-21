using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// A conversa do WhatsApp da loja com um cliente cadastrado: a de quem nunca escreveu (#1408) e a do pedido que a equipe
/// registra pelo Console quando o cliente falou no WhatsApp pessoal da dona (#2871).
///
/// <list type="number">
/// <item>Cliente da empresa, ativo, não bloqueado, com celular BR no cadastro: o contato é o E.164 sem o <c>+</c>.</item>
/// <item>A conversa aberta do contato (as duas grafias do nono dígito, #1290) é reaproveitada; sem cliente, ganha o
///   vínculo; de outro cliente, é recusada. Sem conversa aberta, abre uma, sem automática de primeira entrada.</item>
/// </list>
/// </summary>
public sealed class ConversaWhatsAppDoCliente(IConversaRepository conversas, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    private const string SemCelular = "O cliente não tem celular válido no cadastro. Corrija o telefone na Ficha.";

    /// <summary>Contato WhatsApp do cliente apto a receber mensagem da loja; recusa com a orientação da Ficha.</summary>
    public static string ContatoDoCliente(ClienteEntity? cliente, Guid empresaId)
    {
        if (cliente is null || cliente.EmpresaId != empresaId)
            throw new UseCaseValidationException("Cliente não encontrado nesta empresa.");
        if (!cliente.Ativo) throw new UseCaseValidationException("Cliente desativado: reative o cadastro para chamar no WhatsApp.");
        if (cliente.Bloqueado) throw new RegraDeDominioVioladaException("Cliente bloqueado: desbloqueie na Ficha para chamar no WhatsApp.");

        string e164;
        try
        {
            e164 = NormalizadorTelefone.NormalizarE164Br(cliente.Telefone ?? string.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new UseCaseValidationException(SemCelular);
        }
        if (!NormalizadorTelefone.EhCelularBr(e164)) throw new UseCaseValidationException(SemCelular);
        return Conversa.NormalizarContato(CanalConversa.WhatsApp, e164);
    }

    /// <summary>
    /// A conversa aberta do contato, travada, ou uma nova, já gravada. Duas atendentes (ou o cliente escrevendo) no mesmo instante: o
    /// índice único de conversa aberta recusa a segunda gravação, e a segunda volta uma vez e segue na conversa que ficou.
    /// <paramref name="assumidaAoCriarPor"/>: quem atende pessoalmente; a conversa CRIADA nasce Assumida por ele (#2902), a reaproveitada não muda.
    /// </summary>
    public async Task<Conversa> AbrirOuReaproveitarAsync(
        Guid empresaId, ClienteEntity cliente, string contato, CancellationToken ct, Guid? assumidaAoCriarPor = null)
    {
        try
        {
            return await AbrirOuReaproveitarUmaVezAsync(empresaId, cliente, contato, assumidaAoCriarPor, ct);
        }
        catch (Exception ex) when (unitOfWork.EhViolacaoDeUnicidade(ex))
        {
            unitOfWork.DescartarAlteracoesPendentes();
            return await AbrirOuReaproveitarUmaVezAsync(empresaId, cliente, contato, assumidaAoCriarPor, ct);
        }
    }

    private Task<Conversa> AbrirOuReaproveitarUmaVezAsync(
        Guid empresaId, ClienteEntity cliente, string contato, Guid? assumidaAoCriarPor, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
        {
            var agora = relogio.GetUtcNow().UtcDateTime;
            var conversa = await conversas.ObterAbertaPorContatoComLockAsync(empresaId, CanalConversa.WhatsApp, contato, token);
            if (conversa is null)
            {
                conversa = Conversa.Abrir(empresaId, contato, agora, cliente.Nome, cliente.Id);
                // #2902: só a conversa criada aqui nasce com a dona; a que já existia segue com quem a atendia.
                if (assumidaAoCriarPor is { } usuario) conversa.Assumir(agora, usuario);
                await conversas.AddAsync(conversa, token);
            }
            else if (conversa.ClienteId is null)
            {
                conversa.VincularCliente(cliente.Id);
            }
            else if (conversa.ClienteId != cliente.Id)
            {
                throw new RegraDeDominioVioladaException(
                    "Este telefone está numa conversa aberta de outro cliente. Confira o cadastro na Ficha.");
            }

            await unitOfWork.CommitAsync();
            return conversa;
        }, ct);
}
