using System.Globalization;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Atendimento;

/// <param name="Pix">O código e o valor. O código nunca vai para log nem para texto de falha.</param>
/// <param name="TextoUnico">O aviso de antes (resumo e código num balão), usado com o PDF desligado.</param>
public sealed record CobrancaPixAviso(Guid EmpresaId, Guid ConversaId, Guid PedidoId, PixPdfDto Pix, string TextoUnico);

/// <summary>
/// #2962 (E3): a cobrança Pix na conversa em dois balões. Balão 1: PDF do pedido com legenda curta. Balão 2: só o copia e
/// cola. Uma <see cref="Mensagem"/> por balão.
///
/// <list type="bullet">
///   <item>PDF desligado (<c>Atendimento:Cobranca:EnviarPdf=false</c>) ou sem gerador: o texto único de antes.</item>
///   <item>PDF que não gera, não sobe ou não sai: o balão 1 vai só com a legenda em texto e o balão 2 segue igual.</item>
///   <item>Balão 1 não saiu: nada sai e fica uma falha. Balão 1 saiu e o 2 não: o 1 fica gravado e a falha do 2 também, sem
///     repetir (o canal pode ter aceitado) e sem reenvio automático.</item>
///   <item>O código nunca entra em log, exceção ou <c>Mensagem.Texto</c> de falha: a falha grava um texto fixo e um motivo
///     estável, e é <see cref="TipoFalhaEnvio.Permanente"/> para ninguém reenviar esse texto à cliente.</item>
/// </list>
/// </summary>
public sealed class CobrancaPixConversa(
    IConversaRepository conversaRepository,
    ResolvedorCanal resolvedorCanal,
    IUnitOfWork unitOfWork,
    IPedidoRepository pedidos,
    IStorefrontRepository storefronts,
    IConfiguration configuration,
    ILogger<CobrancaPixConversa> logger,
    PublicadorImpressoPedido? impressos = null)
{
    public const string TextoFalhaResumo = "A cobrança Pix não foi enviada. Gere a cobrança de novo pela Ficha do pedido.";
    public const string TextoFalhaCodigo = "O código Pix não foi enviado. Gere a cobrança de novo pela Ficha do pedido.";

    public async Task<bool> EnviarAsync(CobrancaPixAviso aviso, DateTime agora, Guid? enviadaPorUsuarioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        try
        {
            var conversa = await conversaRepository.ObterPorIdAsync(aviso.EmpresaId, aviso.ConversaId, ct);
            if (conversa is null || !conversa.EstaAberta)
            {
                logger.LogInformation("Cobranca Pix nao enviada conversaId={ConversaId}: conversa ausente ou encerrada", aviso.ConversaId);
                return false;
            }
            if (!conversa.DentroDaJanela(agora))
            {
                await RegistrarFalhaAsync(conversa, TextoFalhaResumo, "Fora da janela de 24 h: o aviso não foi enviado.",
                    agora, enviadaPorUsuarioId, ct);
                return false;
            }

            var canal = resolvedorCanal.Obter(conversa.Canal);
            return impressos is not null && PublicadorDocumentoPedido.PdfLigado(configuration)
                ? await EnviarEmBaloesAsync(aviso, conversa, canal, agora, enviadaPorUsuarioId, ct)
                : await EnviarTextoUnicoAsync(aviso, conversa, canal, agora, enviadaPorUsuarioId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sem a exceção: a mensagem dela vem do canal e o log nunca leva o código.
            logger.LogWarning("Falha ao enviar cobranca Pix conversaId={ConversaId} erro={Erro}", aviso.ConversaId, ex.GetType().Name);
            return false;
        }
    }

    private async Task<bool> EnviarTextoUnicoAsync(
        CobrancaPixAviso aviso, Conversa conversa, ICanalMensageria canal, DateTime agora, Guid? usuarioId, CancellationToken ct)
    {
        string externoId;
        try
        {
            externoId = await canal.EnviarTextoAsync(conversa.ContatoIdExterno, aviso.TextoUnico, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FalharAsync(conversa, TextoFalhaResumo, ex, aviso.ConversaId, agora, usuarioId, ct);
            return false;
        }

        await GravarEnviadaAsync(conversa, TipoConteudoMensagem.Texto, aviso.TextoUnico, externoId, agora, usuarioId, ct);
        await unitOfWork.CommitAsync();
        return true;
    }

    private async Task<bool> EnviarEmBaloesAsync(
        CobrancaPixAviso aviso, Conversa conversa, ICanalMensageria canal, DateTime agora, Guid? usuarioId, CancellationToken ct)
    {
        var resumo = await ResumoAsync(aviso, ct);
        if (resumo is null) return await EnviarTextoUnicoAsync(aviso, conversa, canal, agora, usuarioId, ct);
        var legenda = TextoCobrancaPix.Legenda(resumo, aviso.Pix);
        string? externoImagem = null;
        try
        {
            var url = await impressos!.PublicarPixAsync(aviso.EmpresaId, aviso.PedidoId, aviso.Pix, ct);
            if (url is not null)
                externoImagem = await canal.EnviarImagemAsync(conversa.ContatoIdExterno, url, legenda, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A imagem é o enfeite: sem ela a legenda sai em texto e o código segue (a cliente nunca fica sem o Pix).
            // Sem o corpo do impresso no log: ele leva o código Pix.
            logger.LogWarning("Impresso da cobranca Pix nao saiu conversaId={ConversaId} erro={Erro}", aviso.ConversaId, ex.GetType().Name);
        }

        var tipoDoBalao1 = TipoConteudoMensagem.Imagem;
        if (externoImagem is null)
        {
            tipoDoBalao1 = TipoConteudoMensagem.Texto;
            try
            {
                externoImagem = await canal.EnviarTextoAsync(conversa.ContatoIdExterno, legenda, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await FalharAsync(conversa, TextoFalhaResumo, ex, aviso.ConversaId, agora, usuarioId, ct);
                return false;
            }
        }

        await GravarEnviadaAsync(conversa, tipoDoBalao1, legenda, externoImagem, agora, usuarioId, ct);

        var codigo = TextoCobrancaPix.BalaoDoCodigo(aviso.Pix);
        string externoCodigo;
        try
        {
            externoCodigo = await canal.EnviarTextoAsync(conversa.ContatoIdExterno, codigo, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Falha parcial: o resumo já saiu e fica gravado junto com a falha do código.
            await FalharAsync(conversa, TextoFalhaCodigo, ex, aviso.ConversaId, agora, usuarioId, ct);
            return false;
        }

        await GravarEnviadaAsync(conversa, TipoConteudoMensagem.Texto, codigo, externoCodigo, agora, usuarioId, ct);
        await unitOfWork.CommitAsync();
        return true;
    }

    /// <summary>O pedido como a cliente vê; nulo quando ele não é da empresa (o aviso cai no texto de antes).</summary>
    private async Task<ResumoPedidoDto?> ResumoAsync(CobrancaPixAviso aviso, CancellationToken ct)
    {
        var pedido = await pedidos.GetByIdWithDetailsAsync(aviso.EmpresaId, aviso.PedidoId);
        if (pedido is null || pedido.EmpresaId != aviso.EmpresaId) return null;
        var storefront = await storefronts.GetByEmpresaAsync(aviso.EmpresaId, ct);
        var entrega = pedido.AgendadoParaEm is { } quando
            ? HorarioBrasil.ConverterParaBrasilia(quando).ToString("dd/MM 'às' HH:mm", CultureInfo.InvariantCulture)
            : "a combinar";
        return ResumoPedidoAtendimento.Montar(pedido, storefront?.TituloPublico) with { Entrega = entrega };
    }

    private async Task GravarEnviadaAsync(
        Conversa conversa, TipoConteudoMensagem tipo, string texto, string externoId, DateTime agora, Guid? usuarioId, CancellationToken ct)
    {
        var mensagem = AvisoCobrancaConversa.Assinada(Mensagem.Saida(conversa.EmpresaId, conversa.Id,
            AvisoCobrancaConversa.AutorDe(usuarioId), agora, tipo, texto, externoId), usuarioId);
        conversa.RegistrarSaida(agora);
        await conversaRepository.AddMensagemAsync(mensagem, ct);
    }

    private async Task FalharAsync(
        Conversa conversa, string textoFixo, Exception ex, Guid conversaId, DateTime agora, Guid? usuarioId, CancellationToken ct)
    {
        logger.LogWarning("Falha ao enviar cobranca Pix conversaId={ConversaId} erro={Erro}", conversaId, ex.GetType().Name);
        await RegistrarFalhaAsync(conversa, textoFixo, ClassificadorFalhaEnvio.Motivo(ex), agora, usuarioId, ct);
    }

    private async Task RegistrarFalhaAsync(
        Conversa conversa, string textoFixo, string motivo, DateTime agora, Guid? usuarioId, CancellationToken ct)
    {
        var falhou = AvisoCobrancaConversa.Assinada(Mensagem.Saida(conversa.EmpresaId, conversa.Id,
            AvisoCobrancaConversa.AutorDe(usuarioId), agora, TipoConteudoMensagem.Texto, textoFixo), usuarioId);
        // Permanente e sem reenvio: reenviar o texto fixo à cliente não serve; quem decide é a atendente, gerando de novo.
        falhou.RegistrarFalhaEnvio(motivo, TipoFalhaEnvio.Permanente, agora, reenvioAutomatico: false);
        await conversaRepository.AddMensagemAsync(falhou, ct);
        await unitOfWork.CommitAsync();
    }
}
