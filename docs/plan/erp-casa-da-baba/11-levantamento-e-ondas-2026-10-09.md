# Casa da Baba · EasyStok: levantamento e desenvolvimento em ondas

Data: 09/10/2026. Solicitante: Felipe. Escopo: nova versão dedicada à Casa da Baba.
Base do levantamento: master 6763af349510f84db1c9c9bb7434be039588f164, igual ao origin/master na consulta. Árvore de trabalho limpa antes desta documentação.

## 1. Conclusão e alcance da verificação

Existe uma base ampla de regras, APIs e fluxos operacionais. A nova versão ainda não está fechada como produto: partes estão conectadas ao console, outras continuam no Web antigo, outras são demonstração, e várias correções estão em PR aberta. O principal trabalho é concluir jornadas completas, com UX consistente e integrações comprovadas.

Este levantamento cobre as áreas do produto, os planos M0–M8, o histórico recente, as ações conectadas e não conectadas do console, as PRs abertas e verificações locais. Não é uma certificação linha a linha de todo o ERP, nem homologação de produção. Não foram criados pedidos reais, realizadas cobranças, chamados entregadores ou acionadas impressoras.

Classificação usada:

- **Código integrado:** encontrado no master; não significa publicação nem aceite operacional.
- **Parcial:** existe backend, tela ou parte da jornada, mas o conjunto não está fechado.
- **Em PR:** trabalho localizado fora do master; precisa de revisão e validação na base atual.
- **Pendente:** não encontrado na superfície examinada ou explicitamente bloqueado pelo código/plano. A indicação informa qual superfície foi examinada.
- **Operação não comprovada:** depende de conta, dispositivo ou uso real que não foi exercitado nesta demanda.

Não atribuí percentual de conclusão: contar endpoints ou commits produziria uma medida enganosa do funcionamento da loja.

## 2. Direção já decidida

Fontes: [plano M0–M8](README.md), [ADR-0056](../../adr/0056-erp-da-casa-da-baba-front-unico.md), [ADR-0059](../../adr/0059-web-sai-pwa-continua-e-merge-de-conflitos.md) e [homologação de 07/10](../atendimento-whatsapp/homologacao-2026-10-07.md).

1. Produto voltado somente à Casa da Baba; a pessoa não escolhe empresa para operar. Manter proteção por EmpresaId e RLS enquanto existir essa estrutura.
2. EasyStock.Console é o novo frontend, com hall, módulos e menus próprios. Nome decidido: Casa da Baba · EasyStok.
3. Toda área do EasyStock.Web deve ganhar equivalente no console. Inclui estoque, compras, fornecedores, financeiro, etiquetas e relatórios. O Web só sai de cada área depois da substituição comprovada.
4. PWA de produção e cadastro continua como backup offline. O console permanece online.
5. Edição concorrente PWA/console deve gerar conflito resolvido pela dona. Até a resolução, vale o servidor; não sobrescrever silenciosamente.
6. Perfis pretendidos: Dona, Atendimento e Cozinha. Permissões por módulo ainda precisam ser concluídas.
7. Preservar claro/escuro e as interações já aprovadas. Aplicar identidade Casa da Baba a partir dos ativos válidos; o plano M0 registra que o DS de impressos ainda tinha status de proposta, portanto essa referência não equivale a aprovação visual de todas as telas.
8. Pix e cartão de pedidos pelo link do Checkout Pro; maquininha e vale com registro manual, conforme decisão registrada no plano de pagamentos.

O pedido atual é de levantamento e plano. As ondas abaixo são proposta de execução, não autorização automática para excluir dados, publicar alterações ou efetuar transações.

## 3. Código disponível versus ambiente publicado

Consultas públicas, somente leitura, realizadas em 09/10:

| Superfície | Resultado observado | Consequência |
|---|---|---|
| origin/master | 6763af34 | Referência deste inventário |
| app.easystok.online | HTTP 200; metadado easystok-console-sha = 888eeab7 | Console servido anterior ao master |
| api.easystok.online/health/version | HTTP 200; apiVersion 1.10.0; buildSha = 701dbf4b | API servida em outro commit, também anterior ao master |
| casadababa.com | HTTP 200 | A informação antiga de HTTP 503 não descreve esta consulta; checkout não foi exercitado |

Código mergeado não é necessariamente o código que a dona está usando. Comparar SHA do console e da API é o primeiro passo antes de classificar uma funcionalidade como quebrada ou ausente.

Há documentação desatualizada: AGENTS e arquivos da .knowledge ainda citam diferentes infraestruturas, MAUI e .NET 9. O projeto [EasyStock.Api.csproj](../../../EasyStock.Api/EasyStock.Api.csproj) hoje aponta net10.0, confirmado pela compilação dos testes. O [script do console](../../../scripts/deploy/console-deploy.sh) aponta app.easystok.online e VPS hostinger. Os documentos antigos ficam como histórico, não como evidência operacional atual. Não foi feita auditoria interna da VPS nesta demanda.

## 4. Inventário por área

| Área | O que já existe no código examinado | Falta para a nova versão | Situação |
|---|---|---|---|
| Fundação e navegação | Hall M1–M8, rotas por módulo, menus, login senha/Google e tema claro/escuro | Matriz perfil × módulo, UX de navegação, consistência entre telas de operação e ajuste, entrada por perfil e sessão da cozinha | Parcial |
| Design system | Tokens centrais, componentes comuns, CSS por área, temas e regras de movimento | Identidade Casa da Baba aplicada e aprovada; tipografia, estados, formulários, tabelas, acessibilidade e responsividade uniformes | Parcial; tokens ainda trazem a paleta do protótipo |
| M1 Cardápio | Lista e gestão via API; disponibilidade do dia/site, ordem no servidor, arquivamento, item novo em validação, novidade com prazo e categorias | Fechar ficha de produto, variações/porções, adicionais, combos, alergênicos e coerência do preço/saldo entre canais | M1.1–M1.3 integradas; restante parcial |
| M2 Produção | Backend de produção/lotes/estoque e receitas; ajustes recentes no sync da PWA; lote de papel e cardápio do dia ligados no balcão | Módulo próprio completo, insumos, fichas técnicas, planejamento, peso real, perdas, embalagens, etiquetas e conflitos offline | No master o módulo tem telas vazias; M2.1 em PR; M2.2 em issue |
| M3 Balcão | Conversas, assumir/devolver ao automático, texto, reenvio, cadastro/ficha, tags, notas, respostas, galeria, áudio WhatsApp, sugestão e ações confirmadas do assistente | Fechar alterações do pedido na ficha, ocorrências, reabertura, mudança de endereço/janela, lembretes, modelos aprovados, nova conversa e recuperação de falhas | Ampla implementação, jornada incompleta |
| Pedido e cobrança manual | Comanda por API, geração de pedido, cobrança, troca de forma, registro/desfazimento manual e máquina de estados | Unificar a interação no balcão, pagamentos parciais/acréscimos conforme regra definida, cancelamento/estorno e consistência com cozinha, caixa e estoque | Parcial; provas locais passaram |
| Mercado Pago | Cliente HTTP Checkout Pro, consulta de pagamento, webhook, assinatura, confirmação, estorno e testes | Configuração efetiva, eliminar divergência de chave global/por loja, sandbox completo, retorno à conversa/site, conciliação e teste operacional | Código integrado; operação real não comprovada |
| M4 Cozinha | KDS no modo API, fila, comanda/canhoto, integração de atualização e fila de impressão | Sessão por perfil, prioridades/prazos, expedição e homologação no dispositivo real | Parcial; prova da cozinha passou |
| Impressão | Modelos de pedido/comanda, fila persistida, retorno de sucesso/falha e reimpressão; PDF/canhoto no console | Consumidor da fila conectado ao equipamento, escolha por dispositivo, recuperação de falha, corte/tamanho/acentos e prova no papel | Geração implementada; impressão física não comprovada |
| M5 Caixa | Abrir loja com caixa, movimentos, fechamento, justificativa ao fechar no horário e pagamento manual | Venda avulsa no console, gaveta versus pagamentos eletrônicos, reconciliação de totais, reabertura, histórico e relatórios completos | Parcial; prova do caixa passou |
| Financeiro gerencial | Casos de uso de contas a pagar/receber, parcelas, baixas, estornos, categorias, centros de custo, dashboard e integração com pedidos/compras | UX no console, regras conciliadas com o caixa e tratamento do legado de gateways | Backend reaproveitável; módulo novo incompleto |
| Compras e fornecedores | Casos de uso de fornecedor, listas/pedidos de compra, recebimento e entrada de estoque; histórico de testes de recebimento | Jornada completa no console: necessidade → compra → recebimento → estoque → obrigação financeira | Backend/legado; UX nova pendente |
| M8 Entregas | Janelas, zonas, capacidade, entregadores, viagens, painel e roteiro impresso; geocodificação Google/Nominatim no backend | Configuração utilizável de origem/raio/frete, tratamento de endereço inválido, mapa/rota e conexão real com transportadora | Parcial; TomTom em PR; Lalamove em issue |
| Transportadoras | Fachada de demonstração no frontend para cotar/chamar/cancelar/consultar | Adapter real, credencial, cotação válida, criação, acompanhamento, cancelamento e fallback manual | Simulação não conta como integração |
| Automático e assistente | Processamento do agente, catálogo/caderno, transcrição, sugestão, execução com confirmação e transferência humano/automático | Homologar ciclo completo, coleta de dados, limites das ações, primeira resposta, transferência, erros de canal, horário e reconexão | Parcial; desligar automático transfere à dona no master |
| Notificações | Motor/outbox, tipos/canais, rotinas e avisos; trabalhos recentes de acesso por convite e recuperação | Sino, lembretes e Web Push conectados no console; comprovar entrega dos canais configurados | Backend; PRs de frontend abertas |
| M6 CRM/campanhas | Cadastro/dossiê, tags/consentimento/interesses e backend de campanhas com público e ondas | Listagem CRM, editor de campanha, resultados por venda paga, promoções, fidelidade e cupons de loja | Motor parcial; fidelidade/integrações desabilitadas no modo API |
| M7 Configurações | Horários/respostas/canais distribuídos; usuários e parâmetros no backend | Gestão única de equipe, perfis, chaves, testes de conexão, impressão, loja, backup e operação | Parcial; gestão de chaves em PR |
| Site | Vitrine/checkout e ligação ao atendimento registrados no código/histórico | Homologar catálogo, frete, pagamento, identificação do visitante e retorno do pedido com o console | Integração parcial; lead do chat em PR; repo do site não auditado nesta demanda |
| Continuidade operacional | PWA offline com sync; correções recentes de lotes, descarte, custos e estoque mínimo | Conflitos entre dispositivos, precache/reconexão comprovados, restauração de backup, alerta e recuperação | Parcial; módulo de conflitos ainda é decisão/spec pendente |

### Evidências de código para navegar

- Fundação e identidade: [modulos.js](../../../EasyStock.Console/src/dominio/modulos.js), [tokens.css](../../../EasyStock.Console/src/estilos/tokens.css), [App.jsx](../../../EasyStock.Console/src/app/App.jsx).
- Conexão com backend: [acoesApi.js](../../../EasyStock.Console/src/aplicacao/acoesApi.js), [naoLigadas.js](../../../EasyStock.Console/src/aplicacao/api/naoLigadas.js), [ModalGestao.jsx](../../../EasyStock.Console/src/features/gestao/ModalGestao.jsx), [comandaApi.js](../../../EasyStock.Console/src/infra/api/comandaApi.js).
- Pedido: [PedidoStateMachine.cs](../../../EasyStock.Domain/Sales/PedidoStateMachine.cs).
- Pagamento: [MercadoPagoClient.cs](../../../EasyStock.Infra.Integrations/Pagamentos/MercadoPago/MercadoPagoClient.cs), [MercadoPagoWebhookProcessor.cs](../../../EasyStock.Infra.Async/Pagamentos/Webhooks/MercadoPagoWebhookProcessor.cs).
- Impressão: [ImpressaoController.cs](../../../EasyStock.Api/Controllers/ImpressaoController.cs), [casos de uso de impressão](../../../EasyStock.Application/UseCases/Operacao/Impressao).
- Entregas: [casos de uso](../../../EasyStock.Application/UseCases/Atendimento/Entregas), [provedoresDeEntrega.js](../../../EasyStock.Console/src/infra/provedoresDeEntrega.js), [Geocoding](../../../EasyStock.Infra.Integrations/Geocoding).
- Bastidor: [Financeiro](../../../EasyStock.Application/UseCases/Financeiro), [Fornecedor](../../../EasyStock.Application/UseCases/Fornecedor), [Campanhas](../../../EasyStock.Application/UseCases/Campanhas).
- Backup offline e migração: [ADR-0059](../../adr/0059-web-sai-pwa-continua-e-merge-de-conflitos.md).

### Ações com aviso explícito de não conectadas

A lista naoLigadas.js registra reabrir conversa, alterar janela/endereço do pedido, lembretes, bloqueio de cliente, recebido na entrega, refazer cobrança, avaliações, parte das ocorrências, venda avulsa, integrações, fidelidade e cupons. Também há envio de modelo aprovado e automática da biblioteca ainda bloqueados.

Essa lista é por caminho de interface: janelas e entregas, por exemplo, já têm telas próprias conectadas; reemitir cobrança tem chamada real em comandaApi.js. Portanto uma ação bloqueada na ficha não significa ausência de toda a funcionalidade no ERP. Fechar essa diferença entre caminhos é parte central da UX do balcão.

## 5. Trabalho em andamento a reaproveitar

Snapshot do GitHub de 09/10. PR aberta não é entrega incorporada. Checks aprovados não substituem teste visual ou operação real.

| PR / issue | Trabalho existente | Destino no plano |
|---|---|---|
| [PR 1480](https://github.com/michel-az-de/EasyStok/pull/1480) / #1474 | Revisão visual do pedido e PWA | Revisar na onda 0; absorver nas ondas 1 e 2 |
| [PR 1492](https://github.com/michel-az-de/EasyStok/pull/1492) / #1490 | M2.1 estoque do dia | Onda 7; não iniciar implementação duplicada |
| [Issue 1491](https://github.com/michel-az-de/EasyStok/issues/1491) | M2.2 produção por item com peso real | Onda 7 |
| [PR 1304](https://github.com/michel-az-de/EasyStok/pull/1304) / #1246 | Chaves por loja, teste e vigia | Fundação de integração da onda 3 e reuso na 4 |
| [PR 1323](https://github.com/michel-az-de/EasyStok/pull/1323) / #1322 | Geocodificação e rotas TomTom | Onda 4; conferir provider selecionado |
| [Issue 1247](https://github.com/michel-az-de/EasyStok/issues/1247) | Lalamove e rota | Onda 4 |
| [PR 1401](https://github.com/michel-az-de/EasyStok/pull/1401) / #1399 | Estorno de estoque, descoberto e lote do pedido | Corrigir antes de homologar cancelamento na onda 2; regressão na 7 |
| [PR 1293](https://github.com/michel-az-de/EasyStok/pull/1293) / #1283 | Número do pedido do dia e congelado na comanda | Onda 5 |
| [PR 1425](https://github.com/michel-az-de/EasyStok/pull/1425) / #1424 | Mensagem programada no compositor | Onda 2 e validação do automático na 9 |
| [PR 1428](https://github.com/michel-az-de/EasyStok/pull/1428) / #1426 | Sino, lembretes e Web Push | Integrada e homologada localmente na seção 21; entrega externa de push pendente |
| [PR 1429](https://github.com/michel-az-de/EasyStok/pull/1429) / #1427 | SLA de primeira resposta | Integrada e homologada localmente na seção 22; aceite operacional na onda 9 |
| [PR 1431](https://github.com/michel-az-de/EasyStok/pull/1431) / #1430 | Identificação do lead do site | Onda 2 |
| [PR 1433](https://github.com/michel-az-de/EasyStok/pull/1433) / #1432 | Atendimento por e-mail | Onda 9, após fechar WhatsApp/site |
| [Issues 1407](https://github.com/michel-az-de/EasyStok/issues/1407) e [1408](https://github.com/michel-az-de/EasyStok/issues/1408) | Modelo aprovado e iniciar conversa | Onda 2 para operação manual; onda 9 para automação |
| [Issue 1245](https://github.com/michel-az-de/EasyStok/issues/1245) | Fidelidade e cupons de loja | Onda 10 |
| [PR 871](https://github.com/michel-az-de/EasyStok/pull/871) | Testes de integração no CI | Onda 0: investigar; estava draft com falha em Build + dotnet test |

A PR 1492 tinha checks em andamento na consulta. Diversas outras tinham checks verdes e homologação HTTP manual ignorada. Revisar diferenças contra o master antes de qualquer merge. As issues antigas #1237, #1239, #1240, #1242, #1243 e #1244 continuam abertas apesar de entregas posteriores; reconciliar seus aceites em vez de assumir que tudo falta.

## 6. Plano de execução por ondas

Recomendação: uma onda operacional por vez, dividida em pequenas entregas verticais. Cada fatia entrega tela, regra/API, persistência, permissões, erros e prova da jornada correspondente. Design e UX entram no aceite de toda onda.

### Onda 0. Estabelecer uma base confiável

**Resultado:** saber exatamente qual versão está sendo testada, usada e planejada.

1. Reconciliar este inventário com PRs e issues; registrar cada item como integrado, publicado ou homologado separadamente.
2. Revisar as PRs corretivas aplicáveis, principalmente #1480 e #1401; não juntar todas as PRs em lote.
3. Preparar ambiente de homologação com dados controlados e configuração real de API, sem demonstração disfarçada.
4. Alinhar a versão testada do console/API; registrar a versão pública antes e depois de uma futura publicação.
5. Revisar CI efetivo, instruções desatualizadas, backup e processo de restauração. Estabelecer baseline de falhas e avisos.

**Aceite:** um roteiro reproduzível abre loja/caixa, cria pedido manual, acompanha cozinha, conclui ou cancela e confere os efeitos. Falhas encontradas têm evidência e destino. Nenhum defeito impeditivo sem solução/fallback impede seguir.

**Dependência:** nenhuma. O acesso ao ambiente é necessário para homologação autenticada. Este levantamento entrega o inventário inicial, não conclui a onda.

### Onda 1. Navegação, design system e acessos

**Resultado:** a dona e a equipe encontram suas tarefas numa interface coerente.

1. Fechar mapa de módulos e telas; menu próprio, voltar ao hall, links entre pedido/cozinha/entrega e manutenção de contexto.
2. Aplicar tokens e componentes Casa da Baba: cores, fontes, espaçamento, estados, foco, contraste, campos, filtros, tabelas, diálogos e mensagens.
3. Implementar perfil × módulo na API e no frontend; entrada por perfil, convites e sessão da cozinha.
4. Tratar tela vazia, carregamento, erro, sem permissão e ação ainda indisponível. Evitar botão que promete operação inexistente.
5. Validar balcão desktop, cozinha tablet e consulta móvel; claro/escuro e teclado. Propagar o padrão a cada módulo quando ele for entregue.

**Aceite:** Dona, Atendimento e Cozinha executam seus acessos permitidos; URL direta não contorna a API; refresh e voltar preservam navegação; o mesmo controle tem comportamento visual consistente. A dona aprova telas representativas antes da aplicação geral.

**Dependência:** onda 0. Base: M0.1–M0.3 e M7 de usuários/perfis. A poda destrutiva M0.5 não é pré-requisito da navegação.

### Onda 2. Balcão e ciclo manual do pedido completos

**Resultado:** atender e resolver um pedido inteiro a partir do balcão, com cobrança manual e despacho próprio já disponível.

1. Consolidar conversa, cliente, comanda, resumo do pedido e próxima ação. Preservar o rascunho quando mudar de tela.
2. Cadastrar/editar cliente; tratar identificação do site, endereço, retirada/entrega, janela, observações e alterações antes/depois do pagamento conforme regra explícita.
3. Criar, revisar, enviar, registrar recebimento manual, preparar, concluir, cancelar e tratar ocorrência com confirmação e histórico.
4. Fechar os caminhos ainda não conectados da ficha: reabertura, troca de janela/endereço, recebimento na entrega, bloqueios e ocorrências, reaproveitando APIs existentes.
5. Integrar lembretes, SLA, notificações e mensagens programadas das PRs existentes; fechar iniciar conversa e modelos aprovados para o operador.
6. Unificar status e falhas de texto, áudio/foto, reenvio e transferência entre pessoas/automático. Imprimir via navegador o documento já disponível como fallback até a onda 5.

**Aceite:** cenários de retirada, entrega, pagamento na entrega, cancelamento e erro de envio passam sem alterar banco manualmente. Após recarregar e abrir em outra sessão, pedido, pagamento, estoque e caixa continuam coerentes. Duplo clique e reenvio não duplicam efeitos.

**Dependência:** ondas 0–1; correção de estorno de estoque quando aplicável. Integração paga de frete e pagamento online chegam nas ondas seguintes.

### Onda 3. Mercado Pago e conciliação do pedido

**Resultado:** gerar cobrança no balcão/site e enxergar a confirmação real no mesmo pedido.

1. Revisar #1304, definir uma fonte efetiva das credenciais e mostrar o estado da conexão sem revelar segredos.
2. Validar conta/aplicação/ambientes e configuração de entrega mínima para o checkout, usando retirada quando o cenário permitir.
3. Fechar jornada com Checkout Pro: gerar/reemitir link, enviar, pagar, receber webhook e atualizar conversa, pedido, caixa e autorização de preparo.
4. Tratar recusa, pendência, expiração, abandono, indisponibilidade, pagamento duplicado/tardio, cancelamento e estorno. Revisar #1302, sobre liberação de vaga.
5. Conferir reconciliação quando o webhook atrasar ou não chegar; status de pagamento vem do provedor/backend, nunca de um retorno visual do site.

**Aceite:** sandbox comprova sucesso, recusa, expiração, repetição e estorno sem baixa duplicada. Depois, transação de produção controlada com responsável autorizado, referência rastreável e conferência financeira. Cada ambiente tem evidência própria.

**Dependência:** onda 2; acesso à conta e credenciais pelo responsável. Não criar outra integração de pedidos se a existente puder atender.

### Onda 4. Entrega e geolocalização completas

**Resultado:** do endereço informado à entrega confirmada, com preço e acompanhamento compreensíveis no balcão.

1. Definir/configurar endereço e coordenadas da cozinha, áreas, faixas de frete, capacidade, janelas e exceções.
2. Diferenciar localização do cliente, busca de endereço/CEP, cálculo de cobertura e rota rodoviária. Permissão de localização negada deve permitir digitação manual.
3. Rever #1323 antes de escolher/manter o provedor de mapas. Conferir quotas, configuração e tratamento de endereço ambíguo ou não localizado.
4. Fechar primeiro o despacho próprio: atribuir entregador, roteiro, saída, acompanhamento, entregue, falha/devolução e pagamento recebido.
5. Integrar uma transportadora por vez, começando pela escolhida pela operação: cotar → confirmar custo → solicitar → acompanhar → concluir/cancelar. Lalamove tem issue #1247; 99/Uber dependem de viabilidade comercial/técnica confirmada, sem supor API disponível.
6. Em falha do provedor, exibir motivo e permitir despacho manual registrado. Não confundir frete cobrado do cliente com custo da transportadora.

**Aceite:** endereço dentro/fora da área, janela cheia, localização negada e falha de cotação têm resposta clara. Uma entrega própria e uma da transportadora escolhida fecham com referências reais e retorno ao pedido, sem duplicar chamada.

**Dependência:** ondas 2–3 e contas/configurações. A parte de mapas/entrega própria pode avançar se a credencial da transportadora estiver pendente; a integração externa continua sem aceite até a prova real.

### Onda 5. Cozinha, expedição e impressão física

**Resultado:** um pedido apto entra na cozinha, gera papel legível, é preparado e expedido com conferência.

1. Fechar fila, prioridades, prazos, encomendas e atualização em tempo real; cancelamento deve aparecer claramente para a cozinha.
2. Separar preparo, conferência/embalagem e expedição; ligar retirada/entrega ao balcão. Rever #1293 para número do dia e identificação de congelado.
3. Conectar o consumidor da fila à Oasis OIA-8381-B informada por Felipe e ao computador/tablet que a usará. Proposta inicial: validar driver e impressão via USB no Windows; depois testar Bluetooth no dispositivo escolhido. Definir padrão por dispositivo, sem presumir impressão direta do navegador por Bluetooth.
4. Distinguir recibo não fiscal do cliente, comanda da cozinha, etiqueta de lote e roteiro de entrega; usar cada modelo no lugar correto.
5. Registrar tentativas, falha, sucesso e reimpressão. Tratar impressora desconectada, sem papel e reinício; retorno de software não comprova sozinho que saiu papel.

**Aceite:** pedido real controlado gera recibo e comanda físicos com número, itens, observações, status do pagamento e acentos corretos, sem corte indevido. Desconexão e reconexão preservam fila; reimpressão é explícita e rastreável.

**Dependência:** onda 2 e acesso ao equipamento; testar os meios da onda 3 e a expedição da 4 no aceite conjunto. Equipamento informado: Oasis OIA-8381-B, USB e Bluetooth, 203 dpi, área de impressão anunciada de 100 × 150 mm, sem Wi-Fi nem Ethernet. São dados do anúncio fornecido por Felipe, ainda sem validação física. O anúncio descreve uma etiquetadora: usar pedido/comanda 100 × 150 mm como primeiro cenário; confirmar mídia, sensor, margens, protocolo e suporte a papel contínuo antes de prometer recibo em rolo. Não assumir ESC/POS nem reutilizar automaticamente o caminho da WTP05.

### Onda 6. Cardápio comercial completo

**Resultado:** a dona cadastra e altera a oferta uma vez e os canais vendem a mesma oferta.

1. Partir de M1.1–M1.3 já integradas; não reimplementar lista/categorias.
2. Concluir edição de produto, fotos, porções/variações, disponibilidade, preços, observações e alergênicos.
3. Definir e implementar adicionais/combos com efeito claro no preço, preparo e estoque. Decisões pendentes de M1 viram regra antes da respectiva fatia.
4. Exibir a mesma oferta no balcão, site e agente; preservar dados históricos de pedidos quando o cardápio mudar.

**Aceite:** cadastrar, alterar, pausar e arquivar item reflete nos canais; pedido usa preço correto, não vende oferta indisponível por erro de cache e mantém sua composição histórica. Combo/adicional chega corretamente à cozinha.

**Dependência:** ondas 1–2; integrações das ondas 3–5 usadas nos cenários. Base: [M1](02-m1-cardapio.md).

### Onda 7. Produção, estoque, etiquetas e backup offline

**Resultado:** produzir e vender atualiza saldo, lotes e custos sem divergência entre console e PWA.

1. Incorporar M2.1 (#1492) e desenvolver M2.2 (#1491) na sequência, com UX do módulo próprio.
2. Fechar insumos, fichas técnicas, rendimento/peso real, planejamento, produção do dia, perdas, embalagens e etiquetas de lote.
3. Definir quando baixar insumo/embalagem, como contar porções e como tratar saldo descoberto. Reaproveitar a regra existente de venda sem bloqueio por saldo, salvo nova decisão explícita.
4. Validar entradas/saídas, validade, ajustes com motivo, cancelamento e estorno de lote; conferir as correções recentes do PWA no aparelho.
5. Implementar versão-base no sync, fila de conflitos, aviso e resolução pela dona conforme ADR-0059. Proteger reenvio, reconexão e duplicação.

**Aceite:** insumo → produção → lote/etiqueta → venda → baixa/perda/estorno bate nos dois clientes. Editar a mesma entidade offline e online gera conflito visível; a decisão da dona propaga sem perder o registro original nem duplicar saldo.

**Dependência:** ondas 1, 2 e 6; impressão de etiquetas validada com o dispositivo correspondente. Base: [M2](03-m2-producao.md) e ADR-0059.

### Onda 8. Compras, fornecedores e financeiro gerencial

**Resultado:** comprar, receber, pagar e fechar a operação pelo novo console.

1. Colocar fornecedores, listas/pedidos de compra e recebimento parcial/total no console, integrados ao estoque da onda 7.
2. Fechar contas a pagar/receber, parcelas, vencimentos, baixas, estornos, categorias e centros de custo necessários à Casa da Baba.
3. Unificar fonte dos totais de caixa e distinguir dinheiro físico, cartão/Pix, contas pendentes, taxa e custo de entrega.
4. Entregar venda avulsa, conferência da gaveta, fechamento/reabertura com motivo, histórico, extrato e relatórios de vendas/canais, custos/perdas e caixa.
5. Resolver o destino do caixa da PWA antes de retirar qualquer função; o ADR-0059 preservou produção/cadastro e deixou essa parte pendente.

**Aceite:** compra recebida movimenta estoque e obrigação financeira; pagamento baixa uma vez; venda recebida e estornada reconcilia pedido/caixa/contas. Fechamento explica diferenças, e relatório rastreia valores até as operações de origem.

**Dependência:** ondas 3 e 7, com despacho da 4 para custos de entrega. Base: [M5](06-m5-caixa.md), revisado conforme ADR-0059. O menu pode acomodar compras/estoque em M2 e financeiro em M5 sem inventar módulos novos antes de validar a navegação.

### Onda 9. Atendimento automático de ponta a ponta

**Resultado:** o automático resolve cenários autorizados e transfere o restante com contexto preservado.

1. Usar catálogo, horários, entrega e pagamentos que já passaram pelos aceites anteriores; não automatizar um fluxo ainda inconsistente.
2. Fechar identificação do cliente, entendimento de texto/áudio, montagem/revisão da comanda, cobrança e acompanhamento.
3. Definir quais ações são sugestões, quais exigem confirmação e quais podem ocorrer automaticamente. Preservar as decisões de controle da dona.
4. Homologar primeira resposta, ausência de operador, fora de horário, mensagens em rajada, retomada, limite do canal, falha da IA e transferência humano/automático.
5. Revisar notificações e a PR de e-mail #1433; fechar WhatsApp e site antes de expandir canais. Histórico para RAG exige decisão específica sobre fonte e uso; não tratar como já autorizado/implementado.

**Aceite:** conjunto acordado de conversas realistas cobre sucesso, dúvida, indisponibilidade, mudança de pedido, recusa de pagamento, falha de entrega e intervenção humana. O agente não inventa preço/prazo/saldo nem executa ação fora da permissão; desligar automático entrega a conversa à pessoa.

**Dependência:** ondas 2–7. Melhorias locais do agente e transferência não precisam esperar esta onda; o que fica para cá é a homologação da autonomia sobre o ciclo completo.

### Onda 10. CRM, campanhas, promoções e fidelidade

**Resultado:** selecionar público e executar campanhas mensuráveis dentro da operação da loja.

1. Entregar lista CRM com busca, tags, histórico, consentimentos e interesses.
2. Ligar tela de campanhas ao motor existente: público, exclusões, conteúdo, aprovação, agenda, ondas e cancelamento.
3. Implementar promoções com validade, cupons e fidelidade por loja; definir acumulação, resgate, cancelamento e devolução dos benefícios.
4. Medir envio, falha, resposta e pedido pago atribuível; impedir que pedido apenas criado seja apresentado como receita.

**Aceite:** campanha pequena autorizada percorre seleção, revisão, envio e resultado; exclusões são respeitadas; cupom/resgate altera o pedido corretamente, inclusive no estorno.

**Dependência:** ondas 3, 6, 8 e 9 para os canais automatizados. Base: [M6](07-m6-campanhas.md) e #1245. A execução de campanhas reais precisa de autorização específica.

### Onda 11. Encerrar a transição e operar só na nova versão

**Resultado:** todas as tarefas acordadas da Casa da Baba são executadas no console e no backup offline previsto.

1. Conferir matriz Web → console por área: catálogo, clientes, produção, estoque, compras, fornecedores, caixa, financeiro, etiquetas, relatórios e configurações.
2. Homologar jornada completa com dona/equipe: abrir loja → produzir → atender/vender → cobrar → preparar/imprimir → entregar → fechar caixa → conferir relatórios.
3. Exercitar restauração de backup em ambiente isolado, reconexão da PWA, impressora indisponível, queda de provedor, monitoramento e retorno a uma versão anterior.
4. Desativar cada área antiga só depois do uso aprovado do equivalente. Poda de dados/tabelas exige inventário, backup recuperável e escopo confirmado; não fazer limpeza só para o menu ficar simples.
5. Registrar versão publicada, evidência de aceite, responsáveis e procedimento de suporte. Confirmar os acessos de Configurações e o estado dos canais.

**Aceite:** ciclo operacional acordado concluído pela equipe sem recurso às telas antigas, sem divergência não explicada de pedido/pagamento/estoque/caixa; recuperação demonstrada. Pendências residuais têm impacto, responsável e prioridade explícitos.

**Dependência:** ondas operacionais anteriores. Homologação e retirada parcial do Web acontecem ao longo delas; esta onda fecha o conjunto.

## 7. Ordem e marcos

Sequência sugerida: 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11.

As dependências técnicas permitem adiantar trabalho já iniciado: M2.1/#1492 pode continuar sem esperar todas as integrações; verificar driver/mídia da Oasis, credenciais e condições de entrega deve começar na onda 0. Isso não muda o critério de concluir cada jornada com provas.

| Marco | Evidência de resultado |
|---|---|
| Após 2 | Balcão manual utilizável, com pedido e efeitos consistentes |
| Após 5 | Operação vende, cobra online, despacha, prepara e imprime fisicamente |
| Após 8 | Oferta, produção, estoque, compras e financeiro operados no console |
| Após 9 | Atendimento automático homologado sobre processos confiáveis |
| Após 11 | Transição concluída com backup offline e recuperação demonstrada |

Não há prazo ou orçamento fechado: faltam capacidade da equipe, decisões de negócio e validação das integrações. Estimar cada fatia depois do roteiro e das dependências definidos, usando a velocidade real de entrega. Prazo de credencial/aprovação externa fica separado de esforço de código.

## 8. Definição de concluído para toda fatia

1. Cenário e regra claros, incluindo quem pode agir e quais dados serão persistidos.
2. UX com carregamento, vazio, erro, sucesso, confirmação quando necessária e recuperação.
3. Backend e frontend conectados em modo API; sem simulação apresentada como gravação.
4. Testes adequados ao risco: unitário/integração para dinheiro, estoque e permissões; percurso visual para navegação; dispositivo real para impressão/offline.
5. Recarregar/outra sessão confirma persistência; repetição e falha não duplicam efeitos.
6. Revisão e merge separados da publicação. Publicação separada de homologação. Registrar SHA e evidência de cada estado.
7. Aceite operacional da dona/equipe no ambiente correto e procedimento de recuperação aplicável.

Campos mínimos do acompanhamento: onda, fatia, resultado, dependência, responsável, issue/PR, SHA integrado, SHA publicado, prova e pendência. Estados sugeridos: planejada → em execução → em revisão → integrada → publicada → homologada. Dependência externa é uma marca adicional, não equivale a conclusão.

## 9. Decisões e dependências que não devem ser inventadas

| Informação | Necessária para | Encaminhamento |
|---|---|---|
| Oasis OIA-8381-B: USB/Bluetooth, 203 dpi, área anunciada 100 × 150 mm | Onda 5 | Modelo informado por Felipe em 09/10; falta verificar driver/protocolo, mídia carregada e dispositivo hospedeiro. USB/Windows é proposta inicial |
| Motoboy próprio e transportadora prioritária; conta/acesso técnico | Onda 4 | Perguntado a Felipe; não presumir APIs de 99/Uber |
| Endereço/origem, raio, valores, horários e capacidade | Ondas 3–4 | Conferir cadastro com a dona; snapshot antigo não prova estado atual |
| Conta e acesso Mercado Pago em teste/produção | Onda 3 | Responsável configura em canal seguro; sem segredos na documentação |
| Matriz de permissões e exceções de operação | Ondas 1–2 | Partir dos três perfis já decididos; confirmar apenas exceções |
| D3-04: cancelamento de pedido pago | Onda 2 | Felipe decidiu em 09/10: Atendimento cancela sem pagamento; com qualquer valor recebido, inclusive parcial, só Dona/gerente. Implementação e limites na seção 23 |
| Regras de combos, adicionais, insumos e embalagens | Ondas 6–7 | Resolver as D-M1/D-M2 correspondentes antes da fatia |
| Destino do caixa offline da PWA | Onda 8 | Decisão pendente explicitamente no ADR-0059 |
| Identidade de tela e ativos aprovados | Onda 1 | Validar aplicação visual; não refazer marca por conta própria |
| Fonte/uso de histórico para RAG e limites da autonomia | Onda 9 | Definir caso de uso e confirmação das ações |

Dependência externa bloqueia o aceite correspondente, não o restante do levantamento ou as frentes independentes.

## 10. Validação executada nesta demanda

| Verificação | Resultado | Limite da evidência |
|---|---|---|
| npm run qualidade no console | Passou: lint, camadas e build; build com 392 módulos | Lint emitiu avisos de acessibilidade, chaves duplicadas e outros; não representa aceite visual |
| 14 scripts de prova do console | Todos saíram com código 0 | Provas de funções/contratos e chamadas simuladas, não E2E de produção |
| dotnet test Api.UnitTests com filtro MercadoPago ou Impressao | 103 aprovados, 0 falhas, 0 ignorados; net10.0 | Não chama Mercado Pago real nem imprime em dispositivo |
| Git local/remoto e PRs | Base identificada e PRs consultadas ao vivo | Snapshot de 09/10; pode mudar depois |
| HTTP público e metadados de versão | Console/API/site responderam 200; divergência de SHA registrada | Disponibilidade HTTP não comprova login, checkout ou rotina da loja |

Provas do console executadas: prova-1447-hall-modulos, prova-f06-honestidade, prova-f03-pedido-api, prova-1255-pagamento-manual-api, prova-1446-cozinha-viva, prova-1440-janelas-e-roteiro, prova-1443-loja-abre-caixa, prova-1441-respostas-tags-notas, prova-1481-gestao-cardapio, prova-1482-item-cardapio, prova-1483-categorias-cardapio, prova-f11-lote-papel, prova-1445-assistente-acoes e prova-f07-sincronizacao, todos em EasyStock.Console/ferramentas com extensão .mjs.

Não executados: suíte completa de backend, integração com banco real, teste visual autenticado, pagamento real, cotação/corrida real, impressão física e validação no aparelho offline. São aceites previstos nas ondas, não entregas desta análise.

## 11. Primeira execução recomendada

Começar pela onda 0, revisar a PR visual #1480 e a correção #1401 e preparar o roteiro único do pedido. Na sequência, fechar navegação/DS/perfis da onda 1 e a operação manual da onda 2. Em paralelo apenas no planejamento, confirmar driver/mídia da Oasis, conta de pagamentos e entregas para não descobrir esses bloqueios no fim.

Não abrir outro conjunto de implementações duplicadas: atualizar as issues existentes com a onda e o aceite, abrindo novas somente para lacunas confirmadas. Este documento não cria nem modifica issues e não realiza merge ou deploy das PRs listadas.

## 12. Execução autorizada da onda 0, 09/10/2026

Felipe aprovou a continuação após o levantamento. As seções anteriores preservam o snapshot da análise inicial; esta seção registra o trabalho de implementação posterior. Desenvolvimento em worktree isolado, mantendo os demais trabalhos locais.

### Correções e conciliação

- PR #1401 revisada e atualizada para `425324a34fb48876ada782a982db3523f835137a`: estornos de estoque/caixa e consumo FEFO. A revisão reproduziu uma corrida em que o saldo diminuía entre a consulta inicial e o lock; agora o serviço recusa descoberto quando a política não permite estoque negativo. Novos testes cobrem saldo concorrente, quantidades fracionadas e devolução aos lotes originais.
- PR #1480 revisada e atualizada para `d7a21b59aacb71fbff76a1bd6d46f41dcf201eb4`: três conflitos resolvidos preservando as entregas atuais do cardápio e do controle de atendimento. A revisão corrigiu itens avulsos excluídos da cozinha/PWA, pareamento cruzado entre empresas e seleção de cliente homônimo por nome. Bloqueio agora usa a conversa escolhida.
- Sync PWA: erro de gravação mantém o cursor para reenvio; timeout não pula dados antigos ainda não recebidos. Duas regressões foram reproduzidas antes da correção. Isso não implementa a resolução de conflitos entre edições offline, que continua na onda 7.
- Provas da cozinha e da revisão #1474 incluídas no workflow do console. Avisos adicionados à entrada da `.knowledge` para distinguir os snapshots antigos da base atual.
- PR #1401 integrada em `master` como `96516cb2` após build/testes, análise CodeQL e secret-scan aprovados. O job manual de homologação HTTP do GitHub foi ignorado; a prova HTTP desta seção foi executada localmente.
- PR #1480 integrada em `master` como `1a8cbdb9` após build/testes, CodeQL, secret-scan, console e PWA aprovados. A homologação HTTP manual do GitHub permaneceu ignorada; não houve deploy. As correções da revisão e a conciliação com M2 foram preservadas.
- Durante esta execução, outro trabalho integrou #1492 e #1495: estoque do dia e produção por prato com peso real/etiquetas. Essas entregas foram conciliadas, preservadas e revalidadas aqui. Não são implementações feitas por esta revisão; o inventário inicial as listava antes do merge. A seção de M2 em `03-m2-producao.md` contém seu escopo atualizado.

### Evidência local

| Verificação | Resultado |
|---|---|
| Correção de estoque, Application | 63 aprovados, sem ignorados |
| ItemEstoque, Domain | 14 aprovados, sem ignorados |
| Estoque, pedido, caixa, PostgreSQL real | 18 aprovados, sem ignorados |
| Criação concorrente de pedido por HTTP, PostgreSQL real | 1 aprovado, sem ignorados |
| Atendimento, checkout e cardápio, Application | 630 aprovados, sem ignorados |
| Controllers, pareamento, calculadora e sync, API | 105 aprovados, sem ignorados |
| Cozinha/KDS e atendimento, PostgreSQL real | 9 aprovados, sem ignorados |
| Console | lint/camadas/build aprovados; 47 scripts de prova aprovados; ficha novamente aprovada com 23 verificações após correção de homônimos |
| PWA | 167 aprovados, 1 ignorado por IndexedDB ausente no sandbox; 2 scripts ESM não executados pelo runner |
| Gate de commit | build aprovado e 36 testes de arquitetura aprovados |
| Verificador HTTP | 8 testes aprovados e console/API locais respondendo com o mesmo SHA `d7a21b59` |
| Conciliação com #1492/#1495 | 242 testes de aplicação e 11 de integração PostgreSQL aprovados; qualidade do console e provas 1490, 1491, 1447 e ambas 1474 aprovadas novamente |

Jornada executada por `scripts/homologacao/onda0-http.ps1` em API real com autenticação JWT e PostgreSQL 17 descartável, sem usar os bancos das outras stacks. Abriu loja/caixa, criou pedido avulso de 2,5 unidades a R$ 10, confirmou a cozinha, recebeu R$ 25, percorreu preparando/pronto/entregue e releu cada estado. Recebeu/cancelou outro pedido de R$ 15 e conferiu fechamento de R$ 125, incluindo R$ 100 de abertura. Identificadores de teste: entregue `9520d7ca-59be-4a2a-808a-d0016ac02c3a`; cancelado `c281d1ba-d3f7-490a-bf7a-95e7a7326412`.

Backup local: `pg_dump -Fc` e `pg_restore --exit-on-error` para outra base descartável. Quantidade e hash do conteúdo de pedidos (2), itens (2), pagamentos (2) e fechamentos (1) iguais ao original. SHA-256 do dump de teste: `CA7D215D0AE30FCA1513678D2380FF8ADF8D617E44B0777A543B12D0F788CC4C`. Logs/TRX/dump ficam no worktree em `.build/onda0-tests`, fora do Git.

Backup externo: acesso SSH `hostinger` confirmado. O log de 09/10 registra envio e `cryptcheck` de dois arquivos sem diferenças. Executado o script existente `scripts/infra/backup-externo/testar-restauracao.ps1`: baixou `diario/2026-10-09`, restaurou o dump em PostgreSQL 17 descartável na VPS e confirmou **159 tabelas** e **230 entradas legíveis** no tar de uploads. Execução com saída 0; container descartável removido pelo próprio script, sem restauração sobre o banco de produção. Isso verifica recuperação técnica do backup, não aceite funcional da aplicação restaurada.

Pendências encontradas no backup: a cópia de `enviar.sh` instalada na VPS registra `tmp: unbound variable` na limpeza após o envio; o arquivo canônico local já usa expansão antecipada no trap. Reconciliar essa instalação separadamente. O rclone emite aviso sobre descontinuação do `client_id` compartilhado do Google Drive; configurar cliente próprio com o responsável pela conta antes de depender da continuidade desse acesso. Nenhuma credencial foi alterada nesta execução.

### Limites e próximos aceites

- Revisão e teste local não são publicação. Os SHAs públicos registrados na seção 3 não foram substituídos por um deploy nesta execução.
- A abertura do navegador automatizado falhou no início por `windows sandbox failed: apply deny-read ACLs`. O aceite visual autenticado permanece pendente; as provas JavaScript e HTTP não o substituem.
- Recuperação técnica do backup externo comprovada acima. Teste funcional da aplicação restaurada, correção do script instalado e continuidade da autenticação do rclone seguem pendentes.
- Não há workflow `build-casa-da-baba-apk.yml` na árvore atual, nem cópia MAUI ativa para sincronizar. O workflow PWA vigente roda `node tests/pwa/run.js`; não foi gerado APK nem testada a Oasis.
- Não fechar a onda 0 como homologada visualmente. A próxima fatia é completar esse aceite e então apresentar navegação/DS/perfis da onda 1 para aprovação das telas representativas. API pública `701dbf4b` e console público `888eeab7` foram reconferidos após os testes e continuam anteriores às correções; não houve deploy nesta execução.

## 13. Continuação autorizada: identidade e navegação, 09/10/2026

Felipe autorizou concluir os aceites por ele e continuar. Base desta fatia: `a680c639`, incluindo insumos #1497 entregues em outro trabalho. Esta seção atualiza as pendências da seção 12; não transforma o inventário inicial em funcionalidades publicadas.

### Implementado

- Identidade do Console unificada: cacau/caramelo/creme, Lora e Nunito Sans, escala maior, claro/escuro e estados sem os aliases antigos dos tokens. Ragu nos nomes dos pratos continua sendo conteúdo válido.
- Logo completa no login, hall e balcão, com placa clara no escuro. Derivada PNG 420 × 280, 88.397 bytes, sem recorte/redesenho. SHA-256 da fonte: `785C453D6A011A0002EFB8173C3E27D42508E9792D75826E7314B2EF63DB56BC`.
- Login por uma chamada a `/api/auth/login`; a API resolve a empresa e o Console confere sua presença no JWT antes de guardar sessão. Não chama mais `lista-empresas`. Google preservado, mas OAuth real não exercitado aqui.
- Um guarda de sessão para todas as rotas. Busca de módulos e telas por nome, ignorando acentos/caixa, com estado vazio e atalhos diretos.
- Corrigido o corte lateral da ficha: colunas cedem espaço respeitando mínimos, inclusive com larguras salvas maiores que a janela.
- Na demonstração, navegar para Cozinha/Entregas dentro do módulo mantém o provider do balcão vivo; antes o espelho perdia sua origem. Apelidos de janelas avulsas continuam independentes. Rascunho preservado no percurso balcão → módulos → cozinha → balcão.
- Verificador de contraste no `npm run qualidade`; prova de busca/autenticação no workflow do Console.

### Evidência

| Verificação | Resultado e limite |
|---|---|
| Console | Lint/camadas/build aprovados; avisos preexistentes do lint permanecem |
| Provas JavaScript | 51 scripts aprovados, incluindo busca e login de uma etapa |
| Contraste | 264 pares aprovados nos dois temas, incluindo texto, ações, estados, avisos, bordas e foco; não equivale a auditoria WCAG completa |
| API local atual | Compilada em net10.0 e conectada ao PostgreSQL descartável da onda 0 |
| Navegador autenticado | Login real, busca por Insumos, abertura/volta de módulo, aliases e logout; 5 telas × 2 temas × 2 tamanhos (1280 × 800 e 1024 × 768), sem excesso de largura do documento/root |
| Demonstração preenchida | Conversa, ficha, cozinha no mesmo tab, rascunho e larguras salvas conferidos; massa simulada não comprova envio a clientes |
| Peso | Base de 1.218.522 bytes; final de 1.335.434 bytes; crescimento de 116.912 bytes, abaixo dos 150.000 da M0.1 |
| Gate de commit | Build aprovado e 36 testes de arquitetura aprovados |
| Integração | Commit `e0d401bd` integrado por fast-forward e enviado ao master; check remoto do Console aprovado |
| Capturas | 12 imagens antes/depois autenticadas e 2 do balcão demonstrativo, em `evidencias-onda1/` |

Capturas: [balcão claro demonstrativo](evidencias-onda1/depois-balcao-demonstracao-light.png), [balcão escuro demonstrativo](evidencias-onda1/depois-balcao-demonstracao-dark.png), [cozinha autenticada](evidencias-onda1/depois-cozinha-light.png) e [caixa autenticado](evidencias-onda1/depois-gestao-dark.png). A base local tinha caixa fechado e pedidos da jornada HTTP anterior, mas nenhuma conversa aberta; o aceite autenticado do balcão cobre seu estado vazio. Ciclo completo de pedido real por interação no balcão continua sendo aceite da onda 2.

Playwright já instalado permitiu executar o navegador após o CUA continuar falhando na inicialização do sandbox. Antes de encerrar o worktree, logs, scripts locais, TRX, dump da onda 0 e capturas adicionais foram preservados em `C:\rep\EasyStok\.build\validacao-casa-da-baba-20261009`, fora do Git: 163 arquivos conferidos por SHA-256. A API/servidores locais foram encerrados; o container PostgreSQL de teste foi parado, com os dados mantidos. Isso não homologa a versão publicada.

### Backup e próximo trabalho

A cópia instalada de `/home/felipe/backup-externo/enviar.sh` diferia da canônica apenas pelo trap. Preservada como `enviar.sh.antes-onda1-20261009` e substituída após conferir seu hash. `bash -n` aprovado; ensaio do trap instalado em processo isolado saiu com 0 e removeu o diretório temporário. SHA-256 instalado: `8a3d114b844c6a06eb6a2a00dbba4f1425dfaac592992c8d5ef88b886bade5f1`. Nenhum banco restaurado sobre produção, nenhuma credencial alterada e nenhum novo expurgo/envio disparado para testar a limpeza.

Confirmada a ausência de `client_id` próprio do Google Drive, sem imprimir valores de credenciais. Essa configuração depende do acesso à conta/projeto OAuth responsável e permanece pendente.

**A onda 1 não está integralmente concluída.** Próxima fatia: M0.3, matriz Dona/Atendimento/Cozinha aplicada pela API e shell, porta por perfil, bloqueio de rota digitada e classificação dos controllers. Sino real transversal e sessão longa da cozinha também permanecem pendentes. Tenant, permissões de backend e migrations não foram alterados nesta fatia. Mercado Pago real, transportadoras/geolocalização e Oasis mantêm os aceites das respectivas ondas. Não houve deploy da aplicação nesta execução.

## 14. Continuação autorizada: perfis e acesso aos módulos, 09/10/2026

Fatia funcional da M0.3 implementada sobre `771da5c6`, incluindo produção M2.4b. Não representa encerramento da onda 1 nem deploy. A referência anterior da seção 13 passa a ser histórica.

| Perfil | Módulos liberados | Entrada |
|---|---|---|
| Dona | Todos os 8 | Sala de módulos |
| Atendimento | Cardápio, Atendimento, Cozinha, Financeiro e Entregas | Balcão |
| Cozinha | Produção e Cozinha | Fila de preparo |

### O que mudou

- Enum de módulos, oito permissões de acesso e fallback por nível conforme a matriz decidida. Lista explícita continua prevalecendo sobre o nível. Claims explícitas inválidas negam acesso, sem transformá-las em fallback permissivo.
- `GET /api/auth/me/modulos` fornece matriz, entrada e capacidade de editar o Cardápio. O identificador do caixa é `financeiro`, compatível com as rotas existentes do Console.
- Classificação explícita de todos os controllers em `Api/Authorization/ModulosConvention.cs`. Um controller novo sem classificação falha no teste e na inicialização. A convenção MVC acrescenta requisito de módulo às policies e permissões finas existentes. Catálogo compartilhado, leitura KDS de Entregas e impressão têm exceções restritas. Controllers públicos, de infraestrutura, do bastidor Web e do backup Mobile permanecem com seus contratos próprios.
- Migração aditiva `20261009170822_AddPerfilModuloInicial`: uma coluna anulável de até 30 caracteres. Login por senha, finalização de login Google e refresh transportam a entrada no JWT.
- Seed idempotente de Dona, Atendimento e Cozinha na empresa resolvida pela configuração existente `Auth:Google:EmpresaPadrao`. Não cria usuários, não atribui perfis automaticamente e não sobrescreve perfis existentes de mesmo nome. Sem empresa configurada, registra que o seed não foi executado. Não usa empresa fictícia como destino de produção.
- Corrigido defeito encontrado no teste HTTP: `UsuarioRepository.GetByIdAsync` não carregava as permissões, e o refresh ampliava o acesso ao fallback do nível. A consulta agora carrega permissões, com teste de regressão em contexto novo e confirmação HTTP.
- Console aguarda a matriz antes de montar módulos, filtra cartões e atalhos, respeita entrada por perfil, bloqueia links digitados e aliases e oferece saída/retentativa quando a matriz falha. A produção da Cozinha lê o catálogo sem carregar conversas, expediente ou respostas prontas. Eventos de conversa não são enviados ao SSE da Cozinha/Entregas sem acesso a Atendimento.
- Atendimento consulta itens e categorias do Cardápio. Edição de cadastro continua exigindo Gerente na API; controles reservados ficam desabilitados/ocultos. Ligar/desligar a disponibilidade de hoje mantém a regra anterior de Operador. Fechamento de caixa continua reservado a Gerente.

### Evidência da fatia

| Verificação | Resultado |
|---|---|
| API unitária | 952 testes aprovados, incluindo classificação, requisitos, seed, claims, refresh e SSE |
| Domínio | 53 testes de acesso/permissão aprovados; inclui 5 níveis × 8 módulos |
| Login | 12 testes de aplicação aprovados |
| Console | Lint, camadas, 264 pares de contraste e build aprovados; 54 provas JavaScript aprovadas |
| PostgreSQL 17 local | 157 migrations aplicadas em base nova; reinícios preservaram um perfil de cada nome, com 19/7/3 permissões |
| HTTP real | 51 verificações em `scripts/homologacao/onda1-perfis-http.ps1`; três logins, matriz, portas, 200/401/403, consulta de Cardápio, bloqueio de edição/fechamento e refresh sem ampliar acesso |
| Navegador real | Três logins e saídas; 8/5/2 cartões; entrada correta; Cozinha navega na produção sem buscar conversas; rotas proibidas não consultam caixa; consulta de Cardápio pelo Atendimento; falha de carga de permissões bloqueia e permite retentar; sem erros JavaScript |
| Limites | Base de teste local, sem OAuth Google real, usuários reais, envio a clientes ou pagamento externo |

Capturas: [Dona](evidencias-onda1/perfis-dona-hall.png), [Atendimento](evidencias-onda1/perfis-atendimento-hall.png), [Cozinha](evidencias-onda1/perfis-cozinha-hall.png) e [rota bloqueada no celular](evidencias-onda1/perfis-cozinha-negado-celular.png). Logs, TRX e roteiro do navegador ficam em `C:\rep\EasyStok\.build\validacao-casa-da-baba-20261009\perfis`, fora do Git. O banco isolado chama-se `easystock_onda1_perfis`.

### Pendências e próxima sequência

1. **M0.3 ainda parcial:** mantidas as 18 permissões legadas no enum nesta entrega. Sua retirada e a limpeza de dados exigem a migration e o teste de login legado previstos na especificação. Não apagar a última permissão explícita de um perfil sem avaliar o efeito do fallback. A lista atual tem 37 membros, não os 19 finais.
2. Preparar a publicação e conferir a empresa configurada e os vínculos de pessoas reais. Perfis antigos e seus vínculos foram preservados. Perfil com lista explícita sem permissões de módulo será negado no Console, conforme a regra decidida; a implantação precisa revisar esses perfis. Nenhuma atribuição de função a pessoa real foi inferida.
3. Completar a UX por ação dos outros módulos, sino transversal e sessão longa da Cozinha. O CRUD/editor de perfis permanece na M7.2. Algumas telas antigas continuam exibindo aviso de ainda não ligadas.
4. Seguir para a onda 2: ciclo completo de pedido, interação e exceções no balcão. Mercado Pago, entregas/geolocalização e impressão física Oasis conservam seus próprios aceites e dependências. A matriz de perfis não comprova essas integrações.

Não houve deploy nem alteração do banco de produção nesta execução.

## 15. Continuação: arquivo de permissões antigas da Casa da Baba, 09/10/2026

Implementado sobre `4ffef29a`. Os cinco workflows dessa base passaram no GitHub: CI, Console, CodeQL, imagens e secret-scan. Esta fatia conclui a limpeza da lista ativa no tenant configurado, com histórico preservado. Não encerra a onda 1 nem representa deploy.

### Comportamento entregue

- A migration `20261009175941_RemoverPermissoesLegadas` é aditiva: inclui `PermissoesExplicitas` e o arquivo JSON `PermissoesLegadas` em `perfis`. Apesar do nome histórico, ela não apaga nem modifica permissões existentes.
- Na inicialização, o seed usa a empresa já resolvida por `Auth:Google:EmpresaPadrao`. Só os perfis cujo `EmpresaId` coincide com essa empresa têm as 18 permissões antigas retiradas da lista ativa. Perfis globais e de outras empresas permanecem intactos.
- Cada registro retirado fica arquivado no próprio perfil com `Id`, `PerfilId` e o nome original da permissão. Arquivo, marcador de lista explícita e retirada são gravados na mesma transação do EF. Reexecutar o seed não duplica o arquivo. Não muda usuários ou seus vínculos.
- Uma lista que fica vazia continua explícita e nega acesso; não passa a usar o nível do perfil. Login por senha, finalização do Google, JWT, refresh e seleção de atendente preservam essa decisão. O accessor HTTP do Worker respeita as claims; os jobs de sistema mantêm seu acesso anterior.
- Perfis que já usavam fallback por nível continuam usando-o. Lista mista preserva as permissões atuais. Os três perfis iniciais mantêm as capacidades da seção 14.

### Limite de compatibilidade e revisão de escopo

O catálogo operacional da Casa da Baba tem as 19 permissões previstas. O enum continua com 37 membros para ler os dados preservados das demais empresas e dos perfis globais. Remover esses membros agora quebraria essa leitura. A poda global do enum permanece pendente de uma decisão explícita sobre esses dados e seu alcance; não declarar o aceite original de enum com 19 membros como concluído.

A revisão automática recusou as propostas de limpeza global, inclusive a reversível, por ultrapassarem o escopo exclusivo da Casa da Baba. A implementação final aceita restringe a alteração de dados à empresa configurada e preserva um arquivo recuperável.

O `Down` automático é bloqueado: apagar o arquivo ou retirar o marcador sem restauração prévia poderia perder histórico ou ampliar acesso. Reversão exige restauração planejada dos registros arquivados e revisão dos perfis explícitos, ou recuperação do backup anterior. Nenhuma reversão foi executada em produção.

### Evidência

- Suite da API: 961 testes aprovados, incluindo claims, lista explícita vazia, transferências, seed e Worker.
- Domínio: 55 testes de acesso/permissão aprovados. Aplicação: 12 testes de login aprovados.
- PostgreSQL 17 real: teste de migração desde a versão anterior, seed repetido, arquivo com os IDs originais, vínculos preservados e bloqueio de downgrade. Cinco cenários: apenas legado, lista mista, fallback vazio, outra empresa e perfil global. Login, finalização Google e refresh verificados com repositórios e JWT reais; transferência filtra os perfis bloqueados. O teste não autentica no provedor Google externo.
- HTTP real: 51 verificações aprovadas no roteiro `scripts/homologacao/onda1-perfis-http.ps1`, incluindo login, módulos, portas, respostas 200/401/403 e refresh de Dona/Atendimento/Cozinha.
- Logs e TRX desta fatia: `C:\rep\EasyStok\.build\validacao-casa-da-baba-20261009\permissoes-legadas`, fora do Git.

### Próximo trecho executável

1. Onda 1: revisar a UX das ações permitidas em cada módulo, concluir sino transversal e sessão longa da Cozinha. Validar navegação, saída, falha de sessão e operação em celular/computador.
2. Onda 2: fechar o ciclo de pedido no balcão, da montagem/revisão à confirmação, preparo, entrega/retirada e cancelamento, incluindo exceções e histórico.
3. Publicação continua separada: conferir a empresa real configurada, os perfis personalizados e os vínculos das pessoas, além de backup e homologação. Não foi inferido o papel de nenhuma pessoa real.
4. Mercado Pago, transportadoras/geolocalização, impressão física Oasis e atendimento automático completo seguem com as dependências e aceites das ondas 3–5 e 9. Esta fatia não comprova essas integrações.

Não houve deploy, alteração de banco de produção, envio externo ou cobrança real nesta execução.
## 16. Continuação: sessão longa da Cozinha, 09/10/2026

Implementada a decisão D-05 da M0.2 sobre `7c3314dd`. Login por senha e finalização Google emitem a capacidade de permanência somente para o perfil Cozinha da empresa ativa única, com nível e permissões da matriz inicial. Perfil global, outra empresa, nível maior, permissões adicionais ou usuário com várias empresas não recebem essa capacidade. Nenhum perfil ou vínculo foi alterado no banco.

- A Cozinha conserva a sessão no aparelho e renova antes de consultar a API com o acesso vencido. Dona e Atendimento continuam no armazenamento da aba. Navegador sem coordenação segura entre abas ou sem armazenamento disponível usa sessão temporária.
- A renovação usa o contrato existente de refresh, recalcula permissões e nunca troca silenciosamente de empresa. A validade continua a da API: 30 dias no refresh inicial e 7 dias a cada renovação; não é acesso indefinido sem validação.
- Duas abas compartilham uma renovação. Falha de rede/429/5xx preserva a chave para retentar; credencial revogada encerra a sessão. Uma resposta antiga não encerra uma conta que entrou depois.
- Sair limpa o aparelho e solicita a revogação no servidor. Se um refresh estava em andamento, sua chave recebida depois também é revogada. Falha de comunicação no logout é informada na tela de login.
- A renovação mantém a busca, a rota e o estado de tela da mesma pessoa/empresa. A matriz é relida; troca de identidade reinicia o estado. O canal ao vivo também aguarda uma sessão válida antes de conectar.

Validação: 970 testes da API aprovados, incluindo 9 cenários de senha/Google/refresh/logout; prova JavaScript de concorrência, offline, revogação e armazenamento; qualidade do Console aprovada (lint, camadas, 264 pares de contraste e build). As 54 provas anteriores foram executadas; a de login precisou incluir o EventTarget do navegador em sua fixture e passou após o ajuste. A nova prova da sessão também está no CI.

Navegador Chromium e PostgreSQL reais: Dona/Atendimento pedem login em uma nova aba; Cozinha reabre o navegador com a sessão vencida e renova na API; duas abas geram um único refresh; a busca e o hall sobrevivem à renovação; offline conserva a chave e reconexão renova; logout invalida a chave (refresh retorna 401); celular de 390 px sem rolagem horizontal. Nenhum erro JavaScript. O roteiro HTTP de perfis manteve suas 51 verificações aprovadas. Google externo e tablet físico não foram usados nesta prova.

Evidências em `C:\rep\EasyStok\.build\validacao-casa-da-baba-20261009\sessao`. Não houve deploy. O reparo do Down da migration anterior está na PR #1505 de outra frente, ainda não integrado neste registro. Sino transversal e revisão das ações dos módulos continuam na próxima fatia da onda 1.

## 17. Continuação: sino transversal e destinatários dos avisos, 09/10/2026

O sino agora consulta badge e avisos recentes da API no hall, nos módulos e nas duas navegações do balcão. Não depende do contexto de Atendimento. A lista mostra até 10 avisos não lidos, permite atualizar e marcar individualmente como lido, mantém o aviso quando a gravação falha e distingue falha de consulta de uma lista vazia. Atualiza a cada 30 segundos enquanto a página está visível e ao voltar à página/conexão. Modal reutiliza foco, Esc e componentes do design system.

Corrigida uma falha encontrada na conexão: a API filtrava a empresa mas não o `UsuarioId` das notificações InApp. Listagem, recentes, badge, resumo e marcação em lote agora recebem o usuário do JWT; leitura/exclusão individual recusam outro destinatário. Avisos sem destinatário continuam gerais da equipe e sua leitura é compartilhada, comportamento informado na tela. Nenhum aviso real foi enviado.

Validação: 973 testes da API aprovados; teste de integração PostgreSQL com aviso geral, próprio, de colega e de outra empresa, incluindo marcação em lote; 56 provas JavaScript; lint, camadas, 264 contrastes e build do Console aprovados. Chromium com API/PostgreSQL locais confirmou ausência dos avisos da colega, respostas 404 para leitura/exclusão alheias, atualização do contador após gravação, preservação em erro 503, falha de rede sem falso vazio, retentativa, retorno de foco com Esc e uso no hall/balcão/Cozinha em 390 px. Capturas e logs em `C:\rep\EasyStok\.build\validacao-casa-da-baba-20261009\sessao`.

Os lembretes do balcão permanecem separados. A PR #1428, ainda não integrada, inclui lembretes e Web Push; seu consumo de notificações deverá ser conciliado com este sino para evitar duplicação. Esta entrega não ativa Web Push nem notificações externas. Não houve deploy.

## 18. Consolidação: reversão segura e CI, 09/10/2026

A PR #1505 corrigiu os seis testes que precisavam reverter migrations em banco sem dados novos a preservar. Incorporado o reparo e acrescentadas duas proteções: a reversão recusa um papel sujeito a RLS, que poderia enxergar uma tabela aparentemente vazia, e bloqueia alterações concorrentes na tabela antes de conferir os dados. Não desliga RLS, não concede bypass e não restaura/apaga arquivos de permissões automaticamente.

Prova em PostgreSQL 17: banco vazio desce e sobe; perfil explícito/arquivado continua bloqueando; um papel proprietário sem privilégio de bypass enxerga zero perfis pela policy, mas a reversão é recusada e o perfil permanece intacto. As classes afetadas pelo CI, o teste do arquivo legado e o teste de destinatários somaram 27 testes aprovados, sem ignorados. Build e testes de arquitetura permanecem no gate de cada commit.

Estado desta execução: sessão longa da Cozinha e sino transversal implementados/homologados localmente. A onda 1 ainda exige a revisão contínua das ações por perfil nas telas restantes e o aceite da operação real. O próximo trecho da onda 2 deve consolidar as PRs existentes de lembretes/mensagens/SLA antes de ligar as ações restantes. A especificação M3 registra decisão pendente sobre quem pode cancelar pedido já pago; não foi presumida autorização para estorno externo. Mercado Pago real, transportadora escolhida e impressão física Oasis conservam suas dependências. Nenhum deploy ou operação real foi executado.

## 19. Continuação: ações por perfil e cadastro real da Produção, 09/10/2026

A matriz da sessão agora informa `editarProducao` e `gerenciarCaixa`, além de `editarCardapio`. Exige empresa, módulo liberado e nível Gerente/Admin/SuperAdmin. A interface assume leitura quando a capacidade não vem na resposta; as políticas da API continuam protegendo cada gravação.

- **Cozinha:** consulta insumos e abre receitas completas, com rendimento, ingredientes e quantidades. Cadastro/ajuste de insumo, edição de receita e baixa automática ficam com a dona ou gerente. Falha ao abrir a receita apresenta erro e permite nova consulta.
- **Atendimento:** continua abrindo caixa e lançando entradas/saídas. Fechamento e estorno deixam de aparecer sem a capacidade; a tela explica quem pode executá-los, inclusive para caixa esquecido aberto.
- **Dona:** conserva as edições da Produção, a baixa automática, o fechamento e o estorno. Não houve ampliação dos perfis nem mudança nas políticas dos endpoints.

A prova com API/PostgreSQL encontrou dois defeitos anteriores. O cadastro de insumo gravava um produto e depois tentava anexar outra instância com a mesma chave, deixando o cadastro incompleto. Agora os atributos de insumo são gravados no primeiro cadastro; o insumo nasce ativo sem exigir preço de venda, enquanto o produto comum sem preço continua inativo. As edições de insumo, composição e baixa automática também perdiam o `xmin` na leitura sem rastreamento. Esses três caminhos usam uma leitura rastreada, limitada à empresa, e conservam a recusa de alterações concorrentes. Nenhuma migration nova foi necessária.

Validação desta fatia: **2.336 testes de aplicação**, **983 da API**, **2 de integração PostgreSQL** e **70 verificações HTTP** aprovados, sem testes ignorados nessas suítes. O teste com banco real cria, lista e ajusta o insumo pelos serviços de produção, nega a leitura de outra empresa e comprova a exceção ao tentar gravar uma versão antiga. Provas JavaScript de insumos/receitas/baixa/perfis e qualidade do Console aprovadas. A arquitetura e a compilação completa são verificadas pelo gate do commit.

Chromium com API local comprovou consulta da Cozinha em 390 px sem rolagem horizontal, erro de consulta recuperável, abertura/lançamento pelo Atendimento, ajuste de mínimo, salvamento de receita e ativação/desativação da baixa pela Dona. Também simulou resposta sem capacidades e confirmou edição indisponível. Sem erros JavaScript. Scripts, capturas e TRX ficam em `C:\rep\EasyStok\.build\validacao-casa-da-baba-20261009\sessao`, com prefixos `capacidades` e `insumos`; dados exclusivamente sintéticos.

O CI de `98e87ae2` completou os passos de build e testes com sucesso, mas o workflow foi marcado como cancelado pela atualização concorrente do master. Console, CodeQL, imagens e secret-scan dessa base passaram. A PR #1503 de planejamento da produção foi publicada por outra frente em `7a71d596` durante esta execução e incorporada sem conflito à entrega `9da944b1`. Build, push e homologação de produção continuam estados separados.

Após essa integração: **2.344 testes de aplicação**, **984 da API** e **6 de integração PostgreSQL** aprovados, sem ignorados. Os seis últimos cobrem insumos, produção e demanda agendada; a prova de planejamento passou em seus oito cenários. Qualidade e build do Console também passaram no código combinado.

Conferência local da impressão: a fila do Windows contém a HP Smart Tank e impressoras virtuais; nenhuma Oasis está instalada nessa lista. A busca por dispositivos presentes com nomes Oasis/OIA-8381/Label Printer/Thermal Printer também não encontrou correspondência. Isso não comprova ausência física; conexão, driver e teste de saída 100 × 150 mm da OIA-8381-B permanecem pendentes. Nenhuma impressão foi disparada.

Próxima sequência: completar os gestos do balcão com as PRs existentes de lembretes, mensagens programadas e SLA; revisar as ações por perfil das telas restantes; manter pagamentos reais, fornecedor de entregas, Google externo e impressão física Oasis nos aceites próprios. As correções acima não concluem a onda inteira nem comprovam essas integrações.

## 20. Continuação: mensagens programadas no balcão, 09/10/2026

Primeira fatia da onda 2 integrada em `5df6b5e1`, sobre `a447b476`. Incorpora a PR #1425, incluindo a exposição de `programada` no histórico, e resolve seus conflitos preservando as funcionalidades atuais do Console. Lembretes/WebPush (#1428) e SLA (#1429) continuam pendentes; esta entrega não encerra a onda 2.

### Comportamento entregue

- O botão **Programar** permite escolher data/hora, finalidade e texto ou modelo aprovado, consultar as mensagens da conversa e cancelar as que continuam agendadas. Datas locais são convertidas para UTC; erros de horário, janela, consentimento e destino ficam visíveis. O histórico identifica a mensagem enviada como programada.
- Cliques concorrentes compartilham a mesma chamada. Uma retentativa após perda da resposta conserva a chave de idempotência até a confirmação; a rota da API agora participa do middleware existente. O rascunho só é limpo após sucesso. Marcadores sem valor, como `{pedido}`, são recusados antes do envio.
- Falha ao atualizar a lista preserva os itens já conhecidos e oferece nova consulta. Falha no cancelamento preserva a situação anterior. Campos e fechamento ficam bloqueados durante a operação, com prazo de 15 segundos nas chamadas do Console para permitir recuperação.
- Cliente bloqueado não recebe novos agendamentos. O disparador confere novamente o bloqueio antes de enviar texto ou modelo. A consulta e o cancelamento dos agendamentos existentes continuam disponíveis.
- Corrigida uma disputa reproduzida no PostgreSQL: o cancelamento lia `Agendada` enquanto o disparador reservava a mensagem e podia sobrescrever `Enviando`. Agora a leitura para cancelar usa lock da linha dentro de transação, com filtro de empresa e checagem no caso de uso. Se o disparo já foi reservado, o cancelamento é recusado com o motivo correto. Não houve migration nem dependência nova.

### Evidência

| Verificação | Resultado |
|---|---|
| Aplicação | 2.346 testes aprovados, incluindo cliente bloqueado antes e depois do agendamento |
| API | 986 testes aprovados, incluindo rota idempotente e selo no histórico |
| PostgreSQL real | 3 testes aprovados, sem ignorados: isolamento por empresa/cancelamento, dois disparadores concorrentes e cancelamento concorrente com a reserva; a última regressão falhou antes da correção |
| Console | 58 provas JavaScript aprovadas; lint, camadas, 264 pares de contraste e build aprovados |
| Gate do commit | Build de `EasyStok.CI.slnf` e 36 testes de arquitetura aprovados |
| HTTP e Chromium | Sem login: 401; Cozinha: 403 ao listar/programar. Rascunho, duplo clique com um POST, persistência após recarregar e em outra sessão, falhas 503 recuperáveis, resposta perdida após gravação sem duplicar, consentimento, marcador e bloqueio de cliente conferidos |
| Apresentação | Tema escuro no computador e fluxo de agendar/cancelar em 390 × 844 px, com modal dentro da tela, sem rolagem horizontal e sem erros JavaScript |
| Disparo | Serviço real entregou exatamente uma mensagem ao ChatSite local de teste; histórico e caixa do visitante confirmaram o mesmo texto e o selo `programada` |

Scripts, capturas, logs e TRX estão em `C:\rep\EasyStok\.build\onda2-balcao`, fora do Git. A API usou banco isolado `easystock_onda2_balcao` e dados sintéticos. As provas de navegador estão em `programadas-browser.cjs` e `programadas-celular.cjs`, com resultados JSON; o roteiro do celular começa diretamente em 390 px e comprova a área visível da modal. O ChatSite desta validação é local: não houve envio a cliente real, homologação de modelo na Meta ou teste de entrega externa de WhatsApp/SMS/e-mail. Não foi executado deploy nesta sessão.

### Próximo trecho executável

1. Revisar e integrar a PR #1428 de lembretes/WebPush, conciliando seus avisos com o sino transversal já entregue na seção 17. Homologar destinatário, leitura, falha e retentativa antes de declarar conclusão.
2. Revisar e integrar a PR #1429 de SLA, incluindo os controles permitidos por perfil e a persistência da configuração.
3. Continuar o ciclo completo de pedido e as exceções do balcão. Homologação externa dos canais, pagamentos, entregas e impressão física conserva os aceites próprios.

## 21. Continuação: lembretes persistidos e WebPush por conta, 09/10/2026

A PR #1428 foi incorporada em `0aafbafb`, sobre `b45dc855`. Os avisos InApp continuam exclusivamente no sino transversal da seção 17; o painel do balcão consulta os lembretes manuais e conserva os automáticos calculados das conversas. A configuração de avisos no aparelho fica no sino transversal, disponível nos módulos autorizados. A Cozinha não consulta nem oferece os lembretes do balcão.

### Comportamento entregue

- **Gravação e retentativa:** programar aguarda a API antes de fechar a modal. Falha conserva texto e horário; dois cliques compartilham uma chamada. Se a resposta se perder depois da gravação, a nova tentativa conserva a chave de idempotência e o instante escolhido. A rota usa o middleware existente. Consulta iniciada antes da gravação não apaga o item novo.
- **Consulta, visto e conclusão:** a falha de consulta fica visível, conserva a lista e permite atualizar. Visto e conclusão são gravados no servidor. Falha ao concluir mantém o item; resposta atrasada não o faz reaparecer após a confirmação. Não se cria mensagem de sistema só no navegador.
- **Empresa e destinatário:** o caso de uso valida conversa, pedido e vínculo ativo do destinatário com a empresa. A lista padrão, a marcação de visto e a conclusão conferem empresa e destinatário além do filtro do repositório. O contrato existente de consulta da equipe com `todos=true` permanece. Lembretes gerais mantêm o visto compartilhado definido pela API.
- **WebPush:** a permissão é solicitada pelo clique. Sem VAPID, bloqueio do navegador e falha de inscrição têm estados próprios. Ativação só aparece após confirmação da API. A inscrição pertence à pessoa e empresa atuais; outra conta não pode reassociar nem desativar o endpoint. O envio também filtra empresa e destinatário, inclusive se o repositório devolver assinaturas de outra empresa.
- **Saída e troca de conta:** desligar ou sair invalida a assinatura no navegador e fecha os avisos já exibidos. Uma ativação cuja resposta chega depois da saída é invalidada. Login de outra conta não reaproveita a assinatura anterior. Essa invalidação é do navegador; não equivale a atualizar a linha do servidor no logout. O transporte existente desativa endpoints que respondem 404/410.
- **Celular:** a prova em 390 px reproduziu o painel saindo pela direita, com o botão de programar fora da tela. O painel agora se posiciona pela barra inteira. Ajustados os espaços e o botão Mais para conservar o acesso aos controles; tema escuro e modal também conferidos.

Esta fatia não adiciona dependência nem migration. Durante o trabalho, `47bf16a2` chegou ao master com a correção de formas de pagamento do PWA; essa atualização foi incorporada sem conflito, preservando a entrega da outra frente. A base combinada passou em 1.007 testes da API e repetiu os dois testes PostgreSQL com sucesso, sem ignorados.

### Evidência local

| Verificação | Resultado |
|---|---|
| Aplicação | 2.352 testes aprovados, sem ignorados |
| API após integrar o master | 1.007 testes aprovados, sem ignorados; eram 994 antes de incorporar a atualização de pagamentos |
| PostgreSQL real | 2 testes aprovados, sem ignorados: avaliador/idempotência e persistência de visto/conclusão com isolamento de empresa e destinatário |
| Console | 59 provas JavaScript; lint, camadas, 264 pares de contraste e build aprovados |
| Gate do código | Build de `EasyStok.CI.slnf` e 36 testes de arquitetura aprovados |
| HTTP e Chromium | 401 sem login; 403 para a Cozinha; vínculos inválidos recusados; destinatário protegido; rascunho preservado; um POST no duplo clique; resposta perdida sem duplicar; visto após recarregar; consulta/conclusão 503 com recuperação |
| Apresentação | Fluxo completo no computador e gravação em 390 × 844 px; painel, botão de programar, Mais e modal acessíveis; temas claro/escuro; nenhum erro JavaScript |
| Push | Inscrição 201 real na API/PostgreSQL, recusa de reassociação e desativação alheias, troca de conta e desligamento comprovados com PushManager controlado; VAPID ausente retorna 404 real e não mostra ativação; bloqueio nativo do Chromium respeitado |

Scripts, capturas, logs e TRX estão em `C:\rep\EasyStok\.build\onda2-lembretes`. O navegador usou a API local e o banco isolado `easystock_onda2_lembretes`, com usuários e dados sintéticos. `lembretes-browser.cjs`, `push-browser.cjs` e `celular-escuro.cjs` registram os fluxos; a prova versionada `prova-1426-notificacoes-console.mjs` cobre os contratos, as retentativas, as trocas de conta e os eventos do service worker.

**Limite:** não foi comprovada entrega externa por Google, Apple ou outro serviço de WebPush. A prova de inscrição controla PushManager e permissão; o teste do service worker usa eventos controlados. A entrega real exige VAPID e aceite no aparelho/ambiente final. Não foi executado deploy nesta sessão. Esses resultados não encerram a onda 2 nem substituem os aceites de pagamentos reais, entregas, Google externo e impressão física.

### Próximo trecho executável

1. Revisar e integrar a PR #1429 de SLA de primeira resposta por loja, incluindo persistência, permissões de edição e comportamento visual do atraso.
2. Continuar o ciclo completo do pedido e as exceções do balcão; revisar as ações restantes por perfil.
3. Homologar entrega externa dos avisos e os demais canais no ambiente próprio, com as dependências já registradas neste plano.

## 22. Continuação: SLA de resposta e pausa pelo expediente, 09/10/2026

A PR #1429 foi incorporada em `e560915c`, sobre `0be7a82a`, preservando as mensagens programadas, os lembretes e as funcionalidades atuais do Console. A revisão corrigiu divergências entre o relógio do navegador e o avaliador do servidor. Esta fatia substitui o comportamento descrito na seção 21 para os lembretes automáticos: no modo API, todos os lembretes do balcão vêm do servidor. O modo de demonstração conserva o cálculo local.

### Comportamento entregue

- **Prazo persistido:** configuração entre 1 e 240 minutos, com validação na API e no domínio. A migration `20261007100720_AddSlaRespostaConfiguracaoAtendimento` acrescenta a coluna com padrão de 5 minutos, inclusive nas configurações já existentes. O modelo atual guarda a configuração por empresa, sem um prazo separado por `LojaId`. Quando não existe configuração persistida, inbox e avaliador usam o fallback existente de `Notifications:Prazos`, cujo padrão é 10 minutos; criar/salvar a configuração aplica o valor escolhido.
- **Expediente e atraso:** servidor e Console contam somente os minutos dos turnos configurados, incluindo virada de dia, semanas sucessivas e turnos sobrepostos sem contagem duplicada. Exatamente no limite ainda não há atraso; um segundo além já ultrapassa o prazo. O Console aguarda o carregamento do expediente antes de calcular. A conversa atrasada sobe em Precisa de você e pisca; movimento reduzido mantém destaque estático.
- **Resposta efetiva:** envio pendente, envio falho e nota interna não encerram a espera. Uma resposta enviada ao cliente encerra o atraso e permite concluir o lembrete automático. Consultas e configurações validam a empresa também nos casos de uso.
- **Lembretes coerentes:** fechar manualmente a loja ou aumentar o SLA não conclui falsamente um lembrete de conversa ainda sem resposta. O envio do aviso aguarda novamente o prazo aplicável; a retomada reutiliza o mesmo lembrete. O painel não acrescenta um segundo alerta local com prazo fixo de 10 minutos.
- **Permissões:** o Atendimento pode ler o expediente para calcular o prazo. Configuração continua exigindo Admin/SuperAdmin, e controle manual de abertura continua exigindo Gerente/Admin/SuperAdmin. As capacidades `editarAtendimento` e `controlarLoja` refletem essas regras e exigem o módulo liberado. Sem capacidade, o Console não apresenta a ação; a API conserva suas políticas de gravação.
- **Gravação recuperável:** falha mantém o rascunho, duplo clique produz uma chamada e campos ficam bloqueados enquanto a gravação está em andamento. Uma consulta atrasada não sobrescreve a edição. Chamadas têm prazo de 15 segundos. Alterar apenas o SLA não regrava o modelo de mensagem; se a alteração adicional do modelo falhar, a tela distingue essa falha da configuração já salva.

**Limite do controle manual:** usa-se o estado atual. Forçar fechada conta zero; forçar aberta conta todo o intervalo. Não existe histórico de mudanças manuais para reconstruir pausas passadas, e esta entrega não cria esse histórico. Servidor e Console aplicam a mesma regra.

### Evidência local

| Verificação | Resultado |
|---|---|
| Domínio | 1.541 testes aprovados, sem ignorados |
| Aplicação | 2.368 testes aprovados, sem ignorados |
| API | 1.018 testes aprovados, sem ignorados, incluindo as capacidades de edição e controle manual |
| PostgreSQL real | 5 testes aprovados, sem ignorados: persistência e destinatários, idempotência/conclusão, SLA por empresa, expediente/resposta efetiva e execução do SQL de Up/Down/Up da migration |
| Migration | Up preenche 5 em linha existente; Down conserva os demais campos; novo Up restaura o padrão. EF não detectou alterações pendentes no modelo |
| Console | 61 provas JavaScript aprovadas, incluindo 17 verificações de SLA; lint, camadas, 264 pares de contraste e build aprovados |
| Gate do código | Build de `EasyStok.CI.slnf` e 36 testes de arquitetura aprovados |
| HTTP e Chromium | Autenticação, faixa 1..240, edição da Dona e leitura do Atendimento; falha conserva rascunho e banco; duplo clique com um PUT; persistência após recarregar |
| Fluxo real local | Conversa atrasada antes da recente; expediente fechado pausa e sua restauração retoma o destaque; resposta enviada ao ChatSite encerra o atraso na API e no cartão |
| Apresentação | Movimento reduzido, tema escuro e gravação em 390 × 844 px sem rolagem horizontal; ausência de capacidade não libera edição; nenhum erro JavaScript nos três contextos |

Scripts, capturas, logs e TRX estão em `C:\rep\EasyStok\.build\onda2-sla`, fora do Git. `sla-browser.cjs` usa API e PostgreSQL reais no banco isolado `easystock_onda2_sla`, com usuários e conversas sintéticos. Resultados estão em `browser-resultados.json`; capturas em `sla-configuracao.png`, `sla-balcao.png` e `sla-celular-escuro.png`. O ChatSite usado é local, sem envio a cliente real. Não houve deploy nem homologação de entrega externa nesta sessão.

Durante a finalização chegaram ao master `f8cec0ab` (espelho do PostgreSQL no CI), `64af5147` (regras de pedidos), `0759e10d` (correções do Console) e `a3e0a521` (sincronização offline). Essas entregas de outra frente foram incorporadas sem conflito. A validação combinada passou em **1.542 testes de domínio**, **2.380 de aplicação**, **1.024 da API** e **5 de integração PostgreSQL**, sem ignorados. As **62 provas JavaScript** e a qualidade completa do Console também passaram após a integração. Os resultados adicionais usam os sufixos `combinado` e `integracao-final` na pasta de evidências. Isso comprova compatibilidade local, sem substituir a homologação operacional do ciclo completo do pedido.

### Próximo trecho executável

1. Continuar o ciclo completo do pedido e as exceções do balcão, com as decisões pendentes da seção 9 respeitadas.
2. Revisar as ações restantes por perfil e homologar a rotina da operação real.
3. Homologar WebPush e os demais canais externos no ambiente próprio. Pagamento real, fornecedor de entregas, Google externo e impressão física Oasis mantêm seus aceites separados.

## 23. Continuação: cancelamento operacional na Ficha, 09/10/2026

**Decisão D3-04 confirmada por Felipe:** Atendimento cancela pedido sem pagamento; com qualquer valor recebido, inclusive parcial, só Dona/gerente. A regra usa o nível da sessão autenticada, incluindo Admin/SuperAdmin para a Dona e Gerente para gestão. O corpo da requisição não pode elevar esse nível.

### Comportamento entregue

- **Ficha ligada à API:** Mais ações do pedido abre a confirmação com motivo de 3 a 500 caracteres. Duplo clique compartilha uma chamada; campos bloqueiam durante a gravação. Falha conserva o motivo e não simula cancelamento em memória. Falha ao reler depois de sucesso informa que o cancelamento foi confirmado e que a tela precisa ser atualizada.
- **Permissão em todos os caminhos:** cancelamento direto, troca de status, KDS, lotes e recusa do site verificam pagamento recebido. Atendimento não vê a opção quando há pagamento; formulário aberto antes de um recebimento recebe 403 com explicação. A capacidade `cancelarPedidoPago` exige gestão e módulo Atendimento autorizado.
- **Concorrência:** cancelamento usa o mesmo lock do pedido que o recebimento. Se o pagamento confirma primeiro, Atendimento perde a autorização; se o cancelamento confirma primeiro, o recebimento manual é recusado. A expiração automática reutiliza sua transação e só publica atualização da tela depois do commit externo.
- **Efeitos persistidos:** status, motivo, autor, horário, evento de integração, liberação da vaga e devolução do estoque já baixado ficam na transação. Cobrança pendente é cancelada. Repetir a operação não duplica auditoria, evento, vaga ou devolução de estoque. O SSE publica `pedido.mudou_status` depois do commit; o horário informado pelo lote offline é preservado na auditoria.
- **Dinheiro recebido:** cancelamento operacional não remove `PedidoPagamento` nem solicita devolução ao provedor. Corrigidas as consultas de total e lista do Caixa: pedido cancelado continua contribuindo com dinheiro recebido e ainda não devolvido. Cobrança com estorno confirmado exclui somente seu pagamento correspondente; pedido já consolidado em Venda continua sem contagem duplicada. O roteiro HTTP da onda 0 foi ajustado para esse comportamento.
- **Comunicação honesta:** a confirmação informa que a devolução deve ser tratada separadamente e orienta avisar o cliente pela conversa. O fluxo existente de recusa do site conserva seu estorno, agora com a mesma proteção por perfil.

Durante a finalização, as melhorias de telas em `ed2c95ba` e a embalagem pela receita em `5f152c11` chegaram ao master e foram incorporadas sem conflito. As contagens abaixo refletem a base combinada.

### Evidência local

| Verificação | Resultado |
|---|---|
| Aplicação | 2.396 testes aprovados, sem ignorados |
| API | 1.049 testes aprovados, sem ignorados |
| PostgreSQL real | 15 testes aprovados, sem ignorados: ciclo pedido/estoque/pagamento/caixa, vaga, idempotência, corrida pagamento/cancelamento nas duas ordens, soma/lista do Caixa e aprovação/recusa concorrentes |
| Console | 64 provas JavaScript aprovadas; lint, camadas, contraste e build aprovados |
| HTTP e Chromium | 403 para pagamento parcial, nível/empresa forjados, status e KDS; lotes rejeitam a linha; motivo obrigatório, falha 503 preservando texto, um POST no duplo clique e persistência após recarregar |
| Fluxo ao vivo | Stream SSE real recebeu o cancelamento; Atendimento com tela anterior ao pagamento recebeu 403; Dona cancelou pedido parcial conservando o recebimento e uma auditoria |
| Apresentação | Computador e celular 390 × 844 px, tema escuro, confirmação acessível sem rolagem horizontal; nenhum erro JavaScript |

Scripts, capturas, logs e TRX estão em `C:\rep\EasyStok\.build\onda2-ciclo-pedido`. A prova `cancelamento-browser.cjs` usa API local e PostgreSQL real no banco isolado `easystock_onda2_ciclo`. Usuários, conversas e pedidos foram criados pelas APIs; a preparação da fixture habilita os módulos e vincula os pedidos às conversas no banco sintético. As ações testadas de cancelar e pagar passam pelas APIs reais, sem alteração manual de resultado.

**Limites:** não houve envio a cliente real, devolução de dinheiro real, impressão física ou deploy. O estorno da Ficha e a homologação externa de pagamento continuam pendentes. Esta entrega fecha o cancelamento operacional desta fatia, sem encerrar M3.3 ou a onda 2.

### Próximo trecho executável

1. Continuar o ciclo completo e as exceções do pedido, incluindo reagendamento com troca efetiva da vaga (M3.4).
2. Implementar e homologar o estorno pela Ficha com resposta do provedor e reflexo financeiro explícito, respeitando D3-04.
3. Conferir as ações restantes por perfil e realizar o aceite da operação real. Canais externos, entregas e impressão física conservam seus aceites separados.


## 24. Continuação: reagendamento com troca de vaga (09/10/2026)

**Fatia executada:** M3.4 da Onda 2, reagendamento de pedido criado pela Ficha e por Entregas, com ocupação real da janela e validação local em desktop e celular.

### 24.1 Implementado

- `GET api/pedidos/{id}/janelas` consulta a vaga atual e as opções pelo prazo real dos itens, incluindo preparo padrão e respiro. `PATCH api/pedidos/{id}/janela` usa empresa e autor da sessão e exige o módulo Atendimento ou Entregas, além do nível Operador.
- A troca trava o pedido e, na mesma transação, libera a vaga antiga com motivo `reagendado`, ocupa a nova com a regra de capacidade da S16, atualiza `AgendadoParaEm`, recalcula o início previsto e limpa o aviso de atraso. Janela cheia retorna 409; rollback preserva a reserva anterior.
- A auditoria registra faixa, autor e situação do aviso. Repetir a mesma janela/data não duplica vaga, evento ou aviso. Depois do commit, `pedido.reagendado` atualiza os consumidores SSE, incluindo KDS e Entregas.
- A consulta de vagas prioriza a ativa sobre o histórico. A Ficha passa a receber a janela do servidor e o reducer não restaura a escolha local antiga. A rota antiga `/agendamento` recusa pedidos com vaga para impedir a divergência entre horário e ocupação.
- Ficha e Entregas compartilham a escolha de data/janela e a operação da API. Uma falha conserva a escolha, duplo clique envia uma chamada e recarga indisponível após confirmação não é apresentada como falha da gravação. Pedidos entregues ou cancelados não podem ser reagendados.
- Aviso por WhatsApp é opcional e fica desmarcado inicialmente. Quando solicitado e permitido pelas preferências e pelo telefone do cliente, o evento `PedidoReagendado` entra no outbox de notificações na mesma transação, com faixa/data e chave distinta por troca. O remetente é a Loja, declarado no catálogo e coberto por regressão no domínio. A tela diferencia aviso na fila de aviso não enfileirado.

### 24.2 Validação executada

| Camada | Evidência local |
|---|---|
| Domain | 1.543 testes aprovados, incluindo classificação do remetente do reagendamento como Loja |
| Application | 2.397 testes aprovados, incluindo mensagem de reagendamento com remetente Loja no outbox |
| API | 1.050 testes aprovados, incluindo classificação de módulo do novo controller |
| PostgreSQL | 25 testes aprovados, nenhum ignorado: 15 da troca e 10 de capacidade/KDS/jornada do atendimento |
| Arquitetura | 37 testes aprovados no gate de commit, junto ao build da solução de CI |
| Concorrência | Duas transações reais aguardaram o mesmo lock para a última vaga; uma confirmou e a outra restaurou a vaga antiga |
| Atomicidade | Falha controlada ao gravar o aviso, depois do INSERT da nova vaga, reverteu a troca inteira e não publicou SSE |
| Console | 65 scripts de prova aprovados; lint, camadas, contraste e build aprovados |
| HTTP e navegador | Atendimento autorizado; Cozinha e empresa forjada 403; janela inválida 400; pedido inexistente 404; janela que lotou depois de abrir a Ficha recusada sem perder a reserva |
| Jornada local | Chromium 1440 × 1000 e 390 × 844, tema claro/escuro: Ficha, Entregas, falha 503, duplo clique, recarga persistida, pagamento parcial conservado, KDS e SSE |

Evidências fora do Git: `C:\rep\EasyStok\.build\onda2-reagendamento`, com TRX, logs, script de navegador, resultados e capturas. Banco de homologação isolado: `easystock_onda2_janela`, no PostgreSQL local. Sem migration ou dependência nova.

### 24.3 Limites e próxima fatia

- Gravação e fila de aviso homologadas localmente; não houve envio a cliente real. Entrega externa pelo WhatsApp depende do canal configurado. Fora da janela de 24 horas, é necessário configurar um modelo aprovado pela Meta; o seed não presume essa aprovação.
- Não houve publicação pública verificada nesta fatia. Commit/push e deploy são estados distintos.
- Próxima fatia: estorno pela Ficha, com resposta efetiva do provedor, reflexo financeiro e regra D3-04. Depois, continuar as ações restantes por perfil e os aceites operacionais, mantendo separados canais externos, logística e impressão física.
