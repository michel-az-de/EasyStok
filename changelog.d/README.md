# Fragmentos do CHANGELOG (ADR-0055)

Cada PR cria **um arquivo** aqui, em vez de editar o topo do `CHANGELOG.md`. Assim duas PRs
paralelas nunca conflitam no mesmo trecho.

## Nome

`<numero-da-issue>.md`, por exemplo `1132.md`. Se a PR fecha mais de uma issue, use a principal.

## Conteúdo

A primeira linha é a seção do Keep a Changelog, depois a entrada, no mesmo estilo do
`CHANGELOG.md`:

```markdown
### Added
- **Caixa de entrada na API real** (F01): login em dois passos e polling da inbox. (#1132)
```

Seções válidas: `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`.

## No release

```powershell
powershell -File scripts/changelog/juntar.ps1 -Versao 1.2.0
```

O script junta os fragmentos por seção no topo do `CHANGELOG.md`, sob `## [1.2.0] - <data>`, e
apaga os arquivos juntados. Este README fica.
