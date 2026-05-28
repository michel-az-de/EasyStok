# TASK-EZ-CR-015 — Remover 'unsafe-inline' da CSP (refatorar Diagnostico pages)

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-14)
**Prioridade:** P2 (medio prazo)
**Esforco:** M
**Status:** inbox

## Objetivo

Eliminar `'unsafe-inline'` de `script-src` e `style-src` na CSP, refatorando paginas Diagnostico para nao usar scripts/styles inline e aplicando nonce per-request.

## Escopo

- [EasyStock.Api/Middleware/SecurityHeadersMiddleware.cs:44-48](../../../EasyStock.Api/Middleware/SecurityHeadersMiddleware.cs)
- Paginas Diagnostico (em `EasyStock.Api/Controllers/Diagnostico*.cs` que retornam HTML)
- [EasyStock.Api/Controllers/DiagnosticoHtmlRenderer.cs](../../../EasyStock.Api/Controllers/DiagnosticoHtmlRenderer.cs)

## Plano

### Fase 1 — Identificar inline scripts/styles
Buscar em `DiagnosticoHtmlRenderer.cs` e similares:
- `<script>...</script>` inline
- `<style>...</style>` inline
- `style="..."` em atributos
- `onclick="..."` em handlers

### Fase 2 — Externalizar
Mover scripts/styles para arquivos servidos via wwwroot ou inline com nonce.

### Fase 3 — Nonce per-request
```csharp
public async Task InvokeAsync(HttpContext context)
{
    var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    context.Items["csp-nonce"] = nonce;

    // ...

    headers["Content-Security-Policy"] =
        $"default-src 'self'; " +
        $"script-src 'self' 'nonce-{nonce}' https://cdn.jsdelivr.net; " +
        $"style-src 'self' 'nonce-{nonce}' https://fonts.googleapis.com; " +
        // ...
}
```

E nas paginas:
```html
<script nonce="@Context.Items["csp-nonce"]">...</script>
```

## Definicao de Pronto

- [ ] CSP sem `'unsafe-inline'`
- [ ] Paginas Diagnostico funcionam (smoke test em dev)
- [ ] Nonce gerado por request
- [ ] ZAP scan ou similar nao reclama mais de unsafe-inline
- [ ] `dotnet build` verde + tests passam
- [ ] PR mergeado

## Riscos

- Diagnostico pode quebrar visualmente se nem todos scripts foram externalizados
- CDN externos podem precisar de hashes (jsdelivr)

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-14)
