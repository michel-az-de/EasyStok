using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums.Atendimento;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// API da inbox do console de atendimento (S07, ADR-0050): a dona lê, responde, assume, devolve ao
/// agente e encerra conversas. A tela é do console novo (outro repositório); aqui só a API.
/// Ações que mexem na conversa exigem <see cref="Permissao.AtenderConversas"/> (S41); ler basta a policy.
/// </summary>
[SwaggerTag("Attendance inbox (console)")]
[ApiController]
[Route("api/atendimento/conversas")]
[Authorize(Policy = "Operador")]
public class AtendimentoConversasController(
    ListarConversasAtendimentoUseCase listarUseCase,
    ListarMensagensConversaUseCase listarMensagensUseCase,
    EnviarMensagemConsoleUseCase enviarUseCase,
    GerenciarConversaAtendimentoUseCase gerenciarUseCase,
    TransferirConversaUseCase transferirUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    private const int TamanhoMaximoImagem = 6 * 1024 * 1024;

    [SwaggerOperation(Summary = "List conversations (inbox)",
        Description = "responsavel: eu | ninguem | {usuarioId}. Sem o parâmetro, todas.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] SituacaoConversa? situacao, [FromQuery] string? q, [FromQuery] string? responsavel,
        [FromQuery] int pagina = 1, [FromQuery] int limite = ListarConversasAtendimentoUseCase.LimitePadrao,
        CancellationToken ct = default)
    {
        FiltroResponsavel? filtro;
        switch (responsavel?.Trim().ToLowerInvariant())
        {
            case null or "": filtro = null; break;
            case "eu": filtro = new FiltroResponsavel(currentUser.UsuarioId); break;
            case "ninguem": filtro = FiltroResponsavel.Ninguem; break;
            default:
                if (!Guid.TryParse(responsavel, out var usuarioId))
                    return DataBadRequest("responsavel deve ser eu, ninguem ou o id de um usuário.");
                filtro = new FiltroResponsavel(usuarioId);
                break;
        }

        return DataOk(await listarUseCase.ExecuteAsync(
            new ListarConversasAtendimentoQuery(currentUser.EmpresaId, situacao, q, pagina, limite, filtro), ct));
    }

    [SwaggerOperation(Summary = "List messages of a conversation (backwards by antesDe)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id:guid}/mensagens")]
    public Task<IActionResult> ListarMensagens(
        Guid id, [FromQuery] DateTime? antesDe,
        [FromQuery] int limite = ListarMensagensConversaUseCase.LimitePadrao, CancellationToken ct = default)
        => Tratar(async () => DataOk(await listarMensagensUseCase.ExecuteAsync(
            new ListarMensagensConversaQuery(currentUser.EmpresaId, id, antesDe, limite), ct)));

    [SwaggerOperation(Summary = "Send a text message as the owner (takes over the conversation)",
        Description = "Fora da janela de 24 h: 409 { erro: \"fora_da_janela_24h\", sugestao: \"template\" } e nada gravado.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [HttpPost("{id:guid}/mensagens")]
    public Task<IActionResult> EnviarMensagem(Guid id, [FromBody] EnviarMensagemConsoleBody body, CancellationToken ct = default)
        => Atendendo(async () => DataOk(await enviarUseCase.EnviarTextoAsync(
            new EnviarTextoConsoleCommand(currentUser.EmpresaId, currentUser.UsuarioId, id, body?.Texto ?? string.Empty), ct)));

    [SwaggerOperation(Summary = "Send an image as the owner (multipart; takes over the conversation)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [RequestSizeLimit(TamanhoMaximoImagem)]
    [RequestFormLimits(MultipartBodyLengthLimit = TamanhoMaximoImagem)]
    [HttpPost("{id:guid}/mensagens/imagem")]
    public Task<IActionResult> EnviarImagem(Guid id, IFormFile file, [FromForm] string? legenda, CancellationToken ct = default)
        => Atendendo(async () =>
        {
            if (file is null || file.Length == 0)
                return DataBadRequest("Arquivo nao informado ou vazio.");
            if (file.Length > TamanhoMaximoImagem)
                return DataBadRequest("A imagem nao pode ser maior que 6 MB.");

            await using var memoria = new MemoryStream();
            await file.CopyToAsync(memoria, ct);
            return DataOk(await enviarUseCase.EnviarImagemAsync(new EnviarImagemConsoleCommand(
                currentUser.EmpresaId, currentUser.UsuarioId, id, file.FileName, file.ContentType, memoria.ToArray(), legenda), ct));
        });

    [SwaggerOperation(Summary = "Take over the conversation without sending")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [HttpPost("{id:guid}/assumir")]
    public Task<IActionResult> Assumir(Guid id, CancellationToken ct = default)
        => Atendendo(async () => DataOk(await gerenciarUseCase.AssumirAsync(Acao(id), ct)));

    [SwaggerOperation(Summary = "Transfer the conversation to another attendant",
        Description = "O destino precisa ser usuário ativo da empresa com permissão de atender; senão 422.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [HttpPost("{id:guid}/transferir")]
    public Task<IActionResult> Transferir(Guid id, [FromBody] TransferirConversaBody body, CancellationToken ct = default)
        => Atendendo(async () => DataOk(await transferirUseCase.ExecuteAsync(
            new TransferirConversaCommand(currentUser.EmpresaId, currentUser.UsuarioId, id, body?.ParaUsuarioId ?? Guid.Empty), ct)));

    [SwaggerOperation(Summary = "Give the conversation back to the agent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [HttpPost("{id:guid}/liberar-automatico")]
    public Task<IActionResult> LiberarAutomatico(Guid id, CancellationToken ct = default)
        => Atendendo(async () => DataOk(await gerenciarUseCase.LiberarAutomaticoAsync(Acao(id), ct)));

    [SwaggerOperation(Summary = "Allow delivery outside the service area (S14)",
        Description = "Grava foraDeAreaLiberado=true e o motivo no contexto da conversa; o próximo pedido criado nela vai para aprovação.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{id:guid}/liberar-fora-de-area")]
    public Task<IActionResult> LiberarForaDeArea(Guid id, [FromBody] LiberarForaDeAreaBody? body, CancellationToken ct = default)
        => Atendendo(async () => DataOk(await gerenciarUseCase.LiberarForaDeAreaAsync(Acao(id), body?.Motivo, ct)));

    [SwaggerOperation(Summary = "Close the conversation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{id:guid}/encerrar")]
    public Task<IActionResult> Encerrar(Guid id, CancellationToken ct = default)
        => Atendendo(async () => DataOk(await gerenciarUseCase.EncerrarAsync(Acao(id), ct)));

    [SwaggerOperation(Summary = "Mark the conversation as read (NaoLidas = 0)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{id:guid}/marcar-lida")]
    public Task<IActionResult> MarcarLida(Guid id, CancellationToken ct = default)
        => Tratar(async () => DataOk(await gerenciarUseCase.MarcarLidaAsync(Acao(id), ct)));

    private AcaoConversaCommand Acao(Guid id) => new(currentUser.EmpresaId, currentUser.UsuarioId, id);

    /// <summary>Ação que mexe na conversa: só quem atende (S41). Sem a permissão, 403 antes de tocar em nada.</summary>
    private Task<IActionResult> Atendendo(Func<Task<IActionResult>> acao)
        => currentUser.TemPermissao(Permissao.AtenderConversas) ? Tratar(acao) : Task.FromResult<IActionResult>(Forbid());

    /// <summary>Traduz as exceções do handoff para HTTP. O 409 de janela tem o corpo que o console espera.</summary>
    private async Task<IActionResult> Tratar(Func<Task<IActionResult>> acao)
    {
        try
        {
            return await acao();
        }
        catch (ConversaNaoEncontradaException)
        {
            return DataNotFound("Conversa não encontrada.");
        }
        catch (DestinoNaoAtendenteException ex)
        {
            return UnprocessableEntity(new ApiErrorResponse(new ApiError("DESTINO_NAO_ATENDE", ex.Message, null, null)));
        }
        catch (ForaDaJanelaAtendimentoException)
        {
            return Conflict(new { erro = ForaDaJanelaAtendimentoException.Codigo, sugestao = ForaDaJanelaAtendimentoException.Sugestao });
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            return DataConflict(ex.Message);
        }
        catch (FalhaEnvioCanalException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new ApiErrorResponse(new ApiError("CANAL_FALHOU", "Falha ao enviar pelo canal.", ex.Message, null)));
        }
    }
}

public sealed record EnviarMensagemConsoleBody(string Texto);

public sealed record TransferirConversaBody(Guid ParaUsuarioId);

public sealed record LiberarForaDeAreaBody(string? Motivo);
