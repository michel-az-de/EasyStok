using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

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
        return await unitOfWork.ExecuteInTransactionSemRetryAsync(async ct =>
        {
            var familia = await templateRepository.ListarFamiliaComLockAsync(command.EmpresaId, command.Codigo, ct);
            if (familia.Count != 0)
                throw new InvalidOperationException("Este código já possui um modelo. Edite sua versão atual.");
            var template = TemplateNotificacao.Criar(
                command.Codigo, command.Nome, command.Canal, command.TipoEvento,
                command.AssuntoTemplate, command.CorpoTemplate,
                command.EmpresaId, command.Idioma, command.CriadoPor);

            await templateRepository.AddAsync(template);
            await unitOfWork.CommitAsync();

            logger.LogInformation(
                "Template criado: {Codigo} v{Versao} canal={Canal}", template.Codigo, template.Versao, template.Canal);
            return new TemplateResult(template.Id, template.Codigo, template.Versao);
        });
    }
}

// ── Atualizar (cria nova versão) ─────────────────────────────────────────────

public sealed record AtualizarTemplateCommand(
    Guid TemplateId,
    string NovoAssunto,
    string NovoCorpo,
    string AtualizadoPor,
    Guid EmpresaId) : ICommand;

public sealed class AtualizarTemplateUseCase(
    ITemplateRepository templateRepository,
    IUnitOfWork unitOfWork,
    ILogger<AtualizarTemplateUseCase> logger)
    : IUseCase<AtualizarTemplateCommand, TemplateResult>
{
    public async Task<TemplateResult> ExecuteAsync(AtualizarTemplateCommand command)
    {
        var original = await templateRepository.ObterDaEmpresaAsync(command.TemplateId, command.EmpresaId);
        return await unitOfWork.ExecuteInTransactionSemRetryAsync(async ct =>
        {
            var familia = await templateRepository.ListarFamiliaComLockAsync(command.EmpresaId, original.Codigo, ct);
            var atual = familia.OrderByDescending(t => t.Versao).FirstOrDefault();
            if (atual is null || atual.Id != command.TemplateId)
                throw new InvalidOperationException("Este modelo possui uma versão mais recente. Atualize a lista antes de editar.");
            var novaVersao = TemplateNotificacao.Criar(
                atual.Codigo, atual.Nome, atual.Canal, atual.TipoEvento,
                command.NovoAssunto, command.NovoCorpo,
                atual.EmpresaId, atual.Idioma, command.AtualizadoPor);
            novaVersao.Versao = atual.Versao + 1;
            novaVersao.DefinirMetadados(atual.MetadadosJson);

            // A versão publicada continua resolvível enquanto a nova aguarda aprovação.
            await templateRepository.AddAsync(novaVersao);
            await unitOfWork.CommitAsync();

            logger.LogInformation(
                "Template {Codigo} atualizado para v{Versao}", novaVersao.Codigo, novaVersao.Versao);
            return new TemplateResult(novaVersao.Id, novaVersao.Codigo, novaVersao.Versao);
        });
    }
}

// ── Aprovar ──────────────────────────────────────────────────────────────────

public sealed record AprovarTemplateCommand(Guid TemplateId, string AprovadoPor, Guid EmpresaId) : ICommand;
public sealed record AprovarTemplateResult(bool Aprovado);

public sealed class AprovarTemplateUseCase(
    ITemplateRepository templateRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<AprovarTemplateCommand, AprovarTemplateResult>
{
    public async Task<AprovarTemplateResult> ExecuteAsync(AprovarTemplateCommand command)
    {
        var template = await templateRepository.ObterDaEmpresaAsync(command.TemplateId, command.EmpresaId);
        return await unitOfWork.ExecuteInTransactionSemRetryAsync(async ct =>
        {
            var familia = await templateRepository.ListarFamiliaComLockAsync(command.EmpresaId, template.Codigo, ct);
            var atual = familia.OrderByDescending(t => t.Versao).FirstOrDefault();
            if (atual is null || atual.Id != command.TemplateId)
                throw new InvalidOperationException("Este modelo possui uma versão mais recente. Atualize a lista antes de aprovar.");
            foreach (var anterior in familia.Where(t => t.Id != atual.Id && t.Ativo))
            {
                anterior.Desativar();
                await templateRepository.UpdateAsync(anterior);
            }
            atual.Aprovar(command.AprovadoPor);
            atual.Ativar();
            await templateRepository.UpdateAsync(atual);
            await unitOfWork.CommitAsync();

            return new AprovarTemplateResult(true);
        });
    }
}

// ── Preview ───────────────────────────────────────────────────────────────────

public sealed record PreviewTemplateCommand(
    Guid TemplateId,
    IDictionary<string, object?> Variaveis,
    Guid EmpresaId) : ICommand;

public sealed record PreviewTemplateResult(string AssuntoRenderizado, string CorpoRenderizado);

public sealed class PreviewTemplateUseCase(
    ITemplateRepository templateRepository,
    IRendererTemplate renderer)
    : IUseCase<PreviewTemplateCommand, PreviewTemplateResult>
{
    public async Task<PreviewTemplateResult> ExecuteAsync(PreviewTemplateCommand command)
    {
        var template = await templateRepository.ObterDaEmpresaAsync(command.TemplateId, command.EmpresaId);

        var assunto = await renderer.RenderizarAsync(template.AssuntoTemplate, command.Variaveis);
        var corpo = await renderer.RenderizarAsync(template.CorpoTemplate, command.Variaveis);

        return new PreviewTemplateResult(assunto, corpo);
    }
}

// ── Preview de DRAFT (assunto/corpo direto, sem template salvo) ──────────────

public sealed record PreviewDraftTemplateCommand(
    string AssuntoTemplate,
    string CorpoTemplate,
    IDictionary<string, object?> Variaveis) : ICommand;

public sealed class PreviewDraftTemplateUseCase(IRendererTemplate renderer)
    : IUseCase<PreviewDraftTemplateCommand, PreviewTemplateResult>
{
    public async Task<PreviewTemplateResult> ExecuteAsync(PreviewDraftTemplateCommand command)
    {
        var assunto = await renderer.RenderizarAsync(command.AssuntoTemplate ?? string.Empty, command.Variaveis);
        var corpo = await renderer.RenderizarAsync(command.CorpoTemplate ?? string.Empty, command.Variaveis);
        return new PreviewTemplateResult(assunto, corpo);
    }
}
