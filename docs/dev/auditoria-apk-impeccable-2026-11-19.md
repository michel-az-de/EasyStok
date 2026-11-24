# Auditoria Impeccable do APK Casa da Baba Piloto 0.8.15 (2026-11-19)

Alvo: APK publicado hoje (`latest.json`: 0.8.15, versionCode 22, buildSha `f0717641d`, sha256 conferido). Master não tem commit no app ou na PWA depois de `f0717641d`, então o código lido é o APK em uso.

Método: emulador Android 35 (360x708 CSS px, DPR 2) com a WebView inspecionada por CDP, axe-core 4.10.2 em 9 telas, toques reais via adb, tema escuro, fonte 1.3, perfil de CPU, e leitura do código nativo (`casa-da-baba-mobile/apk/android`) e da PWA (`EasyStock.Api/wwwroot/pwa/index.html`).

Limites: emulador com GPU por software (tempos absolutos de partida não valem para aparelho real); sem login, então sem dados do servidor; TalkBack não foi executado, só a árvore de acessibilidade.

## Placar

| # | Dimensão | Nota | Achado principal |
|---|---|---|---|
| 1 | Acessibilidade | 2 | "+" e "-" sem nome do produto e foco jogado para o BODY a cada toque; toast mudo |
| 2 | Desempenho | 2 | Foto sem redução antes do base64; 3 passes completos a cada gravação; script inline de 683 KB sem cache de código |
| 3 | Aparência e tema | 2 | Splash branco no escuro, logo raster em retângulo branco, 676 hex fixos |
| 4 | Conformidade de plataforma | 3 | Prompt nativo de quantidade com teclado de letras; permissões pedidas antes de valor |
| 5 | Adaptatividade | 2 | Fonte 1.3 corta rótulos ("Atendimentc"); barras do sistema ficam velhas ao trocar tema |
| **Total** | | **11/20** | **Aceitável (trabalho significativo)** |

## Veredito de plataforma

Passa como app, não como site embrulhado: Voltar nativo correto, diálogos nativos, alvos de 48dp, sem overflow horizontal. Falha nos detalhes que um usuário Android percebe: teclado errado no prompt de quantidade, permissões na primeira abertura, splash branco no escuro.

## Achados

Contagem: P0 0, P1 7, P2 14, P3 6.

### P1

| # | Achado | Onde | Evidência | Comando |
|---|---|---|---|---|
| 1 | Botão Foto sobrepõe o "+" no card selecionado: tocar no alto do "+" abre a câmera | Produção, card selecionado | Foto em (287-335, 453-501), Aumentar em (286-334, 486-534); `elementFromPoint(310,495)` = Foto | `/impeccable layout` |
| 2 | Prompt de quantidade abre teclado de letras, sem foco, campo colado na borda, cor teal padrão | `PilotChromeClient.java:64-85` | Toque real no emulador | `/impeccable harden` |
| 3 | "+" e "-" perdem o foco (grade inteira refeita) e o rótulo é só "Aumentar"/"Diminuir", sem produto; quantidade não é anunciada | `index.html` ~12602, 12649, 12651, 21932-21945 | `activeElement` = BODY após toque | `/impeccable harden` |
| 4 | Toast sem `role=status`/`aria-live`: confirmação some calada para leitor de tela | `index.html` 8226-8228 | Atributos ausentes em runtime | `/impeccable harden` |
| 5 | Foto não é reduzida antes de virar base64 no localStorage: risco de memória e cota | `onPhotoPicked` ~12720-12740 | Código | `/impeccable optimize` |
| 6 | Cada gravação faz stringify + snapshot + diff completos, sem poda | `persistAll` 9397-9425 | Código | `/impeccable optimize` |
| 7 | Partida a frio pesada: script inline de 683 KB (sem cache V8), `/api/mobile/version` 2x, Fraunces.ttf esperou 9,3 s com a thread bloqueada | `index.html` ~8930 | Emulador: DCL 15,5 s frio vs 1,55 s quente | `/impeccable optimize` |

### P2

| # | Achado | Onde | Comando |
|---|---|---|---|
| 1 | Notificação pedida na primeira abertura e Bluetooth logo após a boas-vindas | `AppNotifications.java:84`, `offerInitialBluetoothPermission` ~24440 | `/impeccable onboard` |
| 2 | Abas e filtros (`.screen-tab`, `.prod-filter`, `.emoji-pick`) sem `aria-selected`/`aria-pressed` | PWA | `/impeccable harden` |
| 3 | Splash branco no escuro (`values-night/styles.xml` não sobrescreve o tema de abertura) | nativo | `/impeccable colorize` |
| 4 | Logo raster dentro de retângulo branco no cabeçalho escuro | cabeçalho | `/impeccable colorize` |
| 5 | Barras do sistema não acompanham troca de tema com o app aberto | `MainActivity`, configChanges uiMode | `/impeccable adapt` |
| 6 | Fonte 1.3 corta "Atendimento", "Impressoras", "Movimentos", "Extras" | Config, filtros | `/impeccable adapt` |
| 7 | Nome do produto sem autocapitalize, vai minúsculo para a etiqueta | Cadastro | `/impeccable harden` |
| 8 | Lote exibido como GUID de 32 hex na tela e na etiqueta | Etiquetas | `/impeccable clarify` |
| 9 | `#etqBody` rolável sem foco (axe `scrollable-region-focusable`, serious) | Etiquetas | `/impeccable harden` |
| 10 | Custo e estoque mínimo só com placeholder como rótulo; 7 rótulos de Ajustes soltos; Pix com placeholder de telefone | Cadastro, Ajustes | `/impeccable clarify` |
| 11 | `itemPickerModal` e `cestaCalcModal` sem `role=dialog` nem trava de foco | PWA | `/impeccable harden` |
| 12 | Novo pedido e demais telas sem títulos h1-h3: navegação por títulos no TalkBack não funciona | Novo pedido (axe limpo, 0 títulos) | `/impeccable harden` |
| 13 | Borda de campo 1,3:1; texto secundário sobre fundo suave 4,38 a 4,41:1 | tokens | `/impeccable colorize` |
| 14 | 676 hex e 486 rgba fixos, 163 sobrescritas, 76 `!important`, CSS duplicado | PWA + `pilot.css` | `/impeccable polish` |

### P3

Sem ícone monocromático; sem predictive back; `theme-color` fixo; chips Massas/Molhos/Extras fixos mesmo sem produto; ponto vermelho do card sem explicação; `minifyEnabled false`.

## Padrões sistêmicos

- Re-render por `innerHTML=''` destrói foco e estado em toda interação de quantidade.
- Cor fixa em vez de token: o tema escuro vive de sobrescritas.
- Rótulo por placeholder repetido em Cadastro e Ajustes.

## O que funciona

- Voltar nativo Config → Produção → Início.
- axe sem violação WCAG A/AA em Boas-vindas, Início, Produção, Pedidos, Novo pedido, Clientes, Cadastro e Produção/Pedidos no escuro.
- Alvos de 48dp ou mais, sem overflow horizontal em fonte 1.0.
- Fonte acompanha o sistema (16 para 20,8px em 1.3); reduced-motion respeitado; focus-visible correto.
- Folha de novo produto foca Nome; manifest seguro.

Suspeitas refutadas com toque real: anel de foco no cabeçalho (artefato de keyevent), "Só neste aparelho" cortado, WebView debugável (é o emulador), rótulo da nav 10px (é 13px), câmera 28px (é 48x48), tela de Novo pedido vazia (era a animação de entrada no GPU por software).

## Ações recomendadas

1. [P1] `/impeccable layout`: separar Foto do "+" no card selecionado.
2. [P1] `/impeccable harden`: prompt de quantidade com teclado numérico e foco; "+"/"-" com nome do produto, foco preservado e leitura viva; toast com `role=status`.
3. [P1] `/impeccable optimize`: reduzir foto antes do base64, gravação incremental, script externo cacheável, fonte com `font-display` e uma chamada de versão.
4. [P2] `/impeccable onboard`: pedir notificação e Bluetooth no momento de uso.
5. [P2] `/impeccable adapt`: fonte 1.3 sem corte, barras e splash seguindo o tema.
6. [P2] `/impeccable clarify`: rótulos reais, lote legível, placeholder do Pix.
7. `/impeccable polish` ao final.
