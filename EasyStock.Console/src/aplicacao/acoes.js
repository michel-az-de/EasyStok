// Nomes das ações. Constante em vez de literal solta evita erro silencioso.
export const SELECIONAR_CONVERSA = 'selecionar-conversa'
export const MUDAR_FILTRO = 'mudar-filtro'
export const DEFINIR_RASCUNHO = 'definir-rascunho'
export const ENVIAR_MENSAGEM = 'enviar-mensagem'
export const CONFIRMAR_ENTREGA = 'confirmar-entrega'
export const REABRIR_CONVERSA = 'reabrir-conversa'
export const AVANCAR_ESTEIRA = 'avancar-esteira'
export const TROCAR_JANELA = 'trocar-janela'
export const CADASTRAR_ENDERECO = 'cadastrar-endereco'
export const ADICIONAR_ITEM = 'adicionar-item'
export const REMOVER_ITEM = 'remover-item'
export const GERAR_PEDIDO = 'gerar-pedido'
export const SALVAR_NOTA = 'salvar-nota'
export const FECHAR_ALERTA = 'fechar-alerta'
export const ALTERNAR_REGRA = 'alternar-regra'
export const TROCAR_MODO_AGENTE = 'trocar-modo-agente'
export const AGENTE_PEDINDO = 'agente-pedindo'
export const AGENTE_RESPONDEU = 'agente-respondeu'
export const AGENTE_LIMPOU = 'agente-limpou'
export const AGENTE_FALHOU = 'agente-falhou'
export const ENVIAR_MIDIA = 'enviar-midia'
export const DESFAZER_ESTEIRA = 'desfazer-esteira'
export const CORRIGIR_PASSO = 'corrigir-passo'
export const AJUSTAR_QUANTIDADE = 'ajustar-quantidade'
export const AJUSTAR_OBSERVACAO = 'ajustar-observacao'

// Rodada 2 · cobrança por Pix. Acrescentado no fim de propósito: o merge com a
// outra árvore fica limpo quando ninguém reordena a lista de cima.
export const GERAR_COBRANCA = 'gerar-cobranca'
export const REENVIAR_COBRANCA = 'reenviar-cobranca'
export const CONFIRMAR_PAGAMENTO = 'confirmar-pagamento'
export const MARCAR_COMPROVANTE = 'marcar-comprovante'
export const ACEITAR_DIVERGENCIA = 'aceitar-divergencia'

// Rodada 2 · cardápio do dia.
export const ALTERNAR_DISPONIBILIDADE = 'alternar-disponibilidade'
export const AJUSTAR_SALDO = 'ajustar-saldo'

// Rodada 2 · janelas de entrega com capacidade.
export const ESCOLHER_JANELA = 'escolher-janela'
export const FORCAR_ENCAIXE = 'forcar-encaixe'
// Atendimento automático: assumir e devolver, e o som da cozinha (RN-04, D4, D6).
export const PASSAR_PARA_DONA = 'passar-para-dona'
export const ASSUMIR_ATENDIMENTO = 'assumir-atendimento'
export const DEVOLVER_AUTOMATICO = 'devolver-automatico'
export const ALTERNAR_SOM = 'alternar-som'

// Mensagens automáticas: texto e espera editáveis por regra.
export const EDITAR_REGRA = 'editar-regra'

// Lembretes: tarefa da dona.
export const CRIAR_LEMBRETE = 'criar-lembrete'
export const CONCLUIR_LEMBRETE = 'concluir-lembrete'
export const SINCRONIZAR_LEMBRETES = 'sincronizar-lembretes'

// Bloqueio de cliente por cadastro.
export const BLOQUEAR_CLIENTE = 'bloquear-cliente'
export const DESBLOQUEAR_CLIENTE = 'desbloquear-cliente'

// Rodada 3 · passo zero da direção visual (seção 10): ações novas que as três
// frentes (cobrança/esteira, lembretes, comanda) vão consumir.
export const CANCELAR_PEDIDO = 'cancelar-pedido'
export const MARCAR_ESTORNO = 'marcar-estorno'
export const ENCERRAR_ATENDIMENTO = 'encerrar-atendimento'
export const MARCAR_LEMBRETES_VISTOS = 'marcar-lembretes-vistos'

// ---------------------------------------------------------------------------
// Rodada 5 · passo zero da direção visual (seção 8): constantes de TODAS as
// ações novas das seções 2 a 7, de uma vez, para nenhuma frente precisar
// editar este arquivo depois (só `aplicacao/casos/<tema>.js` e
// `aplicacao/acoes/<tema>.js`, que são dela). Abrir/fechar o encerramento é a
// exceção: o passo zero já implementa o caso em `reducer.js`, porque o
// cabeçalho e o menu do pedido (rodada 4) precisam chamar "Encerrar" antes
// da F5 existir.
// ---------------------------------------------------------------------------

// Passo zero (ui.encerrando, de qualquer lugar da tela).
export const ABRIR_ENCERRAMENTO = 'abrir-encerramento'
export const FECHAR_ENCERRAMENTO = 'fechar-encerramento'

// Frente 2 · Ficha do lead e do cliente.
export const SALVAR_CADASTRO_RAPIDO = 'salvar-cadastro-rapido'
export const EDITAR_DADO_CLIENTE = 'editar-dado-cliente'
export const MUDAR_ENDERECO_DO_PEDIDO = 'mudar-endereco-do-pedido'
export const ADICIONAR_TAG = 'adicionar-tag'
export const REMOVER_TAG = 'remover-tag'
export const EDITAR_TAG = 'editar-tag'

// Frente 3 · Comanda e cardápio.
export const ADICIONAR_ACRESCIMO = 'adicionar-acrescimo'
export const TIRAR_ACRESCIMO = 'tirar-acrescimo'
export const TIRAR_ITEM_PAGO = 'tirar-item-pago'
export const SEPARAR_ITEM_ANOTADO = 'separar-item-anotado'
export const MARCAR_CANHOTO_IMPRESSO = 'marcar-canhoto-impresso'

// Frente 4 · Cobrança.
export const REFAZER_COBRANCA = 'refazer-cobranca'
export const COBRAR_COMPLEMENTO = 'cobrar-complemento'
export const MARCAR_RECEBIDO_ENTREGA = 'marcar-recebido-entrega'
export const DESPACHAR_MESMO_ASSIM = 'despachar-mesmo-assim'
// Integração da onda seguinte, ponta (a): o meio escolhido nos chips vale
// para "Enviar comanda" e para o atalho da barra, não só para o bloco.
export const ESCOLHER_MEIO_PAGAMENTO = 'escolher-meio-pagamento'
// Rodada 12 (issue #13): trocar a forma depois que a cobrança saiu (cancela a
// pendente e emite outra) e desfazer a baixa marcada por engano.
export const ALTERAR_MEIO_PAGAMENTO = 'alterar-meio-pagamento'
export const DESFAZER_PAGAMENTO = 'desfazer-pagamento'

// Frente 5 · Encerrar atendimento com resumo.
export const ENCERRAR_COM_RESUMO = 'encerrar-com-resumo'
export const SALVAR_AVALIACAO_CLIENTE = 'salvar-avaliacao-cliente'
export const SALVAR_AUTOAVALIACAO = 'salvar-autoavaliacao'
export const SALVAR_ANOTACAO_FECHAMENTO = 'salvar-anotacao-fechamento'
// "Encerrar" v2 (decisão 46): encerramento automático por janela de 24h sem
// mensagem do cliente. Despachada direto pelo Provider (mesmo padrão de
// SINCRONIZAR_LEMBRETES), sem criador em acoes/encerramento.js: não é ação de
// clique, é o relógio da tela reconferindo sozinho.
export const ENCERRAR_POR_JANELA = 'encerrar-por-janela'

// Frente 6 · Entregas de hoje.
export const CRIAR_VIAGEM = 'criar-viagem'
export const DESFAZER_VIAGEM = 'desfazer-viagem'
export const TROCAR_MODO_VIAGEM = 'trocar-modo-viagem'
export const POR_NA_VIAGEM = 'por-na-viagem'
export const TIRAR_DA_VIAGEM = 'tirar-da-viagem'
export const REORDENAR_PARADA = 'reordenar-parada'
// Gerar rota aplica a ordem sugerida; o desfazer manda a ordem anterior pela mesma ação.
export const APLICAR_ORDEM_VIAGEM = 'aplicar-ordem-viagem'
export const ALTERAR_AGENDAMENTO_ENTREGA = 'alterar-agendamento-entrega'
export const CHAMAR_ENTREGADOR = 'chamar-entregador'
export const CANCELAR_CHAMADO = 'cancelar-chamado'
export const ATUALIZAR_CHAMADO = 'atualizar-chamado'
export const SAIR_PARA_ENTREGA = 'sair-para-entrega'
export const MARCAR_PARADA_ENTREGUE = 'marcar-parada-entregue'

// Frente 7 · Menu de simulações (nomes já dados pela seção 7 da direção).
export const SIMULAR_CONVERSA = 'simular-conversa'
export const SIMULAR_MENSAGEM = 'simular-mensagem'
export const SIMULAR_PAGAMENTO = 'simular-pagamento'
export const DESLOCAR_RELOGIO = 'deslocar-relogio'
export const PAUSAR_SIMULACOES = 'pausar-simulacoes'
export const LIMPAR_SIMULACOES = 'limpar-simulacoes'

// Área de entrega (rodada 5, RN-09/RN-10/RN-11, UC-02): as três ações de um
// toque da dona na ficha, quando o CEP capturado está fora ou no limite.
export const DECIDIR_AREA_ENTREGA = 'decidir-area-entrega'

// Frente Reclamação · ocorrência de pedido entregue (RN-34 a RN-38, UC-06).
// ABRIR_OCORRENCIA nasce sozinha, de dentro de `consultarAgente`, nunca de um
// clique (US-049): "não é botão, é entrar e ver o que aconteceu".
export const ABRIR_OCORRENCIA = 'abrir-ocorrencia'
export const APURAR_OCORRENCIA = 'apurar-ocorrencia'
export const ENCERRAR_OCORRENCIA_SEM_ESTORNO = 'encerrar-ocorrencia-sem-estorno'

// Rodada 5 · cardápio editável (US-020, RN-15). Tag de cliente já tinha ação
// própria (ADICIONAR_TAG/REMOVER_TAG/EDITAR_TAG, acima); o que faltava de
// US-016 (achar cliente pela tag) é busca, sem ação nova.
export const INCLUIR_ITEM_CARDAPIO = 'incluir-item-cardapio'
export const EDITAR_ITEM_CARDAPIO = 'editar-item-cardapio'
export const ALTERNAR_REMOCAO_ITEM_CARDAPIO = 'alternar-remocao-item-cardapio'
export const CONFIRMAR_VALIDACAO_ITEM = 'confirmar-validacao-item'

// Frente Horário e loja (rodada 5, pedido do dono 24/09/2026): controle do
// topo (Aberta/Fechada) e horário de funcionamento configurável por dia.
export const ALTERNAR_LOJA = 'alternar-loja'
// #1443: gesto com conferência ao abrir (abre o caixa) ou fechar no horário (justificativa).
export const PEDIR_GESTO_LOJA = 'pedir-gesto-loja'
export const FECHAR_GESTO_LOJA = 'fechar-gesto-loja'
export const EDITAR_FUNCIONAMENTO = 'editar-funcionamento'

// Frente Cardápio por link (rodada 7, US-021): o pedido fechado na janela do
// site (`#/cardapio-link/<id>`) chega na conversa de origem pelo mesmo canal
// que a janela de Entregas usa. O pagamento em si reaproveita
// CONFIRMAR_PAGAMENTO, que já existe.
export const CRIAR_PEDIDO_CARDAPIO_LINK = 'criar-pedido-cardapio-link'
// Frente Anexos (rodada 7, pedido do dono 24/09/2026 04h12): cadastro de fotos
// (peça = foto pronta para enviar, com nome e descrição) e o CRUD dela.
// Anexar arquivo e enviar áudio continuam saindo por ENVIAR_MIDIA (acima):
// mesma ação, formato novo, ver o registro da frente sobre "Prato vira parte
// da galeria" em auditoria/decisoes/61-anexos.md.
export const INCLUIR_PECA = 'incluir-peca'
export const EDITAR_PECA = 'editar-peca'
export const TIRAR_PECA = 'tirar-peca'
// Frente Respostas (rodada 7, pedido do dono 24/09/2026): biblioteca de
// respostas prontas, cadastrada e arquivada pela própria tela.
export const INCLUIR_RESPOSTA_PRONTA = 'incluir-resposta-pronta'
export const EDITAR_RESPOSTA_PRONTA = 'editar-resposta-pronta'
export const ALTERNAR_ARQUIVAMENTO_RESPOSTA_PRONTA = 'alternar-arquivamento-resposta-pronta'

// Frente Lote de papel (rodada 8, US-042, D6, UC-04 E1): "Internet cai" no
// Simular, e o lançamento dos status marcados no papel quando a conexão volta.
export const CONEXAO_CAIU = 'conexao-caiu'
export const CONEXAO_VOLTOU = 'conexao-voltou'
export const LANCAR_LOTE_PAPEL = 'lancar-lote-papel'
// #1241: no modo API o EasyStok aplicou o lote; a tela só fecha a pendência, sem mensagem.
export const LOTE_PAPEL_LANCADO_API = 'lote-papel-lancado-api'
// #1241: alertas de produto vendido sem saldo lidos do EasyStok (S22).
export const ALERTAS_DE_ESTOQUE_DA_API = 'alertas-de-estoque-da-api'

// Frente Simulação (rodada 10, pedido do dono 25/09/2026 23h42): o cliente
// simulado reage às ações da Thatiane, não só manda falas por tempo fixo.
// Uma ação só, genérica (mensagem "in" ou "out" do próprio cliente/casa,
// formato às vezes especial para a avaliação de um toque), despachada por
// `aplicacao/useReacaoClienteSimulado.js` e resolvida em
// `aplicacao/casos/simulacao.js` (dona: frente 79).
export const SIMULAR_EVENTO_CLIENTE = 'simular-evento-cliente'

// Rodada 13 · Produção e cardápio (issue #43, UC-08, UC-09, RN-44 a RN-51):
// registrar o dia de produção em lote e ajustar a contagem física com
// motivo. Ver `dominio/producao.js` e `casos/producao.js`.
export const PRODUCAO_REGISTRAR_LOTE = 'producao-registrar-lote'
export const PRODUCAO_AJUSTAR_CONTAGEM = 'producao-ajustar-contagem'

// Rodada 11 · atendimento automático visível (issue #8, registro 92). A
// resposta automática não cai pronta: fica "digitando" em
// `conversa.respostaPendente` e sai uma de cada vez por esta ação, despachada
// por `aplicacao/useDigitacaoAutomatica.js`. Com o automático parado
// (Assumir, ela escrevendo), a fila some em vez de sair.
export const ENTREGAR_RESPOSTA_AUTOMATICA = 'entregar-resposta-automatica'

// Rodada 13 · configuração das janelas de entrega pela Thati (issue #42,
// registro 104). Casos em `aplicacao/casos/janelas.js`, ações em
// `aplicacao/acoes/janelas.js`. Nunca confundir com `ESCOLHER_JANELA`/
// `TROCAR_JANELA`/`FORCAR_ENCAIXE` (rodada 2, acima): aquelas marcam a janela
// NO PEDIDO; estas mexem no CATÁLOGO de janelas em si.
export const CRIAR_JANELA = 'criar-janela'
export const EDITAR_JANELA = 'editar-janela'
export const PAUSAR_JANELA = 'pausar-janela'
export const REATIVAR_JANELA = 'reativar-janela'
export const EXCLUIR_JANELA = 'excluir-janela'
export const AJUSTAR_RESPIRO_MINIMO = 'ajustar-respiro-minimo'
// Frente Caixa (rodada 13, issue #44): abrir/fechar o dia no modelo do
// EasyStok (MovimentoCaixa), lançar entrada/saída com categoria, estornar
// lançamento e venda avulsa (UC-11).
export const ABRIR_CAIXA = 'abrir-caixa'
export const LANCAR_MOVIMENTO_CAIXA = 'lancar-movimento-caixa'
export const ESTORNAR_MOVIMENTO_CAIXA = 'estornar-movimento-caixa'
export const FECHAR_CAIXA = 'fechar-caixa'
export const LANCAR_VENDA_AVULSA = 'lancar-venda-avulsa'
// Frente Entregas e integrações (rodada 13, issue #46, registro 108): aba de
// Gestão (ligar/desligar provedor, credencial, padrão) e a corrida chamada
// por um provedor integrado (Lalamove, 99 Entregas), que anda por cima do
// mesmo `pedido.viagem.chamado` da R12 (`acoes/entregas.js`, issue #17).
export const ALTERNAR_PROVEDOR_LOGISTICA = 'alternar-provedor-logistica'
export const SALVAR_CREDENCIAL_PROVEDOR = 'salvar-credencial-provedor'
export const DEFINIR_PROVEDOR_PADRAO = 'definir-provedor-padrao'
export const CHAMAR_ENTREGADOR_PROVEDOR = 'chamar-entregador-provedor'
export const AVANCAR_CORRIDA = 'avancar-corrida'
// Frente Fidelidade e cupons (rodada 13, issue #45, registro 107). Cupom
// aplicado no pedido (ficha/Cobrança) e configuração da Gestão (cupons,
// regra de pontos, catálogo de recompensas, sorteios). Ver `dominio/fidelidade.js`.
export const CRIAR_CUPOM = 'criar-cupom'
export const EDITAR_CUPOM = 'editar-cupom'
export const ALTERNAR_CUPOM_ATIVO = 'alternar-cupom-ativo'
export const APLICAR_CUPOM = 'aplicar-cupom'
export const REMOVER_CUPOM_DO_PEDIDO = 'remover-cupom-do-pedido'
export const EDITAR_REGRA_FIDELIDADE = 'editar-regra-fidelidade'
export const CRIAR_RECOMPENSA = 'criar-recompensa'
export const EDITAR_RECOMPENSA = 'editar-recompensa'
export const ALTERNAR_RECOMPENSA_ATIVA = 'alternar-recompensa-ativa'
export const CRIAR_SORTEIO = 'criar-sorteio'
export const RESGATAR_RECOMPENSA = 'resgatar-recompensa'

// Modo API (ADR-0054, F01): a lista vem do EasyStok por polling e o envio da dona
// é confirmado ou recusado pela API depois do balão otimista.
export const SINCRONIZAR_CONVERSAS = 'sincronizar-conversas'
export const SINCRONIZACAO_FALHOU = 'sincronizacao-falhou'
export const CONFIRMAR_ENVIO_API = 'confirmar-envio-api'
export const FALHAR_ENVIO_API = 'falhar-envio-api'
export const AVISO_API = 'aviso-api'
// F02: horário, controle manual e mensagens do expediente (S40) como a API devolveu.
export const SINCRONIZAR_EXPEDIENTE = 'sincronizar-expediente'
// F03: cardápio da vitrine da empresa e o pedido da conversa (S10/S11) como a API devolveu.
export const SINCRONIZAR_CARDAPIO = 'sincronizar-cardapio'
export const SINCRONIZAR_PEDIDO = 'sincronizar-pedido'
// F06 (#1236): o aviso de ação fica na faixa até a dona fechar; a sincronização não o apaga.
export const FECHAR_AVISO_API = 'fechar-aviso-api'
// #1276: o cliente da conversa como o EasyStok tem (cadastro pelo console ou dossiê ao abrir).
export const CLIENTE_DA_API = 'cliente-da-api'
// #1276: nome que a dona escreveu para o lead ainda sem cadastro; vai junto no cadastro.
export const RENOMEAR_LEAD_API = 'renomear-lead-api'
// #1441: respostas prontas e mensagens automáticas como o EasyStok devolveu (S42).
export const RESPOSTAS_DA_API = 'respostas-da-api'
export const REGRAS_DA_API = 'regras-da-api'
// #1474: a leitura das automáticas falhou (a tela separa carregando, erro e vazio).
export const REGRAS_FALHARAM = 'regras-falharam'
