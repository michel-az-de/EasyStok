# AGENTS.md — EasyStok

Regras comuns a qualquer agente (Claude Code, Codex, Kimi, Copilot). Claude Code acrescenta o `CLAUDE.md`.

## Projeto

- Stack: .NET 10 (`global.json`), PostgreSQL, EF Core, Clean Architecture. Solução `EasyStok.sln`.
- Frentes: API `EasyStock.Api`, console React `EasyStock.Console/` (ADR-0054, destino dos módulos), PWA em `EasyStock.Api/wwwroot/pwa/` (backup offline, ADR-0059), MAUI `EasyStok.Mobile`.
- Produção: VPS `/opt/stacks/easystok`, publicada por `scripts/deploy/vps-deploy.sh` (só o Felipe ou sessão autorizada). Fly.io, Render e Azure App Service estão desligados; `fly.toml` e workflows antigos são histórico.
- Multi-tenant é risco máximo: `EmpresaId` do JWT em toda leitura e escrita, filtro global + checagem no use case, 400 se divergir.

## Fonte da verdade

1. Estado atual vem de Git, `gh`, CI e serviços vivos. Documento é snapshot: `.knowledge/*.md` tem datas de maio a outubro de 2026; não use contagens, plataforma ou bloqueios de lá sem conferir.
2. Roteador técnico: `.knowledge/README.md`. Abra no máximo 1 ou 2 documentos da área. `audit-brutal.md` não se regenera.
3. Decisões: `docs/adr/` (fluxo em `0043`, tiers em `0055`). Planos: `docs/plan/README.md`. Pendências: issues e board do GitHub.
4. Não crie `.md` novo sem pedido. Atualize a fonte canônica que a mudança afetar.

## Fluxo (ADR-0043, PR-first)

issue -> branch `tipo/slug-N` -> commits -> push -> PR `closes #N` -> CI + review -> merge por tier -> limpeza.

| Regra | Conteúdo |
|---|---|
| Branch | Nada direto no `master`. Hotfix só com urgência real e GO explícito na sessão. |
| Stage | Arquivo por arquivo. Nunca `git add -A` ou `git add .`. |
| Commit | `tipo(escopo): descrição` imperativa em PT-BR, `refs #N`. Proibido wip, temp, checkpoint. |
| Gate | `powershell -File scripts/poka-yoke/gate.ps1` verde antes de cada commit (é o Husky pre-commit). |
| Artefatos | bin/, obj/, publish/, dist/, admin/, *.dll, *.exe, *.pdb nunca entram no commit. |
| Assinatura | Mudou assinatura pública: todos os call-sites no mesmo commit (`git grep` antes). |
| Worktree | Paralelo ou arriscado: `C:\rep\.worktrees\EasyStok\<slug>`, 1 worktree = 1 branch = 1 issue. WIP alheio não se descarta nem se reaproveita. |
| Inacabado | Fica na branch e na issue aberta. Stash não é memória. |
| Pronto | Só com PR `MERGEABLE` no instante do relatório, checks verdes e aceite marcado. CI verde não prova integração com o trunk atual. |
| Medir | Toda afirmação de estado exige comando antes (git, build, gh). Premissa refutada: pare e reporte. |
| Dúvida | Genuína e com consequência: uma pergunta, decisiva. Tarefa clara segue sem esperar. |

## Comunicação

PT-BR direto, sem floreio, sem travessão. Resposta = ação + resultado. Código, identificadores e commits seguem o padrão do repo.

<!-- BEGIN CASA-BABA-HUB -->
## Contexto compartilhado da Casa da Baba

Para demandas da Casa da Baba, leia o [contexto comum a Codex e Claude](https://github.com/michel-az-de/casa-da-baba-conhecimento/blob/master/hub-projeto/CONTEXTO-COMPARTILHADO.md) e o [mapa de repositórios e memórias](https://github.com/michel-az-de/casa-da-baba-conhecimento/blob/master/hub-projeto/REPOSITORIOS-VINCULADOS.md).

O hub é um repositório privado; os links exigem acesso autorizado à conta ou ao repositório. Como opção local, clone `michel-az-de/casa-da-baba-conhecimento` em uma pasta irmã chamada `casa-da-baba-conhecimento` e leia os mesmos arquivos em `../casa-da-baba-conhecimento/hub-projeto/`.

O hub reúne estudos, marca, desenhos, operação e a relação entre os sistemas. Código, contratos, decisões técnicas e regras continuam nas fontes deste repositório. Memórias históricas devem ser confrontadas com o checkout, as ADRs e as evidências atuais. O vínculo não sincroniza conversas nem modifica permissões.

Este bloco acrescenta navegação e não substitui as regras locais. Preserve alterações em andamento e siga o fluxo deste repositório ao executar demandas.
<!-- END CASA-BABA-HUB -->
