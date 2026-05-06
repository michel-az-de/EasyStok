using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Notifications;

// ── Criar ───────────────────────────────────────────────────────────────────

public sealed record CriarTemplateCommand(
    string Codigo,
    string Nome,
    CanalNotificacao Canal,
    TipoEventoNotificacao TipoEvento,
    string AssuntoTemplate,
    string CorpoTemplate,
    string Idioma,
    string CriadoPor,
    Guid? EmpresaId = null) : ICommand;

public sealed record TemplateResult(Guid Id, string Codigo, int Versao);

public sealed class CriarTemplateUseCase(
    ITemplateRepository templateRepository,
    IUnitOfWork unitOfWork,
    ILogger<CriarTemplateUseCase> logger)
    : IUseCase<CriarTemplateCommand, TemplateResult>
{
    public async Task<TemplateResult> ExecuteAsync(CriarTemplateCommand command)
    {
        var template = TemplateNotificacao.Criar(
            command.Codigo, command.Nome, command.Canal, command.TipoEvento,
            command.AssuntoTemplate, command.CorpoTemplate,
            command.EmpresaId, command.Idioma, command.CriadoPor);

        await templateRepository.AddAsync(template);
        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Template criado: {Codigo} v{Versao} canal={Canal}", template.Codigo, template.Versao, template.Canal);
        return new TemplateResult(template.Id, template.Codigo, template.Versao);
    }
}

// ── Atualizar (cria nova versão) ─────────────────────────────────────────────

public sealed record AtualizarTemplateCommand(
    Guid TemplateId,
    string NovoAssunto,
    string NovoCorpo,
    string AtualizadoPor) : ICommand;

public sealed class AtualizarTemplateUseCase(
    ITemplateRepository templateRepository,
    IUnitOfWork unitOfWork,
    ILogger<AtualizarTemplateUseCase> logger)
    : IUseCase<AtualizarTemplateCommand, TemplateResult>
{
    public async Task<TemplateResult> ExecuteAsync(AtualizarTemplateCommand command)
    {
        var original = await templateRepository.GetByIdAsync(command.TemplateId)
            ?? throw new InvalidOperationException($"Template {command.TemplateId} não encontrado.");

        // Cria nova versão mantendo histórico do original
        var novaVersao = TemplateNotificacao.Criar(
            original.Codigo, original.Nome, original.Canal, original.TipoEvento,
            command.NovoAssunto, command.NovoCorpo,
            original.EmpresaId, original.Idioma, command.AtualizadoPor);

        // Desativa versão anterior
        original.Desativar();

        await templateRepository.UpdateAsync(original);
        await templateRepository.AddAsync(novaVersao);
        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Template {Codigo} atualizado para v{Versao}", novaVersao.Codigo, novaVersao.Versao);
        return new TemplateResult(novaVersao.Id, novaVersao.Codigo, novaVersao.Versao);
    }
}

// ── Aprovar ──────────────────────────────────────────────────────────────────

public sealed record AprovarTemplateCommand(Guid TemplateId, string AprovadoPor) : ICommand;
public sealed record AprovarTemplateResult(bool Aprovado);

public sealed class AprovarTemplateUseCase(
    ITemplateRepository templateRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<AprovarTemplateCommand, AprovarTemplateResult>
{
    public async Task<AprovarTemplateResult> ExecuteAsync(AprovarTemplateCommand command)
    {
        var template = await templateRepository.GetByIdAsync(command.TemplateId)
            ?? throw new InvalidOperationException($"Template {command.TemplateId} não encontrado.");

        template.Aprovar(command.AprovadoPor);
        await templateRepository.UpdateAsync(template);
        await unitOfWork.CommitAsync();

        return new AprovarTemplateResult(true);
    }
}

// ── Preview ───────────────────────────────────────────────────────────────────

public sealed record PreviewTemplateCommand(
    Guid TemplateId,
    IDictionary<string, object?> Variaveis) : ICommand;

public sealed record PreviewTemplateResult(string AssuntoRenderizado, string CorpoRenderizado);

public sealed class PreviewTemplateUseCase(
    ITemplateRepository templateRepository,
    IRendererTemplate renderer)
    : IUseCase<PreviewTemplateCommand, PreviewTemplateResult>
{
    public async Task<PreviewTemplateResult> ExecuteAsync(PreviewTemplateCommand command)
    {
        var template = await templateRepository.GetByIdAsync(command.TemplateId)
            ?? throw new InvalidOperationException($"Template {command.TemplateId} não encontrado.");

        var assunto = await renderer.RenderizarAsync(template.AssuntoTemplate, command.Variaveis);
        var corpo = await renderer.RenderizarAsync(template.CorpoTemplate, command.Variaveis);

        return new PreviewTemplateResult(assunto, corpo);
    }
}
