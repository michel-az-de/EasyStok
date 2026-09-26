using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record EnviarTextoConsoleCommand(Guid EmpresaId, Guid UsuarioId, Guid ConversaId, string Texto);

public sealed record EnviarImagemConsoleCommand(
    Guid EmpresaId, Guid UsuarioId, Guid ConversaId, string FileName, string ContentType, byte[] Conteudo, string? Legenda);

/// <summary>
/// A dona responde pelo console (S07, RN-04, D4): envia pela porta do canal da conversa (S34), grava
/// <c>Mensagem(Saida, Dona)</c> com o id externo e marca a conversa <see cref="SituacaoConversa.Assumida"/>,
/// o que cala o agente até ela liberar o automático.
///
/// <para>
/// Fora da janela de atendimento (domínio recusa, ou a Meta devolve 131047) nada é gravado como enviado e a
/// conversa não muda: <see cref="ForaDaJanelaAtendimentoException"/>. Qualquer outra falha do canal também
/// não grava nada (<see cref="FalhaEnvioCanalException"/>): a dona tenta de novo.
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
        var conversa = await ObterParaEnvioAsync(command.EmpresaId, command.ConversaId, agora, ct);
        var canal = resolvedorCanal.Obter(conversa.Canal);

        var externoId = await EnviarAsync(() => canal.EnviarTextoAsync(conversa.ContatoIdExterno, texto, ct));

        var mensagem = Mensagem.Saida(command.EmpresaId, conversa.Id, AutorMensagem.Dona, agora,
            TipoConteudoMensagem.Texto, texto, externoId);
        return await RegistrarAsync(conversa, mensagem, command.UsuarioId, agora, ct);
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

        var externoId = await EnviarAsync(() => canal.EnviarImagemAsync(conversa.ContatoIdExterno, imagem.Url, legenda, ct));

        var mensagem = Mensagem.Saida(command.EmpresaId, conversa.Id, AutorMensagem.Dona, agora,
            TipoConteudoMensagem.Imagem, legenda, externoId);
        mensagem.AnexarMidia(imagem.StorageKey, imagem.ContentType);
        return await RegistrarAsync(conversa, mensagem, command.UsuarioId, agora, ct);
    }

    private async Task<Conversa> ObterParaEnvioAsync(Guid empresaId, Guid conversaId, DateTime agora, CancellationToken ct)
    {
        var conversa = await conversaRepository.ObterPorIdAsync(empresaId, conversaId, ct)
            ?? throw new ConversaNaoEncontradaException(conversaId);
        if (!conversa.EstaAberta)
            throw new RegraDeDominioVioladaException("Conversa encerrada: a próxima mensagem do cliente abre outra.");

        try
        {
            conversa.GarantirPodeEnviarTextoLivre(agora);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new ForaDaJanelaAtendimentoException(ex.Message, ex);
        }

        return conversa;
    }

    private static async Task<string> EnviarAsync(Func<Task<string>> envio)
    {
        try
        {
            return await envio();
        }
        catch (WhatsAppCloudException ex) when (ex.Codigo == CodigoMetaForaDaJanela)
        {
            throw new ForaDaJanelaAtendimentoException("A Meta recusou: fora da janela de 24 h.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new FalhaEnvioCanalException($"Falha ao enviar pelo canal: {ex.Message}", ex);
        }
    }

    private async Task<MensagemAtendimentoResult> RegistrarAsync(
        Conversa conversa, Mensagem mensagem, Guid usuarioId, DateTime agora, CancellationToken ct)
    {
        conversa.Assumir(agora, usuarioId);
        conversa.RegistrarSaida(agora);
        await conversaRepository.AddMensagemAsync(mensagem, ct);
        await unitOfWork.CommitAsync();
        return MensagemAtendimentoResult.De(mensagem);
    }
}
