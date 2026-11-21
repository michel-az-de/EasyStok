# Spec: separar CSS e JS do `index.html` da PWA (#2892)

- Data: 2026-11-19
- Origem: auditoria Impeccable do front (`docs/dev/auditoria-impeccable-front-2026-11-19.md`), achado P2
- Tier: alto (feat sem spec até aqui; mexe no service worker, no `MobileModule.cs` e no empacotamento do APK)
- Estado: proposta, aguarda a label `aprovado`

## 1. Problema medido

| Medida | Valor | Fonte |
|---|---|---|
| `index.html` em disco | 1.067.060 B, 24.698 linhas | `wc -c`, master 416d11b7 |
| Servido em produção (br) | 377.383 B | `curl` em `api.easystok.online/pwa/index.html` |
| CSS inline (1 bloco `<style>`, linhas 17 a 7251) | 217.757 B (20%) | `awk` |
| JS inline (bloco principal nas linhas 8916 a 23937, mais 2 menores) | 715.731 B (67%) | `awk` |
| Marcação HTML das telas | cerca de 133.500 B (13%) | diferença |
| Commits que tocaram o arquivo em 30 dias | 31 | `git log --since` |

Hoje, mudar uma vírgula no CSS invalida os 377 KB inteiros. Isso acontece mais ou menos uma vez por dia.

## 2. Achados que mudam a issue

1. **Hash no nome sozinho não economiza nada.** O `sw.js` usa um único cache, `CACHE_VERSION`. O Dockerfile carimba nele o hash de todos os arquivos da PWA, e o `activate` apaga todo cache com outro nome (`sw.js:75`). Por isso, qualquer deploy baixa tudo de novo. O ganho real exige separar o cache em dois:
   - `shell`: guarda `index.html` e `sw.js` e é renovado a cada versão.
   - `assets`: guarda as URLs com hash. Sobrevive às trocas de versão e só é podado do que o manifesto atual não referencia.
2. **O APK quebraria em silêncio.** O `casa-da-baba-mobile/apk/scripts/copy-web.cjs` faz `replace` e `replaceAll` no `index.html`, e vários dos textos substituídos estão dentro do JS inline:
   - "Cadastre o primeiro produto", na linha 12665;
   - "PWA antigo" e "PWA de origem", nas linhas 10174, 10361 e 10507;
   - "Forçar flush" e "Forçar pull", nas linhas 16511 e 16532.

   Depois da extração, essas substituições deixariam de casar sem erro nenhum, e o APK voltaria a mostrar o texto antigo.
3. **A meta de 50 KB é inalcançável sem reescrever as telas.** Só a marcação ocupa cerca de 133 KB. Levá-la para templates em JS mudaria o comportamento e não cabe numa mudança "sem mudança de comportamento". A proposta é que a meta passe a ser no máximo 150 KB em disco.
4. **Ganho que a issue não citava: arrancada mais rápida.** O V8 guarda em cache o código compilado de script externo, mas não de script inline. Hoje os 716 KB de JS são analisados do zero a cada abertura no celular do balcão. Isto é inferência e será medido na fase 0.

## 3. Abordagem

Não haverá bundler: a PWA continua sem etapa de build no Node. Será reaproveitado o padrão que já existe, em que o Dockerfile carimba o `CACHE_VERSION` com um hash de conteúdo.

```
fonte (repo)                         imagem (Dockerfile)                 navegador
index.html  ─┐                       index.html  (?v=<hash8> carimbado)   shell-<ver>  (renova a cada deploy)
css/app.css ─┼─ carimbo de hash ───► css/app.css                          assets       (só baixa o que mudou)
js/app-*.js ─┘                       js/app-*.js
```

1. **Extração literal e na mesma ordem.**
   - O bloco `<style>` vira `css/app.css`.
   - Cada bloco `<script>` inline vira um arquivo clássico (`js/app-nucleo.js` e os demais), carregado no mesmo ponto do HTML, sem `defer` nem `module`.
   - Scripts clássicos dividem o escopo global, então os 395 `onclick=""` e as globais continuam funcionando.
   - Nenhuma linha é editada na extração. A diferença tem de ser só de mudança de lugar.
2. **Versão por conteúdo.**
   - Os `?v=AAAAMMDDx` manuais dos 12 scripts externos de hoje saem.
   - Um passo no Dockerfile reescreve cada `src` e `href` local para `?v=<sha256[0..8]>` do próprio arquivo, junto com o carimbo atual do `sw.js`.
   - O nome do arquivo não muda, o que mantém simples o `copy-web.cjs` e os testes.
3. **Cabeçalhos** (`MobileModule.cs`, `OnPrepareResponse`):
   - Pedido com `v=` igual ao hash do arquivo recebe `Cache-Control: public, max-age=31536000, immutable`.
   - `index.html` recebe `no-cache`.
   - O `sw.js` continua como está.
4. **Service worker com dois caches.**
   - `shell-<CACHE_VERSION>`: `index.html` em rede primeiro, como hoje.
   - `assets`: cache primeiro para URLs com `v=`. No `activate`, é podado do que o `index.html` novo não referencia.
   - O comando remoto `pwa_update` (`sync.js:2337`) continua apagando tudo.
5. **APK.**
   - O `copy-web.cjs` passa a aplicar as substituições de texto em `index.html` e em `js/*.js`.
   - Cada substituição falha alto quando não casa (`throw`), como já acontece com `sync.js` e com as fontes.

## 4. Fases e gates

| Fase | Entrega | Gate objetivo | Abandono |
|---|---|---|---|
| 0 | Linha de base: tempo até a tela de Início no celular do balcão (Chrome remoto, 3 aberturas a frio e 3 a quente), bytes por deploy | Números anotados nesta spec | — |
| 1 | `copy-web.cjs` com substituição que falha alto, ainda com o HTML único | Build do APK verde e teste novo que exige que toda substituição case | — |
| 2 | Extração do CSS e do JS (item 3.1) e testes adaptados | `tests/pwa` com 520 passando; diferença sem linha alterada fora do movimento; detector do Impeccable sem achado novo | Algum teste exigir mudança de lógica para passar |
| 3 | Carimbo `?v=<hash>` e cabeçalhos (itens 3.2 e 3.3) | `curl -I` mostra `immutable` no CSS e no JS e `no-cache` no HTML | — |
| 4 | Service worker com dois caches (item 3.4) | Teste: deploy que só muda o CSS baixa apenas o `app.css` (contar pedidos no `sw-offline.test.js`); funciona offline depois da troca | A troca deixar a PWA sem funcionar offline em qualquer teste |
| 5 | Validação no ar | Repetir as medidas da fase 0; abrir PWA e APK em produção nas telas Início, Produção, Pedidos e Etiqueta | Arrancada pior que a linha de base |

Cada fase é uma PR. A fase 2 vai sozinha porque gera a maior diferença e precisa de revisão por movimento (`git diff --color-moved`).

## 5. Testes que mudam

- O `tests/pwa/run.js` já carrega os scripts locais com `src`, então ele passa a carregar os `js/app-*.js` sem mudança de lógica.
- 15 arquivos de `tests/pwa` leem o `index.html` como texto, para procurar CSS ou JS por regex. Eles passam a usar um helper novo, `fontePwa()`, que junta o `index.html` e os arquivos extraídos. A asserção de cada teste não muda.
- Testes novos:
  - toda substituição do `copy-web.cjs` precisa casar;
  - todo `src` e `href` local tem `?v=` igual ao hash do arquivo;
  - o service worker poda só o que deixou de ser referenciado.

## 6. Riscos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Texto do APK volta ao antigo em silêncio | Alta sem a fase 1 | Médio | A fase 1 vem antes da extração |
| Ordem dos scripts muda e uma global fica indefinida | Baixa | Alto (tela branca) | Extração no mesmo ponto, sem `defer`; `run.js` executa na ordem real |
| Celular fica preso em asset antigo | Média | Alto | `?v=` por conteúdo e `immutable` só com o hash certo; `pwa_update` limpa tudo |
| CSP | Nula | — | `'self'` já cobre arquivo externo; nada precisa entrar na CSP |
| Conflito com PR aberta que mexe no `index.html` | Alta | Médio | A fase 2 entra numa janela combinada, com master congelado para a PWA durante a PR |

## 7. Rollback

- Fases 1, 2 e 3: `git revert` da PR e `vps-deploy.sh`. O HTML único volta inteiro.
- Fase 4:
  - `revert` do `sw.js`. O `CACHE_VERSION` novo apaga o cache `assets` no `activate` antigo, porque o nome dele é diferente.
  - Se algum celular travar, o comando remoto `pwa_update` resolve.

## 8. Aceite revisto (substitui o da issue)

- [ ] `index.html` com no máximo 150 KB em disco (era 1.067 KB)
- [ ] CSS e JS em arquivos próprios, com `?v=` por conteúdo e `immutable`
- [ ] Deploy que muda só o CSS faz o celular baixar só o CSS (teste da fase 4)
- [ ] O APK empacota sem perder nenhuma substituição de texto (teste da fase 1)
- [ ] `tests/pwa` verde, sem asserção enfraquecida
- [ ] Arrancada no celular do balcão igual ou melhor que a da fase 0

## 9. Fora do escopo

- Dividir o JS por tela ou com carregamento tardio.
- Minificar.
- Mexer no `EasyStock.Web`.
- Mudar layout ou texto.

## 10. Lacunas

- Quantos aparelhos usam a PWA no navegador e quantos usam o APK. Se forem só APKs, o ganho de rede some e fica apenas o da arrancada. Medir na fase 0, pelo `/api/mobile/version` ou pelo registro de aparelhos.
- O ganho de arrancada (item 2.4) ainda não foi medido.
