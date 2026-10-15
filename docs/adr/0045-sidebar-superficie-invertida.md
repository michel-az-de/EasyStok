# ADR-0045 — Sidebar e superficie invertida permanente

- Status: Proposed
- Data: 2026-07-19
- Contexto: epico #966 (rodada de UX do QA v1.10); resolve a divergencia com #492
- Relacionado: ADR-0032 (menu lateral: IA + favoritos), #534 (varredura premium de UX/UI), #975

## Contexto

Uma rodada de QA de UI/UX apontou: *"no modo claro, a sidebar permanece escura, criando
uma tela 'meio claro, meio escuro'. Pode ser intencional (sidebars escuras sao comuns),
mas hoje parece inacabado — vale decidir e documentar o padrao."*

O pedido e legitimo, mas a premissa de que seja descuido nao se sustenta na medicao. E,
mais importante: **os dois produtos da casa trataram o mesmo padrao de formas opostas.**

### Evidencia de que a sidebar escura e deliberada

1. O tema **claro** tem um override proprio para a sidebar (`app.css`), com gradiente
   slate e um *sheen* de luz no topo via `::before` — nao e a regra base vazando.
2. O tema **escuro** faz o oposto: **desliga** o sheen e cai no `--navy-900` chapado.

Ou seja, no claro a sidebar e mais clara e iluminada; no escuro, navy chapado. Sao dois
tratamentos de material desenhados um contra o outro — heranca acidental nao produz isso.

### Por que mesmo assim parecia inacabado

O rastro escrito da decisao estava **orfao**. O comentario `/* Sidebar sempre dark */` e
as regras que ele encabeca miram `.ni` / `.ni.active` / `.ni-sub` — **classes mortas**. A
sidebar viva e renderizada pelo `EsSidebarTagHelper` como `.es-ni` / `.es-ni-row` /
`.es-ni-lbl`. A documentacao documentava codigo que ninguem executa, e nenhum override de
tema claro atingia os itens vivos.

Somava-se a isso uma divida real de contraste (tratada a parte em #975): o cinza dos itens
reprovava WCAG AA nos **dois** temas. Uma sidebar de contraste baixo *le* como inacabada,
independente da decisao estetica.

### A divergencia entre produtos

O Admin abriu **#492 — "[ADMIN] Tema claro quebrado: sidebar, abas e botoes permanecem
escuros"**, classificado como `bug`. O Web trata o mesmo comportamento como design. Sem
uma decisao unica, os dois produtos continuam divergindo por acidente, e cada rodada de QA
reabre a discussao.

## Decisao

**A sidebar e uma superficie invertida permanente, em ambos os temas, em todos os produtos
da casa (Web, Admin, PWA).** Ela nao acompanha o tema da aplicacao.

Consequencias praticas:

1. A sidebar mantem fundo escuro no tema claro. O gradiente slate + sheen do tema claro e a
   forma canonica; o navy chapado e a forma do tema escuro.
2. **Contraste nao e negociavel.** Ser superficie invertida nao isenta de WCAG AA: todo
   texto de item de menu deve atingir 4.5:1 contra a superficie mais clara do gradiente
   (a pior das duas), e 4.5:1 contra o navy do tema escuro.
3. **#492 muda de escopo**, de "corrigir tema" para "corrigir contraste". O Admin nao deve
   clarear sua sidebar no tema claro.
4. Componentes dentro da sidebar nao herdam tokens de superficie (`--bg-surface`,
   `--text-primary`): eles vivem sobre superficie invertida e precisam de valores proprios.

## Alternativas consideradas

**A. Sidebar acompanha o tema** (o que a QA supos ser o esperado, e o que #492 pede como
esta escrito). Rejeitada: descarta um tratamento de material deliberado dos dois temas, e o
custo real e reescrever todos os estados de item (default / hover / ativo / badge / favorito
/ rail) para tokens theme-aware, com revisao visual dupla — sem que nenhum problema de
usabilidade medido seja resolvido por isso. O que a QA de fato sentiu era contraste.

**B. Deixar cada produto decidir.** Rejeitada: e o estado atual, e ele produz exatamente a
divergencia que gerou #492. Uma decisao nao tomada aqui volta como bug relatado la.

**C. Nao decidir agora e so corrigir o contraste.** Rejeitada como decisao final, embora o
fix de contraste tenha sido de fato separado (#975) para nao ficar refem de aprovacao. Sem o
registro, a proxima rodada de QA reabre o mesmo ponto.

## Consequencias

Positivas:
- Padrao unico entre Web, Admin e PWA; #492 ganha escopo correto.
- O rastro da decisao passa a viver num ADR, nao num comentario sobre classe morta.
- Contraste AA vira requisito explicito da superficie invertida.

Negativas / riscos:
- Contraria a expectativa de parte dos usuarios de que "tema claro" signifique a tela inteira
  clara. Mitigacao: e padrao difundido em software operacional, e a sidebar segue sendo a
  regiao de navegacao, nao de conteudo.
- Exige disciplina: qualquer componente novo colocado na sidebar precisa de cor propria, e
  nao pode simplesmente herdar tokens de superficie.

## Acoes decorrentes

- [ ] Reescopar #492 para "corrigir contraste da sidebar do Admin" (nao clarear).
- [ ] Remover ou reapontar o comentario e as regras orfas de `.ni` / `.ni-sub` em `app.css`.
- [ ] Auditar a sidebar do Admin e do PWA contra o criterio de 4.5:1.
