# Incidente: SharpCompress sem patch upstream (NU1903)

**Data:** 2026-05-23
**Severidade:** Moderada (GHSA-6c8g-7p36-r338)
**Decisão:** Aceitar risco com supressão + monitorar

## Vulnerabilidade

- **CVE:** GHSA-6c8g-7p36-r338
- **Pacote:** `SharpCompress`
- **Affected:** `<= 0.47.4` (todas as versões disponíveis)
- **First Fixed Version:** **None** (publicado mas sem patch upstream em maio/2026)
- **Tipo:** path traversal em descompactação de arquivos maliciosos

## Triagem da exposição no EasyStok

Grep no repo inteiro por uso real:

```bash
grep -rn "SharpCompress\|IArchive\|ArchiveFactory" /c/easy/EasyStok --include="*.cs" | grep -v "bin/\|obj/"
```

**Resultado: zero matches**. O código do EasyStok não usa nenhuma API do SharpCompress diretamente.

O pacote chega como **dependência transitiva** de `MongoDB.Driver 2.x` em 3 projetos:
- `EasyStock.Api`
- `EasyStock.Api.UnitTests`
- `EasyStock.Infra.MongoDb.IntegrationTests`

Tentativa de bump (PR #210): `0.30.1 → 0.40.0` — sem efeito (toda a faixa `<= 0.47.4` está vulnerável).

## Exposição real = nula

Como SharpCompress só é executado quando alguém **chama uma API dele** (e ninguém chama em todo o codebase), o caminho do CVE não é alcançável. O DLL fica no `bin/` mas inerte.

## Decisão

1. Suprimir warning NU1903 nos 3 csproj via `<NoWarn>$(NoWarn);NU1903</NoWarn>` (escopo local, sem global silencing).
2. Documentar este incidente.
3. **Plano de re-avaliação:**
   - Re-checar a cada 90 dias se houve patch upstream.
   - Se em algum momento o código passar a usar SharpCompress diretamente (qualquer `using SharpCompress`), reativar a checagem imediatamente e isolar input untrusted.
4. **Não bumpar MongoDB.Driver** para tentar trocar a transitiva — versão 3.x tem breaking changes incompatíveis com o data layer atual.

## Validação pós-supressão

```bash
dotnet list /c/easy/EasyStok/EasyStok.sln package --vulnerable --include-transitive
```

Esperado: **0 HIGH, 0 MODERATE** (NU1903 SharpCompress suprimido localmente).

## Referências

- [GHSA-6c8g-7p36-r338](https://github.com/advisories/GHSA-6c8g-7p36-r338)
- PR #203 (Scriban CVE — também HIGH, mas com patch e aplicado)
- PR #210 (sweep inicial — bumpou SharpCompress para 0.40.0, ineficaz)
