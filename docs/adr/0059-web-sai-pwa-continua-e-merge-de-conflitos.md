# ADR-0059 · O EasyStock.Web sai, a PWA continua como backup e conflito vai para um módulo de merge

- Status: Aceito
- Data: 2026-10-08
- Decisor: Felipe
- Emenda: ADR-0056. Supersede o item 7 na parte que mantinha o `EasyStock.Web` para o bastidor
  (estoque, compras, financeiro, etiquetas) e a alternativa descartada "Aposentar o EasyStock.Web
  inteiro". O resto do ADR-0056 continua valendo: tenant único, front único no `EasyStock.Console`,
  sala de módulos, permissão por perfil × módulo e roupa da Casa da Baba.
- Relacionados: ADR-0054 (console no repositório), ADR-0057 (notificações de plataforma), issues
  #1459, #1458, plano `docs/plan/erp-casa-da-baba/`

## Contexto

Em 01/10 o ADR-0056 fez do `EasyStock.Console` o front único da operação, mas deixou o
`EasyStock.Web` (Razor) de vez para as funções de bastidor. Em 08/10 o Felipe revisou essa parte:
"Estamos refazendo o ERP e vamos só aproveitar os conceitos que deram certo."

Fatos medidos no master `888eeab7`:

- O Web é cliente fino da API. As classes de `EasyStock.Web/Services/` chamam a API pelo
  `ApiClient.cs`, em rotas como `categorias` (`CategoriasService.cs`), `produtos/{id}/variacoes`
  (`ProdutosService.cs`), `minha-vitrine/cardapio` e `uploads/cardapio-item/{id}/foto`
  (`CardapioService.cs`), `lotes` (`LotesService.cs`, `EntradasService.cs`) e
  `etiquetas/templates` (`EtiquetasService.cs`). A regra de negócio mora na API, não no Razor.
- A PWA (`EasyStock.Api/wwwroot/pwa`) é offline-first: fila local (`queue-store.js`), service
  worker (`sw.js`) e sincronização por `POST /api/mobile/sync` e `GET /api/mobile/sync/pull`
  (`Mobile/Controllers/SyncController.cs`). Ela é usada todo dia na produção e no cadastro.
- A sincronização tem defeitos abertos que fazem a PWA e o servidor divergirem: entrada de estoque
  em dobro ao sincronizar lote (#1458), descarte de lote que não persiste, custo e estoque mínimo
  descartados pelo servidor e precache incompleto do service worker.

## Decisão

### D1. O `EasyStock.Web` sai inteiro

Nenhuma tela do Web será usada. Cardápio, produção, estoque, compras, financeiro, etiquetas e
relatórios vão para o `EasyStock.Console`, dentro dos módulos do ADR-0056.

- As telas antigas são **referência de regra de negócio**, não código a portar.
- A API continua sendo o backend. Cada endpoint que o Web usa é avaliado quando a tela nova do
  console for feita: fica, muda ou sai.
- Supersede o trecho do ADR-0056 que mantinha o Web para o bastidor.

### D2. A PWA de produção e cadastro continua, como backup offline

A PWA opera junto ao console, em tempo real, e é o backup quando faltar luz ou rede. O console
**não terá offline próprio**.

- A sincronização bidirecional vira contrato de primeira classe: o que o console grava precisa
  chegar à PWA, e o que a PWA grava precisa chegar ao console.
- Bug de sync ou de offline da PWA é prioridade, porque quebra o backup no dia em que ele é
  necessário. Exemplos abertos: #1458 (entrada de estoque em dobro), descarte de lote não
  persistido, custo e estoque mínimo descartados pelo servidor, precache incompleto do service
  worker.

### D3. Conflito vai para um módulo de merge, não para "último vence"

Quando a mesma entidade for editada no console e na PWA offline, o servidor não sobrescreve uma
versão com a outra.

- O conflito entra numa **fila de conflitos** e gera aviso pelo motor interno de notificações
  (ADR-0057).
- A usuária master (Thatiane, a dona) decide, numa tela amigável do console, qual versão fica.
- Até ela decidir, **vale a versão do servidor**; a versão da PWA fica estacionada na fila.
- Isso exige: versão por entidade no sync (a PWA envia a versão-base que editou), tabela de
  conflitos, endpoints para listar e resolver, e a tela. O desenho técnico fica para spec própria.

## Alternativas descartadas

- **Manter o Web para o bastidor (ADR-0056 item 7).** Mantém dois fronts com duas linguagens
  visuais e duas manutenções, num ERP que está sendo refeito.
- **Portar as telas Razor para o console.** Traz junto o que não deu certo; o que se aproveita é a
  regra, e ela já está na API.
- **Offline no próprio console.** Duplica o que a PWA já faz e cria um terceiro lado no conflito.
- **"Último vence" no sync.** Perde em silêncio o que foi feito offline, justamente no dia em que
  o backup foi usado.

## Consequências

- O `EasyStock.Web` entra em desligamento por área: cada área sai quando a tela equivalente do
  console estiver em uso. Até lá ele segue no ar como está.
- A sincronização da PWA ganha prioridade de bug de produção.
- Mudança de schema para versão por entidade e tabela de conflitos é migration, portanto tier alto
  (ADR-0055).
- Decisões do plano que pressupunham o Web vivo precisam ser revistas (ver Pendências).

## Pendências

1. **Ordem de migração das áreas do Web** para o console (cardápio, produção, estoque, compras,
   financeiro, etiquetas, relatórios) e o critério de desligamento de cada uma.
2. **Spec do módulo de merge**: versão por entidade no sync, tabela de conflitos, endpoints de
   listar e resolver, tela da dona e o aviso pelo ADR-0057.
3. **D-M1 e D-M2** dos planos `docs/plan/erp-casa-da-baba/02-m1-cardapio.md` e
   `03-m2-producao.md`, ainda abertas, agora decididas já sabendo que as telas vão para o console e
   que a PWA continua.
4. **Itens do plano que citam o Web ou a saída da PWA**: DM7-1 (parâmetros de estoque "ficam no
   Web"), D-M5-02 e D-M5-07 (caixa e push da PWA) e a Fase B ("aposenta a PWA do caixa"). A decisão
   de 08/10 trata da PWA de produção e cadastro; o destino da parte de caixa dentro dela precisa
   ser confirmado pelo Felipe.
