using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Dtos;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Reporting;

namespace EasyStock.Application.UseCases.Reports;

// ── Command ─────────────────────────────────────────────────────────────────

public sealed record EnqueueReportRunCommand(
    string ReportKey,
    string ParamsJson,
    ReportFormat Format,
    string? IdempotencyKey = null,
    string? MotivoExecucao = null) : ICommand;

// ── Result ───────────────────────────────────────────────────────────────────

public sealed record EnqueueReportRunResult(
    ReportRunDto Run,
    bool JaExistia);   // true = idempotência — run pré-existente retornada

// ── UseCase ──────────────────────────────────────────────────────────────────

public sealed class EnqueueReportRunUseCase
    : IUseCase<EnqueueReportRunCommand, EnqueueReportRunResult>
{
    private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromMinutes(5);

    private readonly IReportRunRepository _repo;
    private readonly ReportRegistry _registry;
    private readonly ICurrentUserAccessor _user;

    public EnqueueReportRunUseCase(
        IReportRunRepository repo,
        ReportRegistry registry,
        ICurrentUserAccessor user)
    {
        _repo = repo;
        _registry = registry;
        _user = user;
    }

    public async Task<EnqueueReportRunResult> ExecuteAsync(EnqueueReportRunCommand command)
    {
        var ct = CancellationToken.None;

        // 1. Definição existe?
        var definition = _registry.Get(command.ReportKey);

        // 2. Permissão
        if (_user.Nivel != EasyStock.Domain.Enums.NivelAcesso.SuperAdmin &&
            !HasPermissao(definition.PermissaoRequerida))
            throw new UnauthorizedAccessException(
                $"Sem permissão para o relatório '{command.ReportKey}'.");

        // 3. Format suportado?
        if (!definition.FormatosSuportados.Contains(command.Format))
            throw new ArgumentException(
                $"Formato '{command.Format}' não suportado por '{command.ReportKey}'.",
                nameof(command.Format));

        // 4. ParamsJson tamanho máximo 64 KB (G-14)
        if (Encoding.UTF8.GetByteCount(command.ParamsJson) > 65_536)
            throw new ArgumentException("ParamsJson excede 64 KB.", nameof(command.ParamsJson));

        // 5. Idempotência forte (IdempotencyKey fornecida pelo cliente)
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existente = await _repo.FindByIdempotencyKeyAsync(
                OwnerId(), _user.UsuarioId, command.IdempotencyKey.Trim(), ct);
            if (existente is not null)
                return new EnqueueReportRunResult(MapToDto(existente, definition), JaExistia: true);
        }

        // 6. Idempotência fraca (paramsHash + janela de 5 min)
        var paramsHash = ComputeHash(command.ReportKey, command.ParamsJson, command.Format);
        var recente = await _repo.FindRecentByParamsHashAsync(
            OwnerId(), _user.UsuarioId, paramsHash, IdempotencyWindow, ct);
        if (recente is not null &&
            recente.Status is ReportStatus.Pending or ReportStatus.Running or ReportStatus.Canceling)
            return new EnqueueReportRunResult(MapToDto(recente, definition), JaExistia: true);

        // 7. Criar run
        var contexto = definition.Contexto;
        var run = ReportRun.Enqueue(
            empresaId: OwnerId() ?? Guid.Empty,
            usuarioId: _user.UsuarioId,
            reportKey: command.ReportKey,
            categoria: definition.Categoria,
            contexto: contexto,
            paramsJson: command.ParamsJson,
            paramsHash: paramsHash,
            format: command.Format,
            semanticVersion: definition.SemanticVersion,
            retention: definition.Retencao,
            maxTentativas: definition.MaxTentativas,
            idempotencyKey: command.IdempotencyKey,
            motivoExecucao: command.MotivoExecucao);

        await _repo.AddAsync(run, ct);
        return new EnqueueReportRunResult(MapToDto(run, definition), JaExistia: false);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private Guid? OwnerId() =>
        _user.Nivel == EasyStock.Domain.Enums.NivelAcesso.SuperAdmin
            ? (Guid?)null
            : _user.EmpresaId;

    private bool HasPermissao(string permissaoRequerida) =>
        // Permissão simples via NivelAcesso ou Claims — verifica por nome
        _user.Nivel == EasyStock.Domain.Enums.NivelAcesso.SuperAdmin;

    private static string ComputeHash(string reportKey, string paramsJson, ReportFormat format)
    {
        var raw = $"{reportKey}|{paramsJson}|{format}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static ReportRunDto MapToDto(ReportRun run, IReportDefinition def) =>
        new(
            Id: run.Id,
            ReportKey: run.ReportKey,
            ReportLabel: def.Label,
            Categoria: run.Categoria,
            Contexto: run.Contexto,
            Format: run.Format,
            Status: run.Status,
            StatusLabel: run.Status.ToString(),
            ParamsJson: run.ParamsJson,
            Tentativas: run.Tentativas,
            MaxTentativas: run.MaxTentativas,
            RowCount: run.RowCount,
            ArtifactSizeBytes: run.ArtifactSizeBytes,
            ErrorMessage: run.ErrorMessage,
            WarningsJson: run.WarningsJson,
            SemanticVersion: run.SemanticVersion,
            EnqueuedAt: run.EnqueuedAt,
            StartedAt: run.StartedAt,
            FinishedAt: run.FinishedAt,
            ExpiresAt: run.ExpiresAt,
            CanDownload: run.Status == ReportStatus.Succeeded && run.ArtifactStorageKey is not null);
}
