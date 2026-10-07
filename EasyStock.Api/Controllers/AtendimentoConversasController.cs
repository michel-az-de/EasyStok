using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ClienteDaConversa;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Atendimento.Reenvio;
using EasyStock.Application.UseCases.Cliente.Dossie;
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
    ReenviarMensagemUseCase reenviarUseCase,
    ListarNaoEntreguesUseCase naoEntreguesUseCase,
    GerenciarConversaAtendimentoUseCase gerenciarUseCase,
    TransferirConversaUseCase transferirUseCase,
    ObterDossieClienteUseCase dossieUseCase,
    GerarLinkCardapioConversaUseCase linkCardapioUseCase,
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

    /// <remarks>Use case por <c>[FromServices]</c>: o construtor fica como está para os testes que o montam.</remarks>
    [SwaggerOperation(Summary = "Media file of a message (private storage, #1287)",
        Description = "Mesma permissão de ler a conversa. 404 para mensagem de outra empresa, sem arquivo ou com o arquivo fora do storage.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id:guid}/mensagens/{mensagemId:guid}/midia")]
    public async Task<IActionResult> Midia(
        Guid id, Guid mensagemId, [FromServices] ObterMidiaMensagemUseCase midiaUseCase, CancellationToken ct = default)
    {
        var midia = await midiaUseCase.ExecuteAsync(new ObterMidiaMensagemQuery(currentUser.EmpresaId, id, mensagemId), ct);
        return midia is null ? DataNotFound("Mídia não encontrada.") : File(midia.Conteudo, midia.Mime);
    }

    [SwaggerOperation(Summary = "Customer dossier beside the conversation (S25)",
        Description = "Mesma projeção de GET api/clientes/{id}/dossie. Conversa sem cliente vinculado devolve o dossiê mínimo (nome do perfil e telefone).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id:guid}/dossie")]
    public async Task<IActionResult> Dossie(Guid id, CancellationToken ct = default)
    {
        var dossie = await dossieUseCase.ObterPorConversaAsync(currentUser.EmpresaId, id, ct);
        return dossie is null ? DataNotFound("Conversa não encontrada.") : DataOk(dossie);
    }

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

    [SwaggerOperation(Summary = "Messages that did not reach the customer (S59)",
        Description = "Saída que falhou, mais recente primeiro: inclui a que espera o cliente responder ao modelo de retomada e a que saiu por SMS.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet("nao-entregues")]
    public async Task<IActionResult> NaoEntregues([FromQuery] int? limite, CancellationToken ct = default)
        => DataOk(await naoEntreguesUseCase.ExecuteAsync(currentUser.EmpresaId, limite, ct));

    [SwaggerOperation(Summary = "Resend a text message that failed (S57)",
        Description = "Fora da janela de 24 h não envia: devolve a mensagem com o motivo no erro.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{id:guid}/mensagens/{mensagemId:guid}/reenviar")]
    public Task<IActionResult> ReenviarMensagem(Guid id, Guid mensagemId, CancellationToken ct = default)
        => Atendendo(async () => DataOk(await reenviarUseCase.ExecuteAsync(currentUser.EmpresaId, id, mensagemId, ct)));

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

    /// <remarks>Use case por <c>[FromServices]</c>: o construtor fica como está para os testes que o montam.</remarks>
    [SwaggerOperation(Summary = "Send a menu gallery photo by item id (takes over the conversation)",
        Description = "#1437: o backend lê a foto do storage pela chave (independe do host gravado na URL), converte para " +
                      "JPEG e envia como a rota /imagem. indice segue a galeria do item; sem galeria, 0 é a capa. " +
                      "Item de outra empresa ou arquivo fora do storage: 404. Índice inválido: 400.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [HttpPost("{id:guid}/mensagens/imagem-cardapio")]
    public Task<IActionResult> EnviarImagemCardapio(
        Guid id, [FromBody] EnviarImagemCardapioBody body,
        [FromServices] EnviarImagemCardapioConsoleUseCase imagemCardapioUseCase, CancellationToken ct = default)
        => Atendendo(async () => DataOk(await imagemCardapioUseCase.ExecuteAsync(new EnviarImagemCardapioConsoleCommand(
            currentUser.EmpresaId, currentUser.UsuarioId, id, body?.CardapioItemId ?? Guid.Empty, body?.Indice ?? 0, body?.Legenda), ct)));

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

    [SwaggerOperation(Summary = "Register the conversation's customer (name, phone, delivery address)",
        Description = "#1276. Sem cliente vinculado, o telefone é obrigatório: acha o cadastro pelo telefone ou cria, e vincula. " +
                      "Com cliente, atualiza nome e telefone. Endereço com CEP vira o padrão de entrega; fora da área é gravado " +
                      "e volta dentroDaArea=false.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [HttpPost("{id:guid}/cliente")]
    public Task<IActionResult> CadastrarCliente(
        Guid id, [FromBody] CadastrarClienteConversaBody body,
        [FromServices] CadastrarClienteDaConversaUseCase cadastrarUseCase, CancellationToken ct = default)
        => Atendendo(async () => DataOk(await cadastrarUseCase.ExecuteAsync(
            new CadastrarClienteDaConversaCommand(currentUser.EmpresaId, id, body?.Nome, body?.Telefone, body?.Endereco), ct)));

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

    [SwaggerOperation(Summary = "Store menu link tied to the conversation (same link the agent sends)",
        Description = "#1353: \"Enviar cardápio\" do console. Vale 24 h e um pedido.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{id:guid}/link-cardapio")]
    public Task<IActionResult> LinkCardapio(Guid id, CancellationToken ct = default)
        => Atendendo(async () => DataOk(await linkCardapioUseCase.ExecuteAsync(currentUser.EmpresaId, id, ct)));

    [SwaggerOperation(Summary = "Agent's suggested reply for the owner (never sent to the customer)",
        Description = "#1420: \"Sugerir\" do painel do agente. Mesmo contexto do agente, só ferramentas de consulta; " +
                      "não grava mensagem nem muda a conversa. Sem Anthropic:Enabled/Anthropic:ApiKey devolve 503.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("{id:guid}/sugestao")]
    public Task<IActionResult> Sugestao(
        Guid id, [FromServices] SugerirRespostaAgenteUseCase sugestaoUseCase, CancellationToken ct = default)
        => Atendendo(async () => DataOk(await sugestaoUseCase.ExecuteAsync(
            new SugerirRespostaAgenteCommand(currentUser.EmpresaId, id), ct)));

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
        catch (MensagemNaoEncontradaException)
        {
            return DataNotFound("Mensagem não encontrada.");
        }
        catch (FotoCardapioNaoEncontradaException ex)
        {
            return DataNotFound(ex.Message);
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
        catch (AgenteIndisponivelException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(new ApiError("AGENTE_INDISPONIVEL", ex.Message, null, null)));
        }
        catch (FalhaEnvioCanalException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new ApiErrorResponse(new ApiError("CANAL_FALHOU", "Falha ao enviar pelo canal.", ex.Message, null) { Details = ex.Mensagem }));
        }
    }
}

public sealed record EnviarMensagemConsoleBody(string Texto);

public sealed record EnviarImagemCardapioBody(Guid CardapioItemId, int Indice, string? Legenda);

public sealed record TransferirConversaBody(Guid ParaUsuarioId);

public sealed record LiberarForaDeAreaBody(string? Motivo);

public sealed record CadastrarClienteConversaBody(string? Nome, string? Telefone, EnderecoDaConversaInput? Endereco);
