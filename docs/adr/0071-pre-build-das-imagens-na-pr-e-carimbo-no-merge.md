# ADR-0071 · Pré-build das imagens na PR e carimbo do sha no merge

- Status: Aceito
- Data: 2026-11-25
- Decisor: Felipe (meta: mediana do deploy de 419 s para 171 s); desenho e medição da sessão da issue #3345
- Amplia: #3323 (o deploy puxa do GHCR a imagem do sha que o `build-images` publica)
- Relacionados: issue #3345; `.github/workflows/build-images.yml`; `scripts/deploy/vps-deploy.sh`

## Contexto

Com a #3323 o deploy de mudança só de script leva 63 s, mas o de mudança de código leva 173 s: o deploy
espera o `build-images` do merge e o `publish` da api roda nele (107 s dos 154 s do build; api 175 s,
worker 140 s, web 93 s). A guarda #1336 e o rollback não mudam; o que falta é tirar a compilação do
caminho do merge.

Medido em 80 PRs mergeadas de 22 a 25/11/2026: em 59% a 64% delas nenhum outro merge entrou no master entre o
último push da PR e o merge (a árvore do merge ref é a do squash). A janela push→merge tem mediana de
14 min. Em 8 de 8 amostras a árvore do merge ref (`git merge-tree`) é igual à do commit squash.

## Decisão

1. **Na PR** (mesmo repositório, não fork, não Dependabot) o `build-images` builda as três imagens no merge ref e
   publica `easystok-<svc>:tree-<hash da árvore>` com o rótulo `io.easystok.tree`. `GIT_SHA=tree-<hash>`: a imagem da
   PR nunca passa na conferência do `vps-deploy.sh`. O job é `continue-on-error` e só lê o cache gha.
2. **No push do master** o job calcula `git rev-parse HEAD^{tree}`. Se `tree-<hash>` existe e o rótulo confere,
   faz a camada fina de `.github/carimbo/Dockerfile` (só `ENV GIT_SHA`, nenhuma camada nova) por cima do digest da
   imagem da PR e publica `:<sha>`. Confere um único `GIT_SHA` igual ao sha, `rootfs` e config iguais à base. Só então move
   `:latest`. Sem tag, com carimbo ou conferência falhos: build completo, como na #3323.
3. **Cache.** O carimbo não grava cache gha; um passo final do job aquece o cache do master depois do `:sha`, fora
   do caminho do deploy.
4. **Limpeza.** Job diário (e `workflow_dispatch` com `limpar=simular|apagar`) apaga a versão que só tem tags
   `tree-*` com mais de 2 dias. Nunca versão sem tag, `:sha` ou `:latest`.
5. **O `vps-deploy.sh` não muda.** Continua puxando `:<sha>`, conferindo o `GIT_SHA` no container (rc 9 refaz em build) e
   guardando o rollback.

## Riscos aceitos

- **Tag `tree-*` forjável.** Quem escreve numa PR do mesmo repositório roda o workflow da própria PR com
  `packages: write` e poderia publicar uma `tree-<hash>` com outro conteúdo; o master a carimbaria. O `master` não tem
  proteção de branch: quem escreve no repositório já empurra direto no master e no GHCR por `workflow_dispatch`.
  O pré-build não amplia essa fronteira. Fork e Dependabot ficam de fora. Se a proteção de branch vier, o
  carimbo passa a exigir run `pull_request` concluído e workflow igual ao do master.
- **Mesma árvore não é mesmo byte.** O `Dockerfile.Web` roda `npm install` sem lockfile e todos os Dockerfiles rodam
  `apt-get`. Isso já valia para o build do merge. Agora a imagem que vai ao ar é a que a PR buildou, a mesma que
  o review viu.
- **Merge que não seja squash.** Vale `HEAD^{tree}` do master, que no merge commit e no rebase também é a árvore do merge
  ref: o carimbo continua correto. O fluxo do projeto é squash.

## Alternativas descartadas

- **Cache de registry escrito pela PR e lido pelo master.** Mesmo envenenamento da tag forjável, sem a igualdade exata de árvore.
- **Chave por imagem (só os arquivos do contexto de cada Dockerfile).** Subiria o acerto, mas exige manter a lista
  de caminhos por imagem; guardada como próximo passo se o acerto medido ficar abaixo de 59%.
- **Carimbar na VPS.** Tira o `:sha` do GHCR, de onde o rollback também puxa.

## Consequências

- Com a árvore igual, a espera do deploy cai de cerca de 118 s (api compilando no CI) para o tempo do carimbo; com árvore diferente, é o build completo de hoje.
- Cada push de PR publica até 170 MB de camada nova (api 100, worker 60, web 7); o pacote é público e a limpeza apaga em 2 dias.
- Reverter o workflow devolve o comportamento da #3323; as tags `tree-*` e os `:sha` carimbados ficam inertes.
