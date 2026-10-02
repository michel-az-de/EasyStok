# M7 Configurações: usuários, permissões, integrações, impressão e parâmetros gerais

Issue: #1316 · Decisão: [ADR-0056](../../adr/0056-erp-da-casa-da-baba-front-unico.md) · Data: 2026-10-01
Base medida: master `b713263a` (worktree `erp-casa-da-baba-1316`). Índice: [README](README.md).
Modelagem de usuários, perfis e permissão por módulo: **fatia M0.3** em [01-fundacao](01-fundacao.md). Este
doc especifica só as **telas** que consomem a M0.3. Fatias do plano irmão referenciadas e não reescritas:
S08, S40, S41, S42, S45, S49 a S52 (impressos), F02, F10, F16.

Legenda: ✅ vivo · 🟡 parcial ou em PR · ⬜ ausente. **Fato** cita `path:linha` medido nesta data;
**(inferência)** marca o que não foi reproduzido.

## 1. Veredito

**O BACKEND DE CONFIGURAÇÃO EXISTE QUASE TODO; O QUE FALTA É JUNTAR AS TELAS NUM LUGAR SÓ.** Hoje a
configuração está espalhada em 3 lugares: a gaveta Gestão do console (atendimento, expediente, integrações
em PR), o `EasyStock.Web` (usuários, lojas, alertas de estoque) e lugar nenhum (vitrine do site, fila de
impressão, perfis). O M7 vira o menu único da gestão; o que o ADR-0056 item 7 deixou no Web (estoque) é só
apontado, não migrado.

| Sub-item do rascunho | Backend | Tela hoje | Gap principal |
|---|---|---|---|
| Usuários | ✅ `api/usuarios` | Web ✅ · console ⬜ | console tem atendente fixa; Web oferece só 3 níveis |
| Permissões | 🟡 só por nível | ⬜ | perfil × módulo é da M0.3; não há rota de perfis |
| Integrações | 🟡 F16 em PR (#1304) | console 🟡 (na PR) | só falta o lugar no menu |
| Impressão | ✅ fila, modelos S49/S52 | ⬜ | fila sem consumidor, sem tela de papel nem de falhas |
| Parâmetros gerais | ✅ | console 🟡, Web 🟡, vitrine ⬜ | vitrine do site sem tela; resíduo fiscal |

## 2. Fatos medidos

| # | Fato | Onde |
|---|---|---|
| 1 | Usuários: `GET/POST api/usuarios`, `PUT/DELETE {id}` (Admin), `PUT {id}/senha` (logado), `PUT {id}/perfis` (Admin) | `Api/Controllers/UsuarioController.cs:16,29-30,47-48,63-64,77-78,92-93,107-108` |
| 2 | Não existe rota de perfis nem de permissões (`git grep 'Route("api/perfis'` e `api/permiss`: 0) | busca no repositório |
| 3 | `Perfil (EmpresaId?, Nome, Descricao, Nivel)`, `PerfilPermissao (Permissao)`, `UsuarioPerfil (EmpresaId, PerfilId, LojaId?)` | `Domain/Entities/Perfil.cs:5-14`, `PerfilPermissao.cs:7`, `UsuarioPerfil.cs:5-11` |
| 4 | Enum `Permissao` com 29 valores, ao menos 13 de helpdesk e faturas, módulos já podados (P02, P03); único de atendimento é `AtenderConversas` | `Domain/Enums/Permissao.cs:5-33` |
| 5 | Níveis: `SuperAdmin, Admin, Gerente, Operador, Visualizador` | `Domain/Enums/NivelAcesso.cs` |
| 6 | Web lista perfis fixos `Admin/Gerente/Operador` como **string**, e a ação recebe `Guid perfilId` | `Web/Models/ViewModels/Usuarios/UsuariosViewModel.cs:12-17`; `Web/Views/Usuarios/Index.cshtml:86-96`; `Web/Controllers/UsuariosController.cs:95-97` |
| 7 | Web cria usuário com senha definida por quem convida | `Web/Controllers/UsuariosController.cs:43-58` |
| 8 | Atendentes (S41): `GET api/atendimento/atendentes` (Operador) | `Api/Controllers/AtendimentoAtendentesController.cs:11-19` |
| 9 | Login do console mostra o título "EasyStok" | `Console/src/features/login/TelaLogin.jsx:45-46` |
| 10 | F16 (#1304, aberta): `credencial_integracao` com RLS, testadores de MP, WhatsApp, Maps e Lalamove, vigia, `AbaIntegracoesApi.jsx`, faixa de integração parada | worktree `integracoes-chaves-1246`, `git diff --stat origin/master...HEAD` (72 arquivos) |
| 11 | Fila de impressão: `GET api/impressao/pendentes`, `POST {id}/impressa`, `POST {id}/falhou` (policy da fila), `POST api/pedidos/{id}/reimprimir` (Operador) | `Api/Controllers/ImpressaoController.cs:59-84` |
| 12 | Impressos S49/S52: `GET api/pedidos/{id}/impresso` e `/comanda`; 6 modelos `.sbn` (10×15, 58 mm, A4) | `Api/Controllers/PedidoImpressoController.cs:23-41`; `Api/Data/Templates/Impressao/` |
| 13 | Chave do agente de impressão vem de env (`Impressao__ApiKey`); sem chave só o console (JWT) consome | `Api/Authorization/ImpressaoApiKeyAuthHandler.cs:13-20` |
| 14 | **Ninguém consome a fila**: `impressao/pendentes` aparece 0 vez no console e na PWA | `git grep` em `Console/src` e `Api/wwwroot/pwa` |
| 15 | "Pronto até" é constante de 30 min no código (S50 sem PR) | `Application/UseCases/Operacao/Impressao/PrazoImpresso.cs:13` |
| 16 | `ConfiguracaoAtendimento`: tom, nível de sugestão, saudações, frase de espera, mensagem fora da área, respiro (40), preparo padrão (60); rota Admin | `Domain/Entities/Atendimento/ConfiguracaoAtendimento.cs:16-42`; `AtendimentoConfiguracaoController.cs:9-10` |
| 17 | Expediente (S40): `GET/PUT api/atendimento/expediente`, `POST controle` (Admin) | `AtendimentoExpedienteController.cs:16-17,27,35,55` |
| 18 | Respostas prontas e automáticas (S42): rotas Operador | `AtendimentoRespostasProntasController.cs:14-15`; `AtendimentoAutomacoesController.cs:15-16` |
| 19 | `ConfiguracaoLoja` (por loja): alertas de validade e parado, mínimo e crítico, cobertura, lead time, 4 flags de notificação, FIFO, KDS, moeda, fuso, conta a receber automática | `Domain/Entities/ConfiguracaoLoja.cs:9-33`; rota `api/configuracoes` (Gerente) `Api/Controllers/ConfiguracoesController.cs:8-9` |
| 20 | Web `/configuracoes` edita só 6 desses campos (validade, parado, mínimo, crítico, FIFO, KDS) | `Web/Views/Configuracoes/_ConfiguracoesGeralTab.cshtml:31,40,49,58,128,141` |
| 21 | Vitrine do site: `api/minha-vitrine/configuracao` (Admin) com subtítulo, logo, cor, WhatsApp, mensagem fora da área, pedido mínimo, frete grátis acima, domínio, **ModeloFiscal**, **HabilitarNfeAutomatica**, loja padrão; **nenhum front chama** | `Api/Controllers/Storefront/StorefrontConfiguracaoController.cs:22-23,35-45`; `git grep minha-vitrine/configuracao`: 0 |
| 22 | `MensagemForaArea` existe na vitrine **e** na configuração do atendimento | `Domain/Entities/Storefront/Storefront.cs:72`; `ConfiguracaoAtendimento.cs:31` |
| 23 | Templates e rotinas de notificação: `api/notificacoes/templates`, `rotinas` (Admin); nenhum front chama | `Api/Controllers/NotificacoesConfiguracaoController.cs:18-19,65-161` |
| 24 | Lojas: `api/lojas` (Gerente); tela só no Web `/lojas` | `Api/Controllers/LojaController.cs:12-89`; `Web/Controllers/LojasController.cs:8-99` |
| 25 | Gestão do console no modo API: abas Atendimento, Produção, Caixa, Janelas, Fidelidade, Integrações; as sem backend mostram faixa "não ligado" | `Console/src/features/gestao/ModalGestao.jsx:20-40` |

## 3. Sub-tela → o que cobre → estado

| Sub-tela do M7 | Backend / S / F que cobre | Tela hoje | Estado | Fatia nova |
|---|---|---|---|---|
| Menu do M7 (substitui a gaveta Gestão) | M0.2 (shell) | gaveta `ModalGestao` | 🟡 | **M7.1** |
| Usuários: lista, criar, editar, desativar, senha | `api/usuarios` (fato 1) | Web ✅ | console ⬜ | **M7.2** |
| Usuários: atende conversas (S41) | `AtenderConversas`, `GET atendentes` | ⬜ | 🟡 | **M7.2** |
| Perfis e permissão por módulo | **M0.3** (rotas, enum limpo, regra na API) | ⬜ | ⬜ | **M7.3** (só tela) |
| Integrações: chaves, testar, vigia | F16 (#1304) | console na PR | 🟡 | não: F16; lugar no menu em M7.1 |
| Lalamove e entregador próprio | F16 + F17 | console na PR | 🟡 | não: card mora no M8 |
| Impressão: papel por documento | S51 (papel salvo por dispositivo) | ⬜ | ⬜ | **M7.4** |
| Impressão: fila, falhas, reimprimir, agente | S20 (fato 11), chave por env (fato 13) | só "reimprimir" na Cozinha | 🟡 | **M7.4** |
| Impressão: "pronto até" por loja | S50 (sem PR) | ⬜ | ⬜ | não: S50; campo entra na tela M7.4 |
| Atendimento: tom, saudações, respiro, preparo | S08 + F02 | console ✅ (`AbaAtendimento.jsx:174-215`) | ✅ | não: muda de lugar em M7.1 |
| Expediente: horário por dia, abrir e fechar, mensagens | S40 + F02 | console ✅ (`AbaAtendimento.jsx:53,96`; `aplicacao/api/expediente.js:32,61`) | ✅ | não: muda de lugar em M7.1 |
| Respostas prontas | S42 + F10 (#1240) | console só local | 🟡 | não: F10 |
| Mensagens automáticas por gatilho | S42 + F10 | console só local (`ModalAutomacoes.jsx`) | 🟡 | não: F10 |
| Janelas, zonas de frete, bloqueios, respiro | S45 parte 1 + F04 | console ✅ (Entregas › Janelas e frete) | ✅ | não: é do M8 |
| Canais ligados por empresa | S45 parte 2 (`CanalEmpresa`, sem PR) | ⬜ | ⬜ | **M7.6** |
| Vitrine do site (logo, pedido mínimo, frete grátis) | `minha-vitrine/configuracao` (fato 21) | nenhuma | ⬜ | **M7.5** |
| Parâmetros de estoque (`ConfiguracaoLoja`) | `api/configuracoes` | Web 🟡 (6 de 17 campos) | 🟡 | não: fica no Web (DM7-1) |
| Lojas | `api/lojas` | Web ✅ | ✅ | não: fica no Web |
| Templates de aviso ao cliente | `api/notificacoes/templates` | nenhuma | ⬜ | não: DM7-4 |

### 3.1 Parâmetros gerais: onde cada um tem tela hoje

| Parâmetro | Entidade | Console (`features/gestao`, `automacoes`, `respostas`) | `EasyStock.Web` | Destino |
|---|---|---|---|---|
| Tom, nível de sugestão, saudações, frase de espera | `ConfiguracaoAtendimento` | ✅ grava na API (F02) | ⬜ | M7 Atendimento |
| Mensagem fora da área | `ConfiguracaoAtendimento` **e** `Storefront` (fato 22) | ✅ só a do atendimento | ⬜ | unificar (M7.5) |
| Respiro entre janelas, preparo padrão | `ConfiguracaoAtendimento` | ✅ | ⬜ | M7 Atendimento |
| Horário por dia, controle manual, fora do horário, loja fechada | `ExpedienteLoja` | ✅ | ⬜ | M7 Atendimento |
| Respostas prontas | `RespostaPronta` | 🟡 local, não grava (F10) | ⬜ | M7 Atendimento |
| Automáticas (6 gatilhos) | `RegraAutomatica` | 🟡 local, não grava (F10) | ⬜ | M7 Atendimento |
| Chaves de integração | `credencial_integracao` | 🟡 na PR #1304 | ⬜ | M7 Integrações |
| "Pronto até" antes da janela | constante (fato 15) | ⬜ | ⬜ | M7 Impressão (após S50) |
| Papel padrão por documento | não existe | ⬜ | ⬜ | M7 Impressão |
| Alertas de validade e parado, mínimo, crítico, FIFO, KDS | `ConfiguracaoLoja` | ⬜ | ✅ | Web (bastidor) |
| Cobertura alvo, lead time, 4 notificações, conta a receber automática, moeda, fuso | `ConfiguracaoLoja` | ⬜ | ⬜ | Web (DM7-1) |
| Logo, cor, subtítulo, WhatsApp de pedidos, pedido mínimo, frete grátis acima, domínio, loja padrão | `Storefront` | ⬜ | ⬜ | M7.5 |
| Modelo fiscal, NF-e automática | `Storefront` | ⬜ | ⬜ | poda M0.4 (RN-28, P04) |

## 4. Fatias novas

Formato do plano irmão. Tier pelo ADR-0055. Todas dependem de M0.1 (tema) e M0.2 (shell e sala).

### M7.1 · Menu do M7 e mudança da gaveta Gestão

**Problema.** Configuração mora numa gaveta modal com abas de assuntos diferentes (fato 25): caixa e
produção, que viram M5 e M2, ao lado de atendimento e integrações.
**Abordagem.** Só console. O M7 ganha menu lateral: **Atendimento** (configuração + expediente + respostas +
automáticas), **Integrações**, **Impressão**, **Usuários**, **Perfis**, **Canais**, **Vitrine do site** e
**Bastidor** (atalhos para `EasyStock.Web` `/configuracoes` e `/lojas`, abrindo em nova aba). Componentes das
abas atuais são reaproveitados como estão; Caixa vai para o M5, Produção para o M2, Janelas para o M8,
Fidelidade para o M6.
**Escopo.** Rotas `#/configuracoes/<secao>`; itens sem fatia pronta ficam com a faixa "ainda não ligado"
existente; a gaveta Gestão sai quando todas as abas tiverem casa.
**Aceite.**
- [ ] Tudo que a gaveta grava hoje (atendimento, expediente) continua gravando pelo menu novo.
- [ ] Nenhuma aba some sem destino: tabela da seção 3 conferida na PR.
- [ ] `npm run qualidade` verde.
**Rollback.** Revert do squash; a gaveta volta.
**Depende de.** M0.2. **Tamanho.** M. **Tier.** baixo.

### M7.2 · Usuários no console

**Problema.** O console tem a atendente fixa e o Web oferece só 3 níveis em string para uma ação que espera
`Guid` (fato 6): **(inferência)** atribuir perfil pelo Web pode estar quebrado (a ligação `"Admin"` → `Guid`
cai em `Guid.Empty`); medir na primeira hora da fatia com a API local.
**Abordagem.** Tela sobre `api/usuarios` (fato 1). Perfil escolhido da lista que a M0.3 expõe. Marcação
"atende conversas" lê a permissão `AtenderConversas` do perfil (S41), sem campo novo.
**Escopo.** Lista (nome, e-mail, perfil, ativo, último acesso se a API devolver, convite pendente ou aceito),
**convidar por link** (DM7-3 decidida em 2026-10-01: convite, sem senha inicial; a dona nunca define a senha de ninguém;
backend na N9 de [06-acesso](../notificacoes-sistema/06-acesso.md)), reenviar convite, editar nome, trocar perfil,
desativar e reativar, trocar a própria senha.
**Fora.** 2FA; SSO.
**Aceite.**
- [ ] Usuário convidado define a senha pelo link, entra no login e cai na porta do perfil (ADR-0056 item 4).
- [ ] Desativar impede login na próxima renovação de token.
- [ ] Operador vê a tela bloqueada e a API devolve 403 em `POST api/usuarios`.
- [ ] Defeito do fato 6 medido; se confirmado, issue própria ou corrigido junto, com teste.
**Testes (Red).** prova Playwright `prova-m7-2-usuarios.mjs`; se o defeito confirmar,
`UsuariosControllerTests.AtribuirPerfilRecebeGuid` no Web.
**Depende de.** M0.3, M7.1, N9 (convite). **Tamanho.** M. **Tier.** alto (mexe em autorização de usuário).

### M7.3 · Perfis × módulos (tela)

**Problema.** A sala de módulos libera ou bloqueia o card por perfil (ADR-0056 item 5), e a dona precisa ver
e mudar isso sem pedir ao Felipe.
**Abordagem.** Tela de matriz perfil × módulo (M1 a M8) com marcação por célula e, dentro de cada módulo, as
permissões finas que a M0.3 definir (ex.: fechar e estornar caixa, disparar campanha). Lê e grava pelas rotas
da M0.3; **nenhuma regra na tela** (a API nega).
**Escopo.** Lista de perfis, criar e renomear perfil, matriz, aviso ao tirar o próprio acesso ao M7
("você perde acesso a esta tela"), contagem de usuários por perfil.
**Fora.** Permissão por loja (`UsuarioPerfil.LojaId`) até a M0.3 decidir.
**Aceite.**
- [ ] Tirar M6 do perfil Atendimento esconde o card na sala e a API passa a devolver 403 em `api/campanhas`.
- [ ] O último perfil com acesso ao M7 não pode perder esse acesso (409 da API).
**Testes (Red).** prova Playwright; o teste de regra é da M0.3.
**Depende de.** M0.3 (D-01), M7.1. **Tamanho.** M. **Tier.** alto (permissão).

### M7.4 · Impressão: papel, fila e falhas

**Problema.** A fila existe e ninguém a consome (fato 14); impressão que falhou some sem aviso; o papel de
cada documento é escolhido a cada vez; o "pronto até" é constante (fato 15).
**Abordagem.** Tela de impressão do console com três blocos. **Papéis:** por documento (Pedido, Comanda,
Canhoto), o papel padrão (10×15, 58 mm, A4), salvo por dispositivo como manda a S51 (DM7-2). **Fila:**
pendentes e falhas das últimas 24 h, com **Reimprimir** (`POST api/pedidos/{id}/reimprimir`, fato 11) e o
erro do consumidor. **Prazos:** campo "pronto até, minutos antes da janela" depois que a S50 entrar.
**Escopo.**
- Backend: `GET api/impressao?status=&desde=` (leitura, Operador) com `EmpresaId` no `WHERE`; a rota
  `pendentes` continua só para o consumidor.
- Lembrete da dona (S43) quando um item fica `Falhou` (sem som novo; reaproveita o sininho).
- Bloco informativo do agente: se há chave configurada (sim ou não, nunca o valor, fato 13).
**Fora.** Consumidor 58 mm em imagem (S51); impressora por IP; modelos novos (backlog do doc 12).
**Aceite.**
- [ ] Item marcado `falhou` aparece na tela com o erro e some ao reimprimir com sucesso.
- [ ] Trocar o papel padrão do Pedido neste tablet não muda o do outro dispositivo.
- [ ] Item `Falhou` gera 1 lembrete, idempotente.
**Testes (Red).** `ListarImpressoesQueryTests.SoDaEmpresa`, `AvaliadorLembretesTests.ImpressaoFalhouIdempotente`.
**Depende de.** M7.1; S51 para o papel valer na impressão real; S50 para o bloco Prazos.
**Tamanho.** M. **Tier.** baixo (leitura e lembrete, sem migration).

### M7.5 · Vitrine do site

**Problema.** O site tem 11 parâmetros sem tela em lugar nenhum (fato 21); dois são fiscais, contra a RN-28 e
a poda P04; a mensagem de fora da área existe duas vezes (fato 22) e pode divergir entre o agente e o site.
**Abordagem.** Tela sobre `api/minha-vitrine/configuracao` com os campos operacionais. Os fiscais não aparecem
e entram na poda M0.4 (DM7-5). `MensagemForaArea` fica com uma fonte só: a do atendimento (S08), e o site
passa a ler dela **(inferência: medir quem lê o campo da vitrine hoje, incluindo o repositório do site)**.
**Escopo.** Formulário (logo pelo upload existente, cor, subtítulo, WhatsApp de pedidos, pedido mínimo, frete
grátis acima, domínio, loja padrão), botão **Ver o site** e ativar ou desativar a vitrine com confirmação.
**Aceite.**
- [ ] Mudar o pedido mínimo reflete no checkout do site no próximo pedido.
- [ ] A tela nunca mostra modelo fiscal nem NF-e.
- [ ] Uma só mensagem de fora da área vale para agente e site.
**Testes (Red).** `IniciarCheckoutUseCaseTests.PedidoMinimoDaVitrine` (se não houver);
`MensagemForaAreaTests.FonteUnica`.
**Depende de.** M7.1; M0.4 para remover os campos fiscais. **Tamanho.** M. **Tier.** alto se unificar a
mensagem mudar contrato do site; baixo se for só tela.

### M7.6 · Canais ligados

**Problema.** A S45 parte 2 (`CanalEmpresa (Canal, Ligado)`) espera S37 a S39, que já entraram (#1081, #1079, #1083);
sem ela, desligar um canal exige mexer em flag.
**Abordagem.** Tela de cartões por canal (WhatsApp, Instagram, Messenger, chat do site, e-mail, SMS) com
Ligado/Desligado e o estado da integração vindo do F16 (teste e último erro). O backend é da S45 parte 2;
esta fatia é só a tela.
**Aceite.**
- [ ] Desligar o Instagram para de responder por ele e o cartão mostra "desligado por {usuário} às {hora}".
**Depende de.** S45 parte 2, F16, M7.1. **Tamanho.** P. **Tier.** baixo.

## 5. Ordem dentro do M7

```
M0.2 shell ─► M7.1 menu ─┬─► M7.2 usuários ─► M7.3 perfis (precisa da M0.3)
                         ├─► M7.4 impressão (S50, S51 completam)
F16 (#1304) ─────────────┼─► integrações no menu
F10 (#1240) ─────────────┼─► respostas e automáticas no menu
                         ├─► M7.5 vitrine (M0.4 poda os fiscais)
S45 parte 2 ─────────────┴─► M7.6 canais
```

## 6. Decisões pendentes do Felipe

| # | Pergunta | Opções | Bloqueia |
|---|---|---|---|
| DM7-1 | Parâmetros de estoque (`ConfiguracaoLoja`) | **(a) Ficam no Web, o M7 só aponta para lá (Recomendado, ADR-0056 item 7)** · (b) Migram para o console agora · (c) Migram só os alertas que a dona usa no dia (validade e mínimo) | M7.1 |
| DM7-2 | Onde mora o papel padrão de cada documento | **(a) Por dispositivo, como a S51 já decidiu (Recomendado)** · (b) Por loja, no servidor · (c) Loja define o padrão e o dispositivo sobrescreve | M7.4 |
| ✅ DM7-3 | Como entra um usuário novo | **DECIDIDO em 2026-10-01: (c) convite com link** (por e-mail e pelo WhatsApp de plataforma; a própria pessoa define a senha; spec N9 em [06-acesso](../notificacoes-sistema/06-acesso.md)). Descartadas: (a) senha inicial com troca no primeiro acesso e (b) senha definida pela dona sem troca obrigatória | M7.2 |
| DM7-4 | Templates de aviso ao cliente (`api/notificacoes/templates`) | **(a) Sem tela por ora; os avisos de status (S13) e as automáticas (S42) bastam (Recomendado)** · (b) Tela no M7 para editar os textos dos avisos | M7.1 |
| DM7-5 | Campos fiscais da vitrine (`ModeloFiscal`, `HabilitarNfeAutomatica`) | **(a) Remover na poda M0.4 (Recomendado)** · (b) Só esconder na tela e manter as colunas | M7.5, M0.4 |
| DM7-6 | Fonte da mensagem "fora da área de entrega" | **(a) A do atendimento (S08), o site passa a ler dela (Recomendado)** · (b) A da vitrine · (c) Manter as duas, uma para o agente e outra para o site | M7.5 |

## 7. Contradições e divergências achadas

| # | Divergência | Evidência | Encaminhamento |
|---|---|---|---|
| 1 | Web oferece perfil como string e a ação espera `Guid` | fato 6 | medir em M7.2 |
| 2 | Vitrine ainda carrega modelo fiscal e NF-e automática depois da poda fiscal P04 (#1114) | fato 21 | M0.4 (DM7-5) |
| 3 | `MensagemForaArea` em duas entidades | fato 22 | M7.5 (DM7-6) |
| 4 | Estudo marca Impressão como ✅; a fila não tem consumidor nem tela de falha | `00-estudo.md` §2; fato 14 | M7.4 |
| 5 | Estudo marca Usuários como ✅ "Web"; o Web só oferece 3 níveis e o console nenhum | `00-estudo.md` §2; fatos 6 e 9 | M7.2 |
| 6 | Policies diferentes para parâmetros da mesma tela: configuração e expediente **Admin**, respostas e automáticas **Operador**, `api/configuracoes` **Gerente** | fatos 16 a 19 | M0.3 alinha por módulo |
| 7 | Login do console diz "EasyStok"; a marca pedida é Casa da Baba | fato 9; README D-02 | M0.1 |
