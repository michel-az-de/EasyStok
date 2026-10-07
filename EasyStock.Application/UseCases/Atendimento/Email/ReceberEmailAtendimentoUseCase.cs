using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Atendimento.Email;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Email;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Email;

public enum DesfechoEmailRecebido
{
    /// <summary>Virou mensagem na conversa.</summary>
    Gravado,

    /// <summary>O Message-ID já estava gravado (outra rodada, outro processo).</summary>
    Duplicado,

    /// <summary>Resposta automática, e-mail da própria caixa ou remetente sem endereço: não vira conversa.</summary>
    Ignorado,
}

/// <summary>
/// Um e-mail da caixa de suporte vira mensagem de entrada na conversa do canal E-mail (#1432). Acha a conversa
/// aberta do remetente ou abre outra na fila humana sem responsável (no e-mail não há agente), já ligada ao
/// cliente que tem o mesmo e-mail. Guarda o assunto, o texto sem o histórico citado e cada anexo como uma
/// mensagem com a mídia no storage privado (mesmo lugar da mídia do WhatsApp). Tudo num commit só: o
/// Message-ID único por empresa impede o mesmo e-mail de entrar duas vezes.
/// <para>
/// Quem chama marca o e-mail como lido só depois deste caso de uso voltar sem erro. Falha de banco ou de
/// storage lança: o e-mail continua não lido e entra de novo na próxima rodada.
/// </para>
/// </summary>
public sealed class ReceberEmailAtendimentoUseCase(
    IConversaRepository conversaRepository,
    IEmailAtendimentoQuery emailQuery,
    IFileStorage fileStorage,
    IOperacaoEventPublisher eventPublisher,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<ReceberEmailAtendimentoUseCase> logger)
{
    /// <summary>Data do cabeçalho mais no futuro do que isto é relógio errado do remetente: vale a hora da leitura.</summary>
    public static readonly TimeSpan ToleranciaRelogio = TimeSpan.FromMinutes(5);

    public const string TextoSemCorpo = "[e-mail sem texto]";

    public async Task<DesfechoEmailRecebido> ExecuteAsync(
        Guid empresaId, CaixaEmailAtendimento caixa, EmailRecebido email, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caixa);
        ArgumentNullException.ThrowIfNull(email);

        if (email.AutoGerado)
            return DesfechoEmailRecebido.Ignorado;

        string contato;
        try
        {
            contato = Conversa.NormalizarContato(CanalConversa.Email, email.DeEndereco);
        }
        catch (RegraDeDominioVioladaException)
        {
            return DesfechoEmailRecebido.Ignorado;
        }

        // A caixa escrevendo para ela mesma (cópia, encaminhamento interno) não é cliente.
        if (string.Equals(contato, caixa.Endereco, StringComparison.OrdinalIgnoreCase))
            return DesfechoEmailRecebido.Ignorado;

        var agora = relogio.GetUtcNow().UtcDateTime;
        var recebidoEm = email.RecebidoEm == default || email.RecebidoEm > agora + ToleranciaRelogio ? agora : email.RecebidoEm;
        var texto = CorpoEmail.Limpar(email.Texto) ?? Mensagem.NormalizarAssunto(email.Assunto) ?? TextoSemCorpo;
        var externoId = IdExternoEmail.Principal(email.MessageId, contato, email.IdNaCaixa, email.Assunto, email.Texto);

        if (await conversaRepository.ObterMensagemPorExternoIdAsync(empresaId, externoId, ct) is not null)
            return DesfechoEmailRecebido.Duplicado;

        Conversa conversa;
        Mensagem principal;
        try
        {
            conversa = await conversaRepository.ObterAbertaPorContatoAsync(empresaId, CanalConversa.Email, contato, ct)
                ?? await AbrirAsync(empresaId, contato, email.DeNome, recebidoEm, ct);

            conversa.DefinirAssunto(AssuntoEmail.SemPrefixos(email.Assunto));
            principal = Mensagem.Entrada(empresaId, conversa.Id, recebidoEm, TipoConteudoMensagem.Texto, texto, externoId);
            principal.DefinirAssunto(email.Assunto);
            conversa.RegistrarEntrada(recebidoEm);
            await conversaRepository.AddMensagemAsync(principal, ct);

            for (var i = 0; i < email.Anexos.Count; i++)
                await conversaRepository.AddMensagemAsync(
                    await AnexoAsync(empresaId, conversa.Id, recebidoEm, externoId, i + 1, email.Anexos[i], ct), ct);

            await unitOfWork.CommitAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            unitOfWork.DescartarAlteracoesPendentes();
            // Regra de domínio não passa em outra rodada: ignora para não ler o mesmo e-mail para sempre.
            if (ex is RegraDeDominioVioladaException)
            {
                logger.LogWarning(ex, "E-mail do atendimento recusado pelo domínio na empresa {EmpresaId}; marcado como lido.", empresaId);
                return DesfechoEmailRecebido.Ignorado;
            }
            // Outro processo gravou o mesmo e-mail entre a checagem e o commit. Só conta como repetido se a mensagem
            // está mesmo lá: a outra unicidade (conversa aberta do contato) não gravou nada e precisa de outra rodada.
            if (unitOfWork.EhViolacaoDeUnicidade(ex)
                && await conversaRepository.ObterMensagemPorExternoIdAsync(empresaId, externoId, ct) is not null)
                return DesfechoEmailRecebido.Duplicado;
            throw;
        }

        try
        {
            await eventPublisher.PublicarAsync("conversa.mensagem_recebida", empresaId,
                new { conversaId = conversa.Id, mensagemId = principal.Id }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "E-mail do atendimento: mensagem {MensagemId} gravada, aviso ao console falhou.", principal.Id);
        }

        return DesfechoEmailRecebido.Gravado;
    }

    private async Task<Conversa> AbrirAsync(Guid empresaId, string contato, string? nome, DateTime em, CancellationToken ct)
    {
        var clienteId = await emailQuery.ObterClienteIdPorEmailAsync(empresaId, contato, ct);
        var conversa = Conversa.Abrir(empresaId, contato, em, nome, clienteId, CanalConversa.Email);
        conversa.Assumir(em); // fila humana, sem responsável: no e-mail quem atende é a dona
        await conversaRepository.AddAsync(conversa, ct);
        return conversa;
    }

    /// <summary>
    /// Cada anexo é uma mensagem com o nome do arquivo. Grande demais, de tipo fora da lista ou recusado pelo
    /// storage, fica só o aviso: o resto do e-mail não pode se perder por causa de um anexo.
    /// </summary>
    private async Task<Mensagem> AnexoAsync(
        Guid empresaId, Guid conversaId, DateTime em, string externoPrincipal, int ordem, AnexoEmailRecebido anexo, CancellationToken ct)
    {
        var mime = anexo.Mime.Split(';', 2)[0].Trim().ToLowerInvariant();
        var nome = NomeDoArquivo(anexo.NomeArquivo, ordem);
        var tipo = mime.StartsWith("image/", StringComparison.Ordinal) ? TipoConteudoMensagem.Imagem : TipoConteudoMensagem.Documento;
        var externoId = IdExternoEmail.Anexo(externoPrincipal, ordem);

        string? recusa = null;
        if (anexo.Conteudo is null) recusa = "grande demais";
        else if (!ArmazenadorMidiaWhatsApp.MimesPermitidos.Contains(mime)) recusa = $"tipo {mime} não aceito";

        var mensagem = Mensagem.Entrada(empresaId, conversaId, em, tipo,
            recusa is null ? nome : $"[anexo {nome} não guardado: {recusa}]", externoId);
        if (recusa is not null) return mensagem;

        try
        {
            var guardado = await fileStorage.UploadAsync(new FileUploadRequest(
                $"atendimento/{empresaId}/{conversaId}", $"{mensagem.Id:N}{ArmazenadorMidiaWhatsApp.ExtensaoPara(mime)}",
                mime, anexo.Conteudo!, IsPublic: false, ArmazenadorMidiaWhatsApp.MimesPermitidos), ct);
            mensagem.AnexarMidia(guardado.StorageKey, guardado.ContentType);
            return mensagem;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "E-mail do atendimento: anexo {Ordem} não guardado no storage.", ordem);
            return Mensagem.Entrada(empresaId, conversaId, em, tipo, $"[anexo {nome} não guardado: o storage recusou]", externoId);
        }
    }

    private static string NomeDoArquivo(string? nome, int ordem)
    {
        var limpo = string.IsNullOrWhiteSpace(nome) ? $"anexo {ordem}" : nome.Trim();
        return limpo.Length <= 200 ? limpo : limpo[..200];
    }
}
