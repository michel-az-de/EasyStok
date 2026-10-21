using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record EnviarTextoConsoleCommand(Guid EmpresaId, Guid UsuarioId, Guid ConversaId, string Texto);

public sealed record EnviarImagemConsoleCommand(
    Guid EmpresaId, Guid UsuarioId, Guid ConversaId, string FileName, string ContentType, byte[] Conteudo, string? Legenda);

/// <summary>Áudio gravado no console (#1444): os bytes como vieram do navegador; o formato sai dos bytes.</summary>
public sealed record EnviarAudioConsoleCommand(Guid EmpresaId, Guid UsuarioId, Guid ConversaId, byte[] Conteudo);

/// <summary>
/// A dona responde pelo console (S07, RN-04, D4): envia pela porta do canal da conversa (S34), grava
/// <c>Mensagem(Saida, Dona)</c> com o id externo e marca a conversa <see cref="SituacaoConversa.Assumida"/>,
/// o que cala o agente até ela liberar o automático.
///
/// <para>
/// Fora da janela de atendimento (domínio recusa, ou a Meta devolve 131047) nada é gravado como enviado e a
/// conversa não muda: <see cref="ForaDaJanelaAtendimentoException"/>. Qualquer outra falha do canal grava a
/// mensagem como falhou (#1396) e devolve o id em <see cref="FalhaEnvioCanalException.Mensagem"/>: a dona reenvia.
/// </para>
/// </summary>
public sealed class EnviarMensagemConsoleUseCase(
    IConversaRepository conversaRepository,
    ResolvedorCanal resolvedorCanal,
    GerenciarUploadsUseCase uploads,
    IUnitOfWork unitOfWork)
{
    /// <summary>Código da Cloud API para mensagem fora da janela de 24 h sem modelo.</summary>
    public const int CodigoMetaForaDaJanela = WhatsAppCloudException.CodigoForaDaJanela;

    public async Task<MensagemAtendimentoResult> EnviarTextoAsync(EnviarTextoConsoleCommand command, CancellationToken ct = default)
    {
        var texto = command.Texto?.Trim();
        if (string.IsNullOrEmpty(texto))
            throw new UseCaseValidationException("Texto da mensagem é obrigatório.");
        if (texto.Length > Mensagem.TextoTamanhoMaximo)
            throw new UseCaseValidationException($"Texto excede {Mensagem.TextoTamanhoMaximo} caracteres.");

        var agora = DateTime.UtcNow;
        var conversa = await ObterAbertaAsync(command.EmpresaId, command.ConversaId, ct);
        var canal = resolvedorCanal.Obter(conversa.Canal);

        // Fora da janela, Instagram e Messenger aceitam resposta humana com HUMAN_AGENT (S35). O console
        // é humano; o domínio confere se a tag vale e se ainda está no prazo dela.
        var tag = TagHumanaForaDaJanela(conversa, canal, agora);
        GarantirJanela(conversa, agora, tag);

        return await EnviarERegistrarAsync(conversa, command.UsuarioId, agora,
            token => tag is not null && canal is ICanalComTagHumana comTag
                ? comTag.EnviarTextoComTagAsync(conversa.ContatoIdExterno, texto, tag, token)
                : canal.EnviarTextoAsync(conversa.ContatoIdExterno, texto, token),
            externoId => Mensagem.Saida(command.EmpresaId, conversa.Id, AutorMensagem.Dona, agora,
                TipoConteudoMensagem.Texto, texto, externoId));
    }

    public async Task<MensagemAtendimentoResult> EnviarImagemAsync(EnviarImagemConsoleCommand command, CancellationToken ct = default)
    {
        var legenda = string.IsNullOrWhiteSpace(command.Legenda) ? null : command.Legenda.Trim();
        if (legenda is { Length: > Mensagem.TextoTamanhoMaximo })
            throw new UseCaseValidationException($"Legenda excede {Mensagem.TextoTamanhoMaximo} caracteres.");

        var agora = DateTime.UtcNow;
        var conversa = await ObterParaEnvioAsync(command.EmpresaId, command.ConversaId, agora, ct);
        if (!conversa.Capacidades.AceitaImagem)
            throw new UseCaseValidationException($"O canal {conversa.Canal} não aceita imagem.");
        var canal = resolvedorCanal.Obter(conversa.Canal);

        var imagem = await uploads.UploadImagemAtendimentoAsync(
            command.EmpresaId, conversa.Id, command.FileName, command.ContentType, command.Conteudo, ct);

        return await EnviarERegistrarAsync(conversa, command.UsuarioId, agora,
            token => canal.EnviarImagemAsync(conversa.ContatoIdExterno, imagem.Url, legenda, token),
            externoId =>
            {
                var mensagem = Mensagem.Saida(command.EmpresaId, conversa.Id, AutorMensagem.Dona, agora,
                    TipoConteudoMensagem.Imagem, legenda, externoId);
                mensagem.AnexarMidia(imagem.StorageKey, imagem.ContentType);
                return mensagem;
            });
    }

    /// <summary>
    /// Áudio gravado pela dona (#1444). Só em canal que aceita áudio (domínio) e cujo adaptador já envia
    /// (<see cref="ICanalComAudio"/>); a conversão e a validação do formato vêm antes de qualquer envio.
    /// </summary>
    public async Task<MensagemAtendimentoResult> EnviarAudioAsync(EnviarAudioConsoleCommand command, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var conversa = await ObterParaEnvioAsync(command.EmpresaId, command.ConversaId, agora, ct);
        if (!conversa.Capacidades.AceitaAudio || resolvedorCanal.Obter(conversa.Canal) is not ICanalComAudio canal)
            throw new UseCaseValidationException($"O canal {conversa.Canal} ainda não envia áudio pelo EasyStok.");

        var audio = await uploads.UploadAudioAtendimentoAsync(command.EmpresaId, conversa.Id, command.Conteudo, ct);

        return await EnviarERegistrarAsync(conversa, command.UsuarioId, agora,
            token => canal.EnviarAudioAsync(conversa.ContatoIdExterno, audio.Url, audio.NotaDeVoz, token),
            externoId =>
            {
                var mensagem = Mensagem.Saida(command.EmpresaId, conversa.Id, AutorMensagem.Dona, agora,
                    TipoConteudoMensagem.Audio, null, externoId);
                mensagem.AnexarMidia(audio.StorageKey, audio.ContentType);
                return mensagem;
            });
    }

    private async Task<Conversa> ObterParaEnvioAsync(Guid empresaId, Guid conversaId, DateTime agora, CancellationToken ct)
    {
        var conversa = await ObterAbertaAsync(empresaId, conversaId, ct);
        GarantirJanela(conversa, agora, tag: null);
        return conversa;
    }

    private async Task<Conversa> ObterAbertaAsync(Guid empresaId, Guid conversaId, CancellationToken ct)
    {
        var conversa = await conversaRepository.ObterPorIdAsync(empresaId, conversaId, ct)
            ?? throw new ConversaNaoEncontradaException(conversaId);
        if (!conversa.EstaAberta)
            throw new RegraDeDominioVioladaException("Conversa encerrada: a próxima mensagem do cliente abre outra.");
        return conversa;
    }

    private static void GarantirJanela(Conversa conversa, DateTime agora, string? tag)
    {
        try
        {
            conversa.GarantirPodeEnviarTextoLivre(agora, tag);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new ForaDaJanelaAtendimentoException(ex.Message, ex);
        }
    }

    private static string? TagHumanaForaDaJanela(Conversa conversa, ICanalMensageria canal, DateTime agora) =>
        !conversa.DentroDaJanela(agora)
        && canal is ICanalComTagHumana
        && conversa.Capacidades.TagsForaDaJanela.Contains(CapacidadesCanal.TagAgenteHumano, StringComparer.Ordinal)
            ? CapacidadesCanal.TagAgenteHumano
            : null;

    /// <summary>
    /// Envia e grava. Na falha do canal (#1396) grava a mensagem <see cref="StatusMensagem.Falhou"/> com o erro
    /// (temporária agenda o reenvio, S57) e devolve o id real na exceção, para o botão Reenviar do console.
    /// #1787: daqui em diante o cancelamento do request não vale. A Meta pode ter entregue mesmo com a aba
    /// fechada, e sem o registro a conversa não fica Assumida e o agente segue respondendo (RN-04).
    /// </summary>
    private async Task<MensagemAtendimentoResult> EnviarERegistrarAsync(
        Conversa conversa, Guid usuarioId, DateTime agora, Func<CancellationToken, Task<string>> envio,
        Func<string?, Mensagem> criarMensagem)
    {
        var ct = CancellationToken.None;
        string externoId;
        try
        {
            externoId = await envio(ct);
        }
        catch (WhatsAppCloudException ex) when (ex.Codigo == CodigoMetaForaDaJanela)
        {
            throw new ForaDaJanelaAtendimentoException("A Meta recusou: fora da janela de 24 h.", ex);
        }
        catch (Exception ex)
        {
            var falhou = criarMensagem(null);
            falhou.RegistrarFalhaEnvio(ex.Message, ClassificadorFalhaEnvio.Classificar(ex), agora);
            var gravada = await RegistrarAsync(conversa, falhou, usuarioId, agora, ct);
            // #1912: o detalhe leva o erro gravado; na incerta ele diz "Envio incerto", que o console mostra como "pode ter chegado".
            throw new FalhaEnvioCanalException($"Falha ao enviar pelo canal: {falhou.Erro}", ex, gravada);
        }

        return await RegistrarAsync(conversa, criarMensagem(externoId), usuarioId, agora, ct);
    }

    /// <summary>
    /// #1792: a conversa é relida sob trava depois do envio. Encerrada no meio do envio, a mensagem fica no histórico
    /// (ela saiu) e a conversa continua encerrada; sem a releitura, assumir gravava <c>Assumida</c> com <c>EncerradaEm</c>.
    /// </summary>
    private Task<MensagemAtendimentoResult> RegistrarAsync(
        Conversa conversa, Mensagem mensagem, Guid usuarioId, DateTime agora, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
        {
            var atual = await conversaRepository.ObterPorIdComLockAsync(conversa.EmpresaId, conversa.Id, token)
                ?? throw new ConversaNaoEncontradaException(conversa.Id);
            mensagem.RegistrarEnviadaPor(usuarioId);
            if (atual.EstaAberta)
            {
                atual.Assumir(agora, usuarioId);
                atual.RegistrarSaida(agora);
            }
            await conversaRepository.AddMensagemAsync(mensagem, token);
            await unitOfWork.CommitAsync();
            return MensagemAtendimentoResult.De(mensagem);
        }, ct);
}
