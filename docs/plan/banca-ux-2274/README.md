# Banca UX/UI da Casa da Babá (#2274)

Fonte: "Auditoria UX/UI da Casa da Babá" (PDF, 27 páginas), feita em produção (app.easystok.online) em
06/11/2026, Chrome desktop 1429×840 e largura estreita, temas claro e escuro. Base do código lido:
`origin/master` `41333438`, o mesmo commit da produção auditada. Este documento é a spec dos 121 itens:
o que muda, o que já atende, o que fica de fora e o que falta verificar no produto.

| Issue | Papel |
|---|---|
| #2274 | Esta spec |
| #2261 | Sobrepõe: layout-mestre único (P7), `motivo` nos botões desabilitados (P6), "(referência X)" no corpo dos avisos (03·6) |
| #2275 | Já aberta: Caixa, aviso de R$ 282 e KPIs (P2) |
| #2276 | Já aberta: Entregas, viagens vazias e contato sem nome (P4) |

Caminhos do Console são relativos a `EasyStock.Console/src/`; os demais levam o caminho completo. O
relatório é só leitura de código: nenhum item foi renderizado, então o que depende de cor, alinhamento
ou truncamento entra como **V**.

## Decisões de base

| Tema | Decisão |
|---|---|
| Cardápio × Estoque | Aviso "sem saldo" no Cardápio. A disponibilidade segue manual (RN-48: venda sem saldo avisa, não trava) e não deriva do estoque (Felipe) |
| Grafia para o cliente | Segue "Casa da Baba". Nenhum UPDATE de grafia no banco nem mudança nos defaults em C# (Felipe) |
| Logo no escuro | Segue em placa clara (#1328). Só existe PNG, sem SVG da marca. O relatório pede SVG transparente; fica de fora até o Felipe enviar um (Felipe) |
| Escopo | P0 a P8. P9 (recursos novos) fora, com spec própria (Felipe) |
| "∴" no nome | Faz parte do nome que o cliente usa no WhatsApp (#2232). Sem mudança (14·5) |
| Vencido em âmbar | #2235: lote vencido é âmbar, sem saldo é vermelho. Sem mudança (12·1). Lote é um por registro de produção, por desenho (12·3) |
| Botão desabilitado tracejado | #2258 P6: tracejado fica; ganha o `motivo` escrito (09·3, via #2261) |
| Filtros do Cardápio | Contagens cruzadas entre select e chips são por decisão (`dominio/filtroDoCardapio.js:43-44`) (10·3) |
| Homologação | Só o SuperAdmin vê (`features/gestao/canais/TelaCanais.jsx:85`) (16·7) |

## Como ler

Estado: **C** corrige nesta entrega, **J** já atende (com `arquivo:linha`), **F** fica de fora (com
motivo), **V** verificar no produto em 390 e 1440 px (com a verificação exata; só quando o código não
prova). Pacote: P1 a P8 conforme a seção "Pacotes"; P9 e "decisão" marcam o que fica de fora.

O relatório numera as telas de 01 a 19. Quatro cuidados na contagem:

| Cuidado | Tratamento |
|---|---|
| Linhas sem número (com travessão no lugar do número) no relatório | Entram como `NN·0` (03·0, 05·0, 12·0) |
| "9–10" na tela 01 | Uma linha só, como no relatório (`01·9-10`) |
| Tabelas que continuam na página seguinte | A linha pertence à tela da tabela, não ao título que aparece antes dela no PDF |
| Os itens 2 a 6 impressos sob o título "18" na página 21 | São da tela 17 (17·2 a 17·6) |

| Estado | Itens |
|---|---|
| C corrige | 92 |
| J já atende | 1 |
| F fica de fora | 16 |
| V verificar | 12 |
| **Total** | **121** (5 críticos, 27 altos, 57 médios, 32 baixos: fecha com o resumo do relatório) |

## Achados por tela

### 01 Home (desktop), 9 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 01·1 | Média | Logo do header fora do grid (x=24), marca pequena | C | P7 | Header no mesmo `max-width` do conteúdo (`hall/HallDeModulos.jsx:177-179`, `hall/hall.module.css:9-13` x `:39`). A logo segue em PNG, placa clara (#1328); só o tamanho mínimo de 32 px se garante (`hall.module.css:20`, `componentes/Marca.module.css:4`) |
| 01·2 | Média | Sino com badge vermelho e bolinha marrom coladas | C | P8 | Um indicador só; "avisos desligados" vai para tooltip e `aria` (`notificacoes/SinoDaSessao.jsx:52-53`, `notificacoes.module.css:19`) |
| 01·3 | Baixa | "8 precisam de você" 3 vezes na dobra | C | P8 | Resumo do dia sem a frase repetida (`dominio/estadoDoHall.js:74-76`, `dominio/pulsoDoHall.js:13`) |
| 01·4 | Alta | "8 / Precisa de você" sem concordância; card esticado | C | P8 | Rótulo com `precisaDeVoce(n)` (`pulsoDoHall.js:14`, hoje fixo em `:13,72`); cartões sem `stretch` (`hall.module.css:182-183`) |
| 01·5 | Média | "0 Prontos para sair" com o mesmo vazio forçado | C | P8 | Mesma correção do 01·4 (`hall.module.css:182-183`) |
| 01·6 | Média | Alerta do caixa em âmbar, cor da marca; palavra órfã | C | P5 | Matiz de alerta afastado da marca (`estilos/tokens.css:51,55` x `:67`, 12,2 graus) e texto sem a palavra órfã (`pulsoDoHall.js:89-90`) |
| 01·7 | Alta | "2 lotes vencidos" com a mesma cor de "26 pratos com pendência" | C | P3 | O que separa é tirar "pratos com pendência" do tom de atenção (ver 10·5, `pulsoDoHall.js:48-51`). Lote vencido segue âmbar com ícone (#2235; `pulsoDoHall.js:57`). Felipe confirma na aprovação desta spec |
| 01·8 | Baixa | Duas redações para o caixa de outro dia | C | P8 | Uma string: "Caixa de outro dia ainda aberto" (`pulsoDoHall.js:44` x `:89-90`) |
| 01·9-10 | Baixa | Mesma engrenagem para "Ajustar tela inicial" e "Configurações"; links soltos no rodapé | C | P7 | Ícone próprio para Ajustar (`HallDeModulos.jsx:290`, `componentes/MarcaIcone.jsx:47`, `dominio/modulos.js:115`) e atalho no menu da conta (`HallDeModulos.jsx:296-298`). O relatório conta "9-10" como uma linha |

### 02 Busca da Home, 4 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 02·1 | Média | "X" de limpar nativo, azul, fora da paleta | C | P6 | `appearance: none` e ícone próprio no campo de busca (`HallDeModulos.jsx:203`, `hall.module.css:24-25`). `caixa-de-entrada/caixa.module.css:165` já esconde o X nativo e serve de modelo |
| 02·2 | Baixa | "X" e "Limpar busca" fazem a mesma coisa; layout shift | C | P8 | Manter só o X dentro do campo (`HallDeModulos.jsx:209`) |
| 02·3 | Baixa | "1 tela encontrada." longe do campo | C | P8 | Resultado dentro de `.busca`, junto do campo (`HallDeModulos.jsx:245-247` x `:200-210`) |
| 02·4 | Média | Resultado substitui o dashboard; sem ⌘K | F | P9 | Command palette é recurso novo (`HallDeModulos.jsx:212,220-227`; nenhum ⌘K no Console). Spec própria |

### 03 Avisos, 9 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 03·0 | Média | Modal central bloqueante, longe do sino | F | P9 | Popover ou drawer ancorado é a "central de avisos" do P9 (`SinoDaSessao.jsx:55`; `componentes/Popover.jsx:41` existe, mas o desenho do drawer pede spec) |
| 03·1 | Baixa | Regra de negócio como subtítulo permanente | C | P8 | Vira tooltip de ajuda (`SinoDaSessao.jsx:55`) |
| 03·2 | Média | "1 aviso não lido" pesa mais que o título | C | P8 | Contador como badge ao lado do título (`SinoDaSessao.jsx:61`) |
| 03·3 | Baixa | "Mostrando 1 de 1" esconde a fila | F | P9 | Lista completa com abas Não lidos e Todos é recurso novo (`SinoDaSessao.jsx:62`) |
| 03·4 | Média | "Ativar avisos neste aparelho" como botão fantasma | C | P8 | `variante="primario"` (`SinoDaSessao.jsx:42`; padrão `secundario` em `componentes/Botao.jsx:17`) |
| 03·5 | Média | Severidade com bolinha de 6 px, sem horário | C | P8 | Chip de severidade com cor (`SinoDaSessao.jsx:66-71`). O "há 5 min" só entra se a API de avisos devolver a data (inferido: o código do Console não lê data); se não devolver, fica para a spec da API |
| 03·6 | Média | Título repetido no corpo e "(referência 6BB73F77)" | C | P8 | Corpo sem título repetido nem referência; CTA "Responder {nome}" (`EasyStock.Api/Data/NotificacoesGlobaisSeed.cs:648,658,668`). Linhas já gravadas não mudam sozinhas (inferido): UPDATE só com GO. Sobrepõe o A7 da #2261 |
| 03·7 | Média | "Atualizar avisos" manual | F | P9 | Tempo real é canal novo (sem `EventSource`, WebSocket ou SignalR no Console) (`SinoDaSessao.jsx:56`) |
| 03·8 | Baixa | "Fechar" duplica o X | C | P8 | Remover o botão do rodapé (`SinoDaSessao.jsx:56`, `componentes/Modal.jsx:29`) |

### 04 Tema escuro e menu da conta, 4 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 04·1 | Crítica | Logo PNG com fundo branco vira retângulo no escuro | F | decisão | Decidido: logo em placa clara (#1328; Felipe, D3 opção A). `componentes/Marca.module.css:2` pinta `var(--branco)` de propósito; só existe PNG, sem SVG da marca. Revisão só se o Felipe enviar um SVG transparente |
| 04·2 | Média | Badge vermelho vira rosa-salmão; bolinha vira amarela | C | P5 | Tokens de badge e ponto testados nos dois temas (`componentes/Contador.module.css:10` com `--erro`, `tokens.css:68`; `notificacoes.module.css:19` com `--alerta`, `tokens.css:67`) |
| 04·3 | Alta | Card sobre página: contraste 1,03:1 no escuro | C | P5 | Papel com +6-8% de luminância e borda `--linha` (`tokens.css:36-37` medido 1,084 e 1,026; `hall.module.css:61` com borda transparente) |
| 04·4 | Média | Ilustrações marrom sobre marrom no escuro | C | P5 | Ilustração com variante escura. Quase tudo já segue o tema (`IlustracaoDoModulo.jsx`, `hall.module.css:59-60`); `--il-ouro` é `var(--trigo)` fixo (`tokens.css:59`) |

### 05 Atendimento, menu lateral e lista, 11 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 05·0 | Alta | Home sem menu; módulos com menu; ordem diferente da Home | C | P7 | Um shell só (`app/App.jsx:274` x `:431,:503`). A ordem dos módulos já é a da Home (#2258), falta o shell. Sobrepõe o "layout-mestre único" da #2261 |
| 05·1 | Média | Logo com caixa branca sobre o rail bege | F | decisão | Mesma decisão do 04·1 (#1328, placa clara) (`app/Moldura.jsx:185,278`) |
| 05·2 | Média | "Loja aberta" parece item de navegação ativo | C | P7 | Tirar do trilho (`app/Moldura.jsx:281-292`, classe `lojaTrilho`); o chip do topo (#2232) fica como único controle |
| 05·3 | Média | "Respostas ..." truncado no rail de 68 px | C | P8 | Rótulo de até 10 caracteres: "Respostas" (`dominio/modulos.js:23`; `componentes/Trilho.module.css:27` corta com reticências). "Configurações" (13 caracteres, `modulos.js:115`) exige abreviação ou só ícone com tooltip: decidir no P7 |
| 05·4 | Média | Mesmo ícone de grade 2x2 para "Gestão" e "Disposição" | C | P8 | Ícone próprio para cada um (`app/Moldura.jsx:217` e `:252`, ambos `painel`) |
| 05·5 | Baixa | Bolinha e badge sobrepostos no sino | C | P8 | Mesmo conserto do 01·2 |
| 05·6 | Média | "3 mensagens não entregues" rosa sobre rosa, em 2 linhas | V | P8 | Faixa compacta com "Revisar" e dispensa já existe (#2232 L6; `caixa-de-entrada/NaoEntregues.jsx:160-168`). Verificar: conversa com falha, 1440 px, texto em 1 linha e contraste do texto sobre o fundo >= 4,5:1; se falhar, vira C no P8 |
| 05·7 | Alta | Coluna de 260 px, nomes truncados | C | P8 | O padrão da coluna é 320 px (`tokens.css:151`), mas no layout de três colunas ela encolhe até 260 (`app/moldura.module.css:74`). Subir o mínimo e pôr o nome em linha própria (`componentes/NomeDoContato.module.css:8`) |
| 05·8 | Alta | Tag "Restrição alimentar" em disparo de marketing | F | domínio | Classificador de tags é domínio e pede spec própria (`dominio/agente.js:8,28`, `dominio/automatico.js:154,398`, `Domain/Entities/ClienteTag.cs:20`). O gatilho exato da campanha não foi reproduzido (inferido pelo código) |
| 05·9 | Média | Chip "Sem nom" e "· #9" órfão | C | P8 | Telefone formatado quando não houver nome (`dominio/nomeDoContato.js:32`, `componentes/NomeDoContato.jsx:5`) |
| 05·10 | Baixa | Prévia cortada em ~15 caracteres | V | P8 | O componente da prévia não foi localizado em `caixa-de-entrada`. Verificar em 1440 px a largura da prévia da lista; se a causa for a coluna estreita, o 05·7 resolve |

### 06 Atendimento, conversa e ficha, 9 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 06·1 | Média | Contato exibido como "Cliente WhatsApp" | C | P4 | Padrão vira telefone formatado (`EasyStock.Application/UseCases/Atendimento/IdentificarClientePorTelefoneUseCase.cs:32`; `aplicacao/api/cliente.js:164` já usa o nome conhecido, #1643). Renomear leads antigos: dado, só com GO |
| 06·2 | Baixa | "Outro atendente" solto sob o header | C | P8 | "Atendido por: Nome" (`dominio/resumoDoModo.js:51`, `dominio/linhaDoBalcao.js:16`) |
| 06·3 | Alta | Negrito do WhatsApp aparece com asteriscos | C | P8 | Renderizar `*negrito*`, `_itálico_`, `~riscado~` em `TextoComLinks` (`features/atendimento/Balao.jsx:53,58-65`) |
| 06·4 | Média | URL longa não clicável | C | P8 | Linkificar qualquer URL e encurtar a exibição (`Balao.jsx:58-65` linkifica só cardápio e pagamento) |
| 06·5 | Média | Janela fechada: compositor parece ativo | C | P6 | Estado desabilitado claro e botão "Escolher modelo aprovado" (`features/atendimento/Composer.jsx:269,274`). Sobrepõe o `motivo` da #2261 |
| 06·6 | Média | Três entradas para "Assistente" | V | P8 | O #2232 A4 juntou "Devolver ao Assistente" num menu (`PainelAtendimento.jsx:206-207`); a entrada flutuante não foi localizada. Verificar em 1440 px quantas entradas do Assistente há na conversa; se mais de uma, unir |
| 06·7 | Média | Dois menus de ações para o mesmo objeto | V | P8 | Existem "Ações" (`features/ficha-cliente/MenuDaFicha.jsx:30`) e "Ações da conversa" (`PainelAtendimento.jsx:194`). Verificar em 1440 px se repetem ações; se sim, um menu por objeto (cliente na ficha, conversa no header) |
| 06·8 | Baixa | Disclosure misto na ficha (">", "v", "+ Nota") | V | P6 | "+ Nota" não foi localizado (`ficha/ModalHistorico.jsx:167` é outro componente). Verificar a ficha em 1440 px; o acordeão do P6 (ver 08·4) vira o padrão único |
| 06·9 | Baixa | "Orientar" solto, sem affordance | C | P8 | "Pedir orientação" (`features/ficha-cliente/AreaColaborativaIa.jsx:100`) |

### 07 Cozinha, 6 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 07·1 | Crítica | Menu lateral muda de itens e ordem por módulo | C | P7 | Trilho do Atendimento (`app/Moldura.jsx:270-330`) x menu por módulo (`features/hall/MolduraDoModulo.jsx:81`, `MenuDosModulos.jsx:16-20`). Shell único, mesmos destinos e ordem. A ordem já foi alinhada à Home (#2258). Sobrepõe a #2261 |
| 07·2 | Baixa | "Cozinha" no header e de novo como H1 | C | P7 | Um título por tela (`features/cozinha/TelaCozinhaApi.jsx:153`, `MolduraDoModulo.jsx:104-106`) |
| 07·3 | Baixa | Dica "Arraste pela alça" com fila vazia | C | P8 | Mostrar só com cartão ou feedback ativo (`TelaCozinhaApi.jsx:222`, `FeedbackArrasto.jsx:8`) |
| 07·4 | Alta | Kanban sem raias, ~600 px de área morta | C | P8 | Fundo por coluna e contador em badge (`features/cozinha/cozinha.module.css:43-56`). Contagem e "Nenhum pedido" por coluna já existem (`TelaCozinhaApi.jsx:244,248-249`). Cronômetro e SLA ficam no P9 |
| 07·5 | Média | Rótulos truncados "Configuraç..." e "Respostas ..." | C | P8 | Mesma correção do 05·3 |
| 07·6 | Baixa | Jargão "Todas as linhas"; filtro ativo em pêssego | C | P8 | "Todos os pedidos" e chip ativo com a cor de ação (`TelaCozinhaApi.jsx:203`) |

### 08 Entregas, painel, 6 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 08·1 | Alta | Duas barras de abas empilhadas | C | P7 | Um nível de abas (`MolduraDoModulo.jsx:107-110` x `features/entregas/ConteudoEntregasApi.jsx:30,82-100`) |
| 08·2 | Média | "Janelas de entrega" nas duas barras | C | P7 | Sai junto com o 08·1 (`dominio/modulos.js:134`, `ConteudoEntregasApi.jsx:30`) |
| 08·3 | Baixa | "Ao vivo" cinza aqui e com ponto na Cozinha | C | P6 | Um componente de status ao vivo (`ConteudoEntregasApi.jsx:70` x `TelaCozinhaApi.jsx:158-161`) |
| 08·4 | Baixa | "Roteiro e impressão" em `<details>` nativo | C | P6 | Acordeão do design system, também para o 10·7 (`features/entregas/EntregasDoDia.jsx:153-154`; não há componente de acordeão) |
| 08·5 | Alta | "Nenhuma entrega neste dia" com 3 viagens "Montando" vazias | C | P4 | API fecha a viagem Montando vazia ao virar o dia; Console filtra por dia (`EasyStock.Infra.Postgre/Repositories/Atendimento/EntregasRepositories.cs:44-46`, `dominio/entregasApi.js:71`). Fechar as 3 já existentes: dado, só com GO |
| 08·6 | Média | `<select>` nativo com 1.240 px | C | P6 | Largura máxima de 360 px no `CampoSelecao` (`componentes/Campo.jsx:52-62`) |

### 09 Entregas, viagens e chamado, 4 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 09·1 | Média | Blocos idênticos com a mesma ajuda 3 vezes | V | P8 | Texto repetido não localizado (candidatos `features/entregas/PainelViagem.jsx:89,119` e `ConteudoEntregasApi.jsx:321-326`). Verificar em 1440 px uma viagem com 3 blocos; ajuda uma vez ou em tooltip |
| 09·2 | Média | Selects nativos em largura total | C | P6 | Mesma correção do 08·6 (`Campo.jsx:57`) |
| 09·3 | Alta | Borda tracejada como estilo de botão | F | P6 | Decisão #2258 P6: botão desabilitado é tracejado (`componentes/Botao.module.css:13-16`). Fica. O motivo escrito no botão ("Saiu para entrega", `ConteudoEntregasApi.jsx:300`) segue na #2261 |
| 09·4 | Alta | Chamado "teste" em produção; ações como texto puro | C | P4 | Cartão com autor, hora e botões (`ConteudoEntregasApi.jsx:330-335`). Arquivar o chamado "teste" (preferir arquivar a apagar): dado, só com GO |

### 10 Cardápio, lista, 7 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 10·1 | Média | Três títulos para uma tela | C | P7 | Header com o módulo, H1 com a seção (`MolduraDoModulo.jsx:104-106`, `MenuDosModulos.jsx:37`, `modulos.js:46`) |
| 10·2 | Alta | Três níveis de navegação; submenu de 15% | C | P7 | Faixa de abas no topo também para módulo só com telas de ajuste (`MolduraDoModulo.jsx:73-75`, `modulos.js:45-50`) |
| 10·3 | Média | Select e chips filtram juntos e mudam os contadores | F | decisão | Contagens cruzadas por decisão (`dominio/filtroDoCardapio.js:43-44`) |
| 10·4 | Baixa | "Limpar filtros" longe dos filtros | C | P8 | Para junto dos chips (`features/gestao/cardapio/AbaCardapioApi.jsx:131` fora de `faixaFiltros` em `:121`) |
| 10·5 | Alta | "Pendência" (descrição < 40) como alerta na Home | C | P3 | Tirar `DescricaoCurta` do alerta e rotular "Sugestões de conteúdo" (`dominio/pulsoDoHall.js:48-51`, `EasyStock.Domain/Entities/Storefront/CardapioItem.cs:377,389`) |
| 10·6 | Média | Variações 500 g e 1 kg em linhas separadas | F | P9 | A lista é uma linha por SKU (`AbaCardapioApi.jsx:152-154`); agrupar é redesenho do Cardápio e pede spec própria. Motivo da linha por SKU (preço e saldo próprios) é inferido |
| 10·7 | Baixa | "Entenda os controles" em `<details>` nativo | C | P6 | Acordeão do design system, igual ao 08·4 (`AbaCardapioApi.jsx:132-135`, `gestaoCardapio.module.css:18`) |

### 11 Cardápio, Editar prato, 6 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 11·1 | Média | Dois estilos de campo (Preço bege sem borda) | C | P6 | Um componente de input (`features/cardapio/EditorItemCardapio.jsx:255-256` x `:267`, `componentes/CampoMascarado.module.css:9-14`) |
| 11·2 | Baixa | Dois secundários lado a lado | C | P6 | Uma variante (`EditorItemCardapio.jsx:227` x `:228`) |
| 11·3 | Alta | Lista diz "Massas recheadas"; formulário diz "Sem categoria" | C | P3 | O editor lê só `SecaoId` (`EditorItemCardapio.jsx:82,183,259`); a lista usa a categoria efetiva, que cai para `CategoriaTexto` (`Domain/Entities/Storefront/CardapioItem.cs:370`). Editor mostra a efetiva. Migrar `CategoriaTexto` para `SecaoId`: dado, só com GO |
| 11·4 | Média | "Até 240 caracteres" sem o mínimo de 40 nem contador | C | P3 | Contador "31/40 mín, 240 máx" (`EditorItemCardapio.jsx:256`) |
| 11·5 | Média | Prévia cortada: "Tortellini de Costela 500" | V | P8 | Candidatos `EditorItemCardapio.jsx:247` (`.previa`) e rodapé `:215`; `editorItem.module.css:6-9,30`. Verificar em 390 e 1440 px o nome longo na prévia; correção provável: `line-clamp` ou rolagem própria |
| 11·6 | Baixa | "Ficha do cardápio" solto no rodapé | C | P8 | Remover o texto de status do rodapé (`EditorItemCardapio.jsx:215`) |

### 12 Produção, Estoque do dia, 6 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 12·0 | Crítica | Cardápio mostra 43 disponíveis; Estoque mostra 0 porções | C | P3 | Decidido: aviso "sem saldo" no Cardápio; disponibilidade segue manual, sem derivar do estoque (RN-48 avisa, não trava) (`infra/api/cardapioApi.js:116`, `dominio/cardapio.js:21,42-48`, `features/cardapio/PainelCardapio.jsx:242,387-407`, `AbaEstoqueDoDiaApi.jsx:13-30`) |
| 12·1 | Alta | "0 porções" vermelho e "lotes vencidos" âmbar | F | decisão | #2235: vencido é âmbar, sem saldo é vermelho (`features/gestao/producao/AbaEstoqueDoDiaApi.jsx:182,242`) |
| 12·2 | Média | Quantidades em colunas diferentes; tabelas sem cabeçalho | C | P8 | Colunas fixas e cabeçalho (sem `<table>` nem `<thead>` em `AbaEstoqueDoDiaApi.jsx`; as posições x=800 e x=727 não foram medidas) |
| 12·3 | Alta | Mesmo código LOT para dois produtos; vencidos pendentes | J | P3 | `detalheDoLote` mostra código e porção na linha (#2235; `aplicacao/estoqueDoDia.js:82-86`, `AbaEstoqueDoDiaApi.jsx:279`). Um lote por registro é por desenho (`RegistrarProducaoUseCase.cs:118-159`, unicidade por empresa e código em `LoteConfiguration.cs:19`). "Escalonar vencidos" não é ajuste de tela e fica de fora |
| 12·4 | Média | Três estilos de botão; lixeira em "Registrar perda" | C | P6 | Ícone de perda no lugar da lixeira; uma variante por nível (`AbaEstoqueDoDiaApi.jsx:290-291`, `AbaPerdasApi.jsx:97,101`, `AbaPlanejamentoApi.jsx:163-164`) |
| 12·5 | Baixa | Chips "Sem saldo" e "Lote vencido" repetem os KPIs | C | P8 | Só na linha (`AbaEstoqueDoDiaApi.jsx:244,246` x KPIs `:181-182`) |

### 13 Produção, Planejamento, 4 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 13·1 | Média | Parede de texto de 4 linhas sobre o algoritmo | V | P8 | Busca por "algoritmo" e "Como o plano" em `features/gestao/producao` sem resultado. Verificar o Planejamento em 1440 px, achar o parágrafo e mover para "?" com popover |
| 13·2 | Média | Nome em x=314 e "Porções" em x=1219, sem cabeçalho | C | P8 | Cabeçalho de colunas e campos na mesma grade (`AbaPlanejamentoApi.jsx:154`) |
| 13·3 | Alta | "Calcular insumos" e "Revisar na produção" ativos sem receita | C | P6 | Desabilitar com `motivo` e atalho "Cadastrar receita" (`AbaPlanejamentoApi.jsx:163-164`, `componentes/Botao.jsx`). O destino do atalho não foi localizado: definir na implementação. Sobrepõe a #2261 |
| 13·4 | Baixa | "Saldo 0" em 12 px cinza claro | V | P8 | Tamanho não localizado no código. Verificar o Planejamento em 1440 px; se confirmar, saldo em coluna própria, peso 600 |

### 14 Financeiro, Caixa do dia, 6 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 14·1 | Média | Status "Aberto" marrom no topo e verde na tabela | C | P2 | Um mapa de status para topo e tabela (`features/gestao/caixa/AbaCaixaApi.jsx:364,476`; a pílula do topo não foi localizada) |
| 14·2 | Média | Dois alertas empilhados, sem ícone nem ação | C | P2 | Alerta com ícone, título curto e botão (`AbaCaixaApi.jsx:440-441`, `dominio/caixa.js:49`) |
| 14·3 | Média | Sete KPIs com rótulos de 1 e 2 linhas | C | P2 | Rótulos curtos com tooltip; valores alinhados pela base (`AbaCaixaApi.jsx:328-347`, classe `resumoEmQuatro`) |
| 14·4 | Crítica | Aviso diz que R$ 282,00 "estão nos totais" que somam R$ 19,00 | C | P2 | O aviso soma cancelados dos últimos 90 dias (`aplicacao/useDevolucoesPendentes.js`, `infra/api/estornosManuaisApi.js:9-14`) e afirma que estão nos totais do dia (`dominio/caixa.js:49`, texto do #2267). O valor do aviso passa a ser a soma das linhas citadas, no mesmo período dos totais, e lista os pedidos. Mesmo aviso em `financeiro/PainelFinanceiro.jsx:276` |
| 14·5 | Baixa | Caractere "∴" após o nome do cliente | F | decisão | Faz parte do nome no WhatsApp (#2232) |
| 14·6 | Média | Parágrafos explicando a tabela acima e abaixo | C | P8 | Notas curtas ou tooltip (`AbaCaixaApi.jsx:349-357` nota da gaveta, `:389-391` aviso acima, `:395` legenda) |

### 15 Financeiro, Visão geral, 5 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 15·1 | Média | Botão "Atualizar" manual | F | P9 | Dados ao vivo é recurso novo, como o 03·7 (`PainelFinanceiro.jsx:72`) |
| 15·2 | Alta | Sem gráfico nem comparação; a pagar e a receber sem cor | F | P9 | Gráfico de fluxo de 30 dias é recurso novo (`PainelFinanceiro.jsx:70-80`; nenhum gráfico em `features/gestao/financeiro`). A cor semântica vem dos tokens do P5 |
| 15·3 | Média | Faixa de atalhos duplica 1:1 o submenu | C | P8 | Remover a faixa (`PainelFinanceiro.jsx:89`) |
| 15·4 | Baixa | "Ver lista ›" sublinhado, terceiro estilo de link | C | P8 | Um estilo de link (`PainelFinanceiro.jsx:49`, classe `verLista`) |
| 15·5 | Baixa | "Vencidas" em caixa cinza, "Vence hoje" sem caixa | C | P8 | Um contêiner por nível (`PainelFinanceiro.jsx:77` x `:78`) |

### 16 Configurações, Canais, 7 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 16·1 | Baixa | Marcadores mistos; três frases dizem "conectado" | C | P8 | Uma linha de status (`features/gestao/integracoes/SecaoWhatsApp.jsx:26-31`, `SecaoWppConnect.jsx:8,42`) |
| 16·2 | Média | H1, H2 e H4 com 20 px/700 | C | P5 | Em Canais é verdade (`SecaoWhatsApp.module.css:11` usado em h3 e h4; `gestao/canais/TelaCanais.module.css:17` em h2). No Console inteiro não: H1 17 px, H4 14-15 px (medido). Escala só onde a lacuna existe |
| 16·3 | Alta | "Desconectar número" é o botão mais forte | C | P6 | Destrutivo em contorno, separado de "Atualizar conexão" (`SecaoWppConnect.jsx:53`). A confirmação inline já existe (`:55-63`, #2258) |
| 16·4 | Alta | "Migrar para conexão oficial" em cinza secundário | C | P8 | `variante="primario"` (`SecaoWhatsApp.jsx:57`) |
| 16·5 | Alta | Risco de bloqueio do número como parágrafo comum | C | P8 | Banner de alerta no topo (`SecaoWppConnect.jsx:43-44,66-67`) |
| 16·6 | Média | Linhas com ~150 caracteres | C | P8 | `max-width` de 68 a 72ch (`SecaoWhatsApp.module.css:12`; só `TelaCanais.module.css:19` tem) |
| 16·7 | Média | "Homologação: testar meu e-mail" exposto em produção | F | decisão | Só o SuperAdmin vê (`gestao/canais/TelaCanais.jsx:85`, `TesteEmailDaSessao.jsx:23`). Que a auditoria usou conta SuperAdmin é inferido |

### 17 Respostas automáticas, 6 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 17·1 | Alta | Terceira versão do menu lateral; "Início" pintado como ativo | C | P7 | Mesma correção do 07·1 (`app/App.jsx:274,431`, `dominio/modulos.js:21`) |
| 17·2 | Média | Liga/desliga como checkbox; o Cardápio usa switch | C | P6 | Um componente switch (`features/gestao/respostas/AbaRespostas.jsx:185-193`; switch em `features/automacoes/automacoes.module.css:109-121` e `cardapio/cardapio.module.css:71-80`) |
| 17·3 | Alta | Mensagem ao cliente com "Casa da Baba" | F | decisão | Decidido: o cliente segue vendo "Casa da Baba" (nome decidido no #1328, `ConfiguracaoAtendimento.cs:22,36`); nenhum UPDATE de grafia. Divergência a resolver: o texto padrão do Console diz "Babá" (`dominio/automacao.js:38`) |
| 17·4 | Crítica | `{linkCardapio}` sai crua para o cliente; "EasyStok" na interface | C | P1 | Ver "P1, desenho do 17·4" abaixo. O texto "EasyStok" fica só na tela da dona (`AbaRespostas.jsx:201-205`), mas sai da interface |
| 17·5 | Baixa | "Editar texto" e "Escrever texto" | C | P1 | Sempre "Editar texto" (`AbaRespostas.jsx:228`) |
| 17·6 | Média | Três linhas em cor de alerta por card | C | P8 | Uma nota neutra; alerta só com risco real (`AbaRespostas.jsx:198-204`, `abaRespostas.module.css:43,54`) |

### 18 Celular (390 px), 8 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 18·1 | Alta | Card "0 Prontos para sair" esticado (~150 px de branco) | C | P8 | Mesma correção do 01·4 (`hall/PulsoDoHall.jsx:63`, `hall.module.css:180-183`) |
| 18·2 | Média | Card de caixa espremido, texto em 3 linhas de 13 px | V | P8 | Regra de linha inteira não localizada. Verificar a Home em 390 px; se o card do caixa não ocupa a linha inteira, corrigir no `hall.module.css` |
| 18·3 | Alta | Header do Atendimento com 6 controles; dois badges "1"; "Loja aberta"; "Auto" | C | P7 | Três controles no header (`app/Moldura.jsx:45-70`, `moldura.module.css:247`). "Loja aberta" e "Auto" com estado já atendem (`Moldura.jsx:148-152`, `rotulosDoTopo.js:6`, `Moldura.jsx:61-68`). Os dois badges "1" não foram localizados: conferir ao final |
| 18·4 | Média | Abas "Conversas" e "Mensagens" são sinônimos | C | P8 | Um nome por tela (`app/rotulosDoTopo.js:27,32,33`, `dominio/modulos.js:75`) |
| 18·5 | Alta | Nomes inteiros no celular e truncados no desktop | C | P8 | É a prova do 05·7: mesma correção (`NomeDoContato.module.css:8`, `features/atendimento/atendimento.module.css:12`) |
| 18·6 | Média | Header do Cardápio em duas faixas; "Avisos" órfão | V | P7 | "Avisos" só existe no shell (`Moldura.jsx:316-318`); o header do Cardápio não foi localizado. Verificar o Cardápio em 390 px; se quebrar, header de uma linha no P7 |
| 18·7 | Média | Card de prato com ~230 px, controles em três linhas | C | P8 | Ações junto ao nome; toggle em linha própria (`features/cardapio/PainelCardapio.jsx:387-400`) |
| 18·8 | Baixa | Chips de filtro em 2 linhas | V | P8 | Nem `Chip.module.css` nem `Pilula.module.css` têm `flex-wrap` ou `overflow-x`; o contêiner não foi localizado. Verificar em 390 px; correção: rolagem horizontal |

### 19 Ajustar tela inicial, 4 itens

| # | Sev. | Achado | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|---|
| 19·1 | Média | "Salvos neste navegador para sua conta e empresa": escopo contraditório | C | P8 | Dizer onde vale, conferido com a chave em `preferenciasHall` (`hall/PersonalizarHall.jsx:16`, `HallDeModulos.jsx:89,144`) |
| 19·2 | Média | Lista 4 indicadores; a Home mostra outros nomes | C | P8 | Mesmos rótulos nos dois lados (`PersonalizarHall.jsx:14,26-28`, `dominio/preferenciasHall.js:6`, `hall/PulsoDoHall.jsx:15-21`) |
| 19·3 | Baixa | "Organizar áreas e favoritos" junto de Cancelar/Aplicar | C | P8 | Link no corpo do modal (`PersonalizarHall.jsx:17`) |
| 19·4 | Baixa | Prévia textual em caixa pêssego | C | P8 | Miniatura do layout (`PersonalizarHall.jsx:23`, `hall.module.css:205`) |

### Observações do relatório sem número (fora dos 121)

| Onde | Observação | Estado | Pacote | Correção/motivo |
|---|---|---|---|---|
| 01 | Campo de busca bege sobre bege (borda 1,07:1) e cartões brancos sem borda nem sombra | C | P5 | Borda `--linha` com 3:1 no campo; borda ou sombra no cartão (`componentes/Campo.module.css:8-10`, `tokens.css:36,41`, `hall.module.css:61`) |
| 04 | Menu da conta só com nome, tema e sair | C | P7 | Papel, unidade, perfil e versão (`componentes/MenuDaConta.jsx:78-81`, `HallDeModulos.jsx:185`). Se o payload da conta não trouxer esses campos, ficam para a spec da API (inferido) |
| 05 | Borda esquerda vermelha x oliva, badges pretos x vermelhos sem legenda, datas mistas, lista com 7 e Home com 8 | V | P8 | Verificar em 1440 px a lista de conversas contra a Home: mesma contagem de "precisa de você" e uma legenda para cor e ícone |

## Sistema visual: causas de raiz

Medido no código contra o que o relatório afirma.

| Dimensão | Relatório | Medido | Pacote |
|---|---|---|---|
| Cor semântica | `--marca` = `--acao` = `#a25803`; `--alerta` = `#7a5a00`, 12° de matiz | Confere: `tokens.css:51,55,67` | P5 |
| Tokens | 115 variáveis, 55 cores, aliases redundantes | 113 nomes e 68 hex únicos em `tokens.css`; 216 literais de cor em 62 CSS fora dele. Os aliases conferem: `--fundo` = `--creme` = `--chao-conversa` (`:36,39,40`), `--linha-media` = `--linha-forte` (`:46,47`) | P5 |
| Elevação | Card/página 1,08:1 no claro e 1,03:1 no escuro | Confere: 1,084 e 1,026. `.cartao` sem sombra (`hall.module.css:55`) | P5 |
| Tipografia | 8 tamanhos; H1 = H2 = H4 = 20 px | A escala tem 7 valores; no uso há 12 distintos. H1 = H2 = H4 só em Canais; no Console, H1 17 px, H2 20 px, H4 14-15 px | P5 |
| Componentes | 7 estilos de botão, 2 de input, switch e checkbox | 4 visuais em `Botao.module.css:17-32`, mais `entrada` e `ficha` usados sem classe; 2 inputs confere; checkbox em 34 arquivos e `role="switch"` em 2 | P6 |
| Elementos nativos | `<select>`, `<details>`, X da busca, date picker | `<select>` em 10 arquivos, `<details>` em 23, `type="search"` em 6 (X escondido só em `caixa.module.css:165`), `type="date"` em 1 | P6 |
| Navegação | 3 versões do menu, Home sem menu | Dois shells em `app/App.jsx:274` e `:431,:503`, mais o trilho do Atendimento | P7 |
| Lint de cor | Pede lint que proíba cor fora do token | Só `oxlint` roda; nada impede cor fora do token | P9 |

As 12 recomendações de "Para virar uma ferramenta premium" (⌘K, Home cockpit, central de avisos em
tempo real, gráficos, kanban com SLA, densidade, regressão visual, lint de tokens e as demais) ficam
no P9, exceto o que os itens C acima já cobrem: shell único, tema escuro, voz única e estados de
primeira classe.

## P1, desenho do 17·4

Há dois caminhos de envio e o problema está nos dois:

| Caminho | Hoje | Mudança |
|---|---|---|
| Regra salva (`ModeloTextoAtendimento`) | A lista de variáveis é nome, pedido e faixa (`EasyStock.Domain/Entities/Atendimento/ModeloTextoAtendimento.cs:11`); o regex `[a-z]+` não casa camelCase (`:28`) e a chave fora da lista sai crua (`:21`) | O servidor resolve `{linkCardapio}` e `{abre}` na hora do envio (`EasyStock.Application/UseCases/Atendimento/Automacoes/DispararAutomacaoUseCase.cs` por volta de `:232-241`; `EasyStock.Domain/Entities/ExpedienteLoja.cs:35` já tem `MarcadorAbre`) |
| Saudação de reserva (`SaudacaoAtendimento`) | Só troca `{nome}` e `{link}` (`EasyStock.Application/Services/Atendimento/SaudacaoAtendimento.cs:30-60`) | Mesma resolução. Trocar a chave por `{link}` no texto padrão (`dominio/automacao.js:39`) não basta, porque a regra salva continua com a chave antiga |

Em cima disso: ao salvar, o validador rejeita chave fora da lista; a chave que ficar sem valor na hora
do envio não sai, reaproveitando o motivo de não envio por variável sem valor (nome exato a confirmar
na implementação); e a mensagem de `AbaRespostas.jsx:201-205` deixa de citar "EasyStok". Regras já
salvas com a chave crua só mudam com GO (ver "Ações em produção").

## Pacotes

Um PR por funcionalidade. Tier: com esta spec aprovada, P5 a P8 deixam de ser "feat/refactor sem
spec" e passam a tier baixo. Segue alto, por natureza, o que tocar migração, processo ou `*.csproj`.

| Pacote | Itens (ids) | Depende de | Tier | Tam. |
|---|---|---|---|---|
| P1 Mensagem ao cliente | 17·4, 17·5 | nada | baixo; alto se mexer no default da migração (a decisão de grafia dispensa) | M |
| P2 Caixa | 14·1 a 14·4 (e as telas do #2275) | nada | baixo | P |
| P3 Cardápio × Estoque × formulário | 12·0, 11·3, 11·4, 10·5, 01·7 (12·3 já atende) | decisão de base (aviso, sem derivar) | baixo | M |
| P4 Entregas e dados de teste | 08·5, 09·4, 06·1 | nada; dado exige GO | baixo (código) | P |
| P5 Tokens | 01·6, 04·2, 04·3, 04·4, 16·2, observação do 01 | esta spec aprovada | baixo com a spec | M |
| P6 Componentes | 02·1, 06·5, 06·8, 08·3, 08·4, 08·6, 09·2, 10·7, 11·1, 11·2, 12·4, 13·3, 16·3, 17·2 | P5 (token antes de componente) | baixo com a spec | G |
| P7 Shell | 01·1, 01·9-10, 05·0, 05·2, 07·1, 07·2, 08·1, 08·2, 10·1, 10·2, 17·1, 18·3, 18·6, observação do 04 | P6 (componente antes do shell) | baixo com a spec; dividir por tela se o diff crescer | G |
| P8 Microcopy e polimento | os 58 itens restantes (coluna Pacote das tabelas) | P5 e P6 | baixo | G (dois lotes de telas) |
| P9 Premium | ⌘K, Home cockpit, central e tempo real de avisos, gráficos, kanban com SLA, agrupar variações, densidade, lint de tokens, classificador de tags | spec própria | alto | fora |

Ordem: P1, P2 e P4 em paralelo e sem dependência; depois P3; depois P5, P6 e P7, com P8 junto.
Sobreposição com a #2261: o "layout-mestre único" é o P7 (05·0, 07·1, 17·1); o `motivo` nos botões
desabilitados é o P6 (06·5, 09·3, 13·3); o "(referência X)" nos avisos é o 03·6 do P8. Cada item
sobreposto é feito uma vez, no pacote que chegar primeiro; o outro só confere.

## Prova esperada nas críticas

Cada item C ganha `ferramentas/prova-2274-*.mjs` que falha antes e passa depois, mais print claro e
escuro em 390 e 1440 px para os visuais.

| Crítica | Prova |
|---|---|
| 17·4 Variável crua | Nenhuma mensagem enviada contém `{\w+}`; o validador bloqueia, ao salvar, chave desconhecida. Consulta somente leitura em produção: zero regras com chave crua depois do UPDATE autorizado |
| 14·4 Caixa | O valor do aviso é a soma das linhas citadas, no mesmo período dos totais; teste com pedido cancelado de ontem e de hoje |
| 07·1 Menu | Mesmos destinos e mesma ordem em todos os módulos; uma prova compara o trilho entre eles |
| 12·0 Cardápio × Estoque | Nenhuma tela diz "disponível" sem o aviso de saldo quando o estoque é 0; teste de contrato entre Cardápio e Estoque |
| 04·1 Logo | Sem mudança de código: print da placa clara nos dois temas, em 390 e 1440 px, com o contraste da placa contra o fundo escuro registrado |

## Ações em produção que dependem do GO do Felipe

Nada abaixo é executado por esta spec.

| Ação | Item | Cuidado |
|---|---|---|
| UPDATE do texto das regras e saudações salvas com `{linkCardapio}` | 17·4 | Medir por consulta de leitura quais regras e empresas, antes |
| Arquivar o chamado "teste" | 09·4 | Medir o alvo exato; arquivar em vez de apagar |
| Fechar as viagens "Montando" vazias | 08·5 | Medir quantas e de quais empresas |
| Migrar `CategoriaTexto` para `SecaoId` | 11·3 | Só dos itens sem seção |
| Renomear leads "Cliente WhatsApp" | 06·1 | Só os sem nome conhecido |
| UPDATE de avisos já gravados com "(referência X)" | 03·6 | Só se a leitura confirmar que existem (inferido) |
| Qualquer `dotnet ef database update` | P1, P3 | Sempre GO |
| Deploy do P1 | 17·4 | Muda o texto enviado a clientes reais; Felipe aprova a prévia |

## Aceite

- [ ] Cada item C com prova que falha antes e passa depois (`ferramentas/prova-2274-*.mjs`).
- [ ] Cada item V verificado no produto em 390 e 1440 px e reclassificado em C ou J, com a evidência.
- [ ] `npm run qualidade` e o gate verdes.
- [ ] Revisão independente sem achado.
- [ ] Produção validada em 390 e 1440 px, claro e escuro, depois de cada deploy.

## Rollback

Reverter o squash do PR de cada pacote. O P1 muda o servidor; as demais mudanças de código são só do
Console. O banco só muda nas ações da seção "Ações em produção", cada uma com GO e com a consulta de
medição guardada para desfazer.
