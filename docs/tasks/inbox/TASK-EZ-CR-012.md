# TASK-EZ-CR-012 — SkiaImageProcessor maxBytes + AdminAuditLog motivo truncamento

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-15 + ACHADO-16)
**Prioridade:** P2
**Esforco:** P
**Status:** inbox

## Objetivo

Prevenir DoS via imagem grande no `SkiaImageProcessor` e bloat de indice no Postgres via `motivo` longo em audit log.

## Problemas

1. `SkiaImageProcessor.Optimize(byte[] source, ...)` aceita tamanho arbitrario. `SKBitmap.Decode` pode crashar com bomb decompressao.
2. `AdminAuditService.LogAsync` passa `motivo` direto sem truncar — se Domain nao validar, pode ser 100KB de texto.

## Escopo

- [EasyStock.Api/Services/SkiaImageProcessor.cs](../../../EasyStock.Api/Services/SkiaImageProcessor.cs)
- [EasyStock.Api/Services/AdminAuditService.cs](../../../EasyStock.Api/Services/AdminAuditService.cs)
- [EasyStock.Api/Controllers/UploadsController.cs](../../../EasyStock.Api/Controllers/UploadsController.cs) (validar `[RequestSizeLimit]`)
- `EasyStock.Domain/Entities/AdminAuditLog.cs` (verificar se ha validacao em `Criar`)

## Fix 1 — SkiaImageProcessor

```csharp
private const int MaxBytes = 10 * 1024 * 1024; // 10MB

public (byte[] Data, string ContentType, string Extension) Optimize(
    byte[] source, string originalContentType, int maxSide = 1920, int quality = 85)
{
    if (source.Length > MaxBytes)
        throw new ArgumentException($"Imagem excede o tamanho maximo de {MaxBytes / 1024 / 1024}MB.", nameof(source));
    // ... resto
}
```

Tambem adicionar em `UploadsController`:
```csharp
[RequestSizeLimit(10 * 1024 * 1024)]
[HttpPost("imagens")]
public async Task<IActionResult> UploadImagem(...)
```

## Fix 2 — AdminAuditLog motivo

Opcao A (no Domain — recomendado):
```csharp
public static AdminAuditLog Criar(string email, string acao, string? detalhes, Guid? tenantId, string? ip, string? motivo, Guid? entidadeAfetadaId)
{
    if (motivo?.Length > 500)
        motivo = motivo[..500];
    // ... resto
}
```

Opcao B (no service):
```csharp
public async Task LogAsync(..., string? motivo = null, ...)
{
    motivo = motivo?.Length > 500 ? motivo[..500] : motivo;
    db.AdminAuditLogs.Add(AdminAuditLog.Criar(..., motivo, ...));
}
```

## Definicao de Pronto

- [ ] `SkiaImageProcessor` rejeita imagens > 10MB
- [ ] `UploadsController` tem `[RequestSizeLimit]` apropriado
- [ ] `motivo` truncado em 500 chars
- [ ] Unit tests: imagem grande → ArgumentException; motivo longo → truncado
- [ ] `dotnet build` verde
- [ ] PR mergeado

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-15 + ACHADO-16)
