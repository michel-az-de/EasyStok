using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Enums.Storefront;

namespace EasyStock.Application.UseCases.Storefront.Expediente;

public sealed record ExpedienteLojaResult(
    Guid EmpresaId,
    IReadOnlyList<HorarioFuncionamento> Horarios,
    ControleManualLoja ControleManual,
    Guid? ControleAlteradoPorUsuarioId,
    DateTime? ControleAlteradoEm,
    string MensagemForaDoHorario,
    string MensagemLojaFechada,
    bool EstaAberta,
    DateTime? ProximaAberturaLocal)
{
    internal static ExpedienteLojaResult De(ExpedienteLoja e, DateTime agoraUtc) => new(
        e.EmpresaId, e.Horarios, e.ControleManual, e.ControleAlteradoPorUsuarioId, e.ControleAlteradoEm,
        e.MensagemForaDoHorario, e.MensagemLojaFechada, e.EstaAberta(agoraUtc), e.ProximaAberturaLocal(agoraUtc));
}

/// <summary>S40: devolve o expediente; sem registro, o padrão em memória (nunca 404).</summary>
public sealed class ObterExpedienteLojaUseCase(IExpedienteLojaRepository repository, TimeProvider relogio)
{
    public async Task<ExpedienteLojaResult> ExecuteAsync(Guid empresaId, CancellationToken ct = default)
    {
        var expediente = await repository.GetByEmpresaIdAsync(empresaId, ct) ?? ExpedienteLoja.CriarPadrao(empresaId);
        return ExpedienteLojaResult.De(expediente, relogio.GetUtcNow().UtcDateTime);
    }
}

public sealed record AtualizarExpedienteLojaCommand(
    Guid EmpresaId,
    IReadOnlyList<HorarioFuncionamento>? Horarios,
    string? MensagemForaDoHorario,
    string? MensagemLojaFechada);

/// <summary>S40: grava horários e mensagens (<c>EDITAR_FUNCIONAMENTO</c> do protótipo).</summary>
public sealed class AtualizarExpedienteLojaUseCase(
    IExpedienteLojaRepository repository, IUnitOfWork unitOfWork, IOperacaoEventPublisher publisher, TimeProvider relogio)
{
    public async Task<ExpedienteLojaResult> ExecuteAsync(AtualizarExpedienteLojaCommand command, CancellationToken ct = default)
    {
        var (expediente, novo) = await CarregarAsync(repository, command.EmpresaId, ct);

        try
        {
            if (command.Horarios is not null) expediente.DefinirHorarios(command.Horarios);
            expediente.DefinirMensagens(command.MensagemForaDoHorario, command.MensagemLojaFechada);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        return await GravarAsync(repository, unitOfWork, publisher, expediente, novo, relogio, ct);
    }

    internal static async Task<(ExpedienteLoja Expediente, bool Novo)> CarregarAsync(
        IExpedienteLojaRepository repository, Guid empresaId, CancellationToken ct)
    {
        var existente = await repository.GetByEmpresaIdAsync(empresaId, ct);
        return existente is null ? (ExpedienteLoja.CriarPadrao(empresaId), true) : (existente, false);
    }

    internal static async Task<ExpedienteLojaResult> GravarAsync(
        IExpedienteLojaRepository repository, IUnitOfWork unitOfWork, IOperacaoEventPublisher publisher,
        ExpedienteLoja expediente, bool novo, TimeProvider relogio, CancellationToken ct)
    {
        if (novo) await repository.AddAsync(expediente, ct);
        else await repository.UpdateAsync(expediente, ct);
        await unitOfWork.CommitAsync();

        var resultado = ExpedienteLojaResult.De(expediente, relogio.GetUtcNow().UtcDateTime);
        // Depois do commit: o console atualiza o trilho de "loja aberta" em tempo real (SSE, S18).
        await publisher.PublicarAsync("expediente.alterado", expediente.EmpresaId,
            new { estaAberta = resultado.EstaAberta, controleManual = resultado.ControleManual }, ct);
        return resultado;
    }
}

/// <param name="Nivel">Nível de quem pede (do token). Padrão: o menor, para nenhum chamador esquecer de informar.</param>
/// <param name="Justificativa">Obrigatória só para fechar dentro do horário de funcionamento (#1443).</param>
public sealed record DefinirControleExpedienteCommand(
    Guid EmpresaId,
    ControleManualLoja Controle,
    Guid? UsuarioId,
    NivelAcesso Nivel = NivelAcesso.Visualizador,
    string? Justificativa = null);

/// <summary>
/// S40: abre ou fecha na mão, ou volta ao automático (<c>ALTERNAR_LOJA</c> do protótipo).
///
/// <para>
/// Quem pode (#1443, decisão do Felipe em 07/10): abrir e fechar na mão é de gerente para cima;
/// devolver ao horário continua só da dona (Admin). Fechar <strong>dentro do horário de
/// funcionamento</strong> exige justificativa: ela vai para a auditoria (quem, quando, porquê) e os
/// donos recebem o aviso <see cref="TipoEventoNotificacao.LojaFechadaNoHorario"/> pelo outbox, na
/// mesma unidade de trabalho. Fora do horário, fechar segue como sempre.
/// </para>
/// </summary>
public sealed class DefinirControleExpedienteUseCase(
    IExpedienteLojaRepository repository,
    IUnitOfWork unitOfWork,
    IOperacaoEventPublisher publisher,
    TimeProvider relogio,
    IAuditLogRepository auditoria,
    INotificadorService notificador)
{
    public const string AcaoAuditoriaFecharNoHorario = "loja.fechada_no_horario";
    public const int JustificativaMinima = 10;
    // 400: com o escape mínimo do JSON (aspas e quebras viram dois caracteres) os detalhes cabem nos 1000 da auditoria.
    public const int JustificativaMaxima = 400;

    private static readonly JsonSerializerOptions JsonAuditoria = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<ExpedienteLojaResult> ExecuteAsync(DefinirControleExpedienteCommand command, CancellationToken ct = default)
    {
        var (expediente, novo) = await AtualizarExpedienteLojaUseCase.CarregarAsync(repository, command.EmpresaId, ct);
        var agora = relogio.GetUtcNow().UtcDateTime;

        ExigirNivel(command);
        var fecharNoHorario = command.Controle == ControleManualLoja.ForcarFechada
                              && expediente.DentroDoHorarioDeFuncionamento(agora);
        var justificativa = fecharNoHorario ? ValidarJustificativa(command) : null;

        var controleAnterior = expediente.ControleManual;
        try
        {
            expediente.DefinirControle(command.Controle, command.UsuarioId, agora);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        if (justificativa is not null)
            await RegistrarFechamentoNoHorarioAsync(command, controleAnterior, justificativa, agora, ct);

        return await AtualizarExpedienteLojaUseCase.GravarAsync(repository, unitOfWork, publisher, expediente, novo, relogio, ct);
    }

    private static void ExigirNivel(DefinirControleExpedienteCommand command)
    {
        if (command.Controle == ControleManualLoja.Automatico)
        {
            if (command.Nivel > NivelAcesso.Admin)
                throw new UnauthorizedAccessException("Só a dona devolve a loja ao horário.");
            return;
        }

        if (command.Nivel > NivelAcesso.Gerente)
            throw new UnauthorizedAccessException("Só gerente ou dona abre e fecha a loja na mão.");
    }

    private static string ValidarJustificativa(DefinirControleExpedienteCommand command)
    {
        if (command.UsuarioId is not { } usuario || usuario == Guid.Empty)
            throw new UseCaseValidationException("Fechar a loja no horário exige um usuário identificado.");

        var texto = command.Justificativa?.Trim() ?? string.Empty;
        if (texto.Length < JustificativaMinima)
            throw new UseCaseValidationException(
                $"Para fechar a loja dentro do horário de funcionamento, escreva a justificativa (pelo menos {JustificativaMinima} letras).");
        return texto.Length > JustificativaMaxima ? texto[..JustificativaMaxima] : texto;
    }

    private async Task RegistrarFechamentoNoHorarioAsync(
        DefinirControleExpedienteCommand command, ControleManualLoja controleAnterior, string justificativa,
        DateTime agora, CancellationToken ct)
    {
        var detalhes = JsonSerializer.Serialize(new
        {
            empresaId = command.EmpresaId,
            nivel = command.Nivel.ToString(),
            controleAnterior = controleAnterior.ToString(),
            justificativa,
        }, JsonAuditoria);
        await auditoria.AddAsync(AuditLog.Criar(command.UsuarioId!.Value, AcaoAuditoriaFecharNoHorario, true, detalhes, null, null));

        // Sem "usuarioId" no payload: ele venceria a audiência "admins" da rotina e o aviso iria só para quem fechou.
        var hora = HorarioBrasil.ConverterParaBrasilia(agora).ToString("HH:mm", CultureInfo.InvariantCulture);
        var payload = JsonSerializer.Serialize(new
        {
            fechadoPorUsuarioId = command.UsuarioId.Value.ToString(),
            hora,
            justificativa,
        }, JsonAuditoria);
        await notificador.EnfileirarEventoAsync(TipoEventoNotificacao.LojaFechadaNoHorario, command.EmpresaId, payload, null, ct);
    }
}
