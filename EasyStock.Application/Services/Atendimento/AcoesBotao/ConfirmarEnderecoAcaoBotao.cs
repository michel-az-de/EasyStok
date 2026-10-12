using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento.AcoesBotao;

/// <summary>
/// <c>acao:confirmar_endereco:&lt;ref&gt;</c> (S14): grava como padrão do cliente o endereço que
/// <c>validar_endereco</c> deixou pendente na conversa e registra uma nota de sistema. Sem cliente vinculado ou
/// sem endereço pendente, a conversa vai para a dona, como antes. O commit é de quem chama o roteador.
/// </summary>
public sealed class ConfirmarEnderecoAcaoBotao(
    IEscaladorConversa escalador,
    ConfirmarEnderecoPendente confirmar,
    IConversaRepository conversaRepository) : IAcaoBotaoHandler
{
    public string Nome => "confirmar_endereco";

    public async Task ExecutarAsync(Guid empresaId, Conversa conversa, string payload, DateTime agora, CancellationToken ct = default)
    {
        var r = await confirmar.ExecutarAsync(empresaId, conversa, ct);
        if (r.EnderecoId is null || r.Endereco is not { } e)
        {
            await escalador.EscalarAsync(empresaId, conversa,
                $"cliente confirmou o endereço {payload} pelo botão, mas não foi possível gravar ({r.Erro})", agora, ct);
            return;
        }

        await conversaRepository.AddMensagemAsync(
            Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Sistema, agora, TipoConteudoMensagem.Texto,
                $"endereço confirmado pelo cliente: {e.Logradouro}, {e.Numero} - {e.Bairro}, {e.Cidade} ({e.Cep})"),
            ct);
    }
}
