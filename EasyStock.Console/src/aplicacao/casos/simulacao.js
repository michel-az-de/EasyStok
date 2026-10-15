// Casos de reducer da Frente 7 · Menu de simulações (rodada 5, seção 7).
// Arquivo da F7: nenhuma outra frente edita este arquivo, e a F7 nunca edita
// `reducer.js` (ele já importa e espalha `casosSimulacao` no objeto
// composto).
//
// O CORAÇÃO DO PROBLEMA (achado da auditoria, decisão 22, item 1): o
// protótipo não tinha o evento "mensagem de cliente chega". `SIMULAR_MENSAGEM`
// É esse evento — o nome já estava reservado pelo passo zero (seção 7, "Ações
// novas"). Ele decide, via `dominio/simulacoes.js`, o que a casa faz sozinha:
// boas-vindas no primeiro contato, saudação pelo nome para quem já é
// cliente, resposta do automático (regra por gatilho), a trava de restrição e
// saldo que `dominio/agente.js` já tinha, e a conciliação quando a mensagem é
// comprovante de Pix.
//
// `PAUSAR_SIMULACOES` fica de fora deste arquivo de propósito: pausar só
// precisa impedir o PRÓXIMO `setTimeout` da própria `useRoteiro.js`, e essa é
// a única leitora. Colocar o estado no reducer pediria expor um campo novo em
// `AtendimentoProvider.jsx` (não é arquivo da F7, nenhuma frente edita o
// Provider fora do que o passo zero já deixou pronto) só para o mesmo hook
// que despachou reler o que ele mesmo sabe. A pausa vive como estado local do
// hook (sem âncora na seção 7, decisão registrada em
// auditoria/decisoes/24-f7-simulacoes.md).
import * as acao from '../acoes'
import { respostaParaMensagemDeCliente } from '../../dominio/simulacoes'
import { GATILHOS, contextoDePrevia, regraDoGatilho, textoDaRegra } from '../../dominio/automacao'
import { aplicarPagamento, liberaEsteira } from '../../dominio/cobranca'
import { indiceDoPasso } from '../../dominio/esteira'
import { janelaPorId } from '../../dominio/entrega'
import { PREFIXOS_CEP_ATENDIDOS } from '../../infra/catalogo'
import { gerarAudioSimulado } from '../../dominio/anexos'
import { ehLead, jaComprou } from '../../dominio/conversa'
import { mesPorExtenso } from '../../dominio/formato'
import { MARCA_DE_CONVERSAO } from '../../dominio/resumoAtendimento'
// Rodada 10 · cliente simulado que reage (registro 79). `comItem`/`novoPedido`
// e `baixarSaldo` são os MESMOS que ADICIONAR_ITEM usa em `reducer.js`: aqui
// entram por import, nunca por editar o reducer (mesmo motivo, já anotado
// acima, de `comMensagemLocal` duplicar em vez de expor).
import { comItem, novoPedido } from '../../dominio/pedido'
import { baixarSaldo } from '../../dominio/cardapio'
import { abrirOcorrencia, ocorrenciaAberta } from '../../dominio/ocorrencia'
// Rodada 13 (issue #41): as duas mudaram de morada para dominio/mensagem.js,
// para dominio/automatico.js (Precisa de você) poder reaproveitar o mesmo
// heurístico sem criar ciclo de import com clienteSimulado.js.
import { ehPerguntaSemResposta, MOTIVO_PERGUNTA_FORA_DO_ROTEIRO } from '../../dominio/mensagem'
// Rodada 11 · atendimento automático visível (issue #8, registro 92): o
// automático capta nome, telefone e endereço da conversa, cria o cadastro e
// anota o pedido. A decisão (o que a fala traz, o que responder) é domínio
// puro em `dominio/captura.js`; aqui só se aplica no estado.
import {
  TEXTO_CADASTRO_CRIADO, cadastroCompleto, extrairDados, perguntaPara, proximoCampo,
  respostaDaCaptura, tagDeGosto,
} from '../../dominio/captura'
import { SITUACOES, situacaoDoCep } from '../../dominio/areaEntrega'

const mapear = (estado, id, transformar) => ({
  ...estado,
  conversas: estado.conversas.map((c) => (c.id === id ? transformar(c) : c)),
})

// Mesma forma de `comMensagem` do `reducer.js` (não exportado de lá: nenhuma
// frente importa de dentro do reducer, só ele espalha os `casos`). Duplicar
// esta função pequena é o preço de manter "frente nenhuma edita reducer.js".
function comMensagemLocal(conversa, mensagem, { id, agora }) {
  const paraOCliente = mensagem.dir !== 'sistema'
  return {
    ...conversa,
    atrasada: paraOCliente ? false : conversa.atrasada,
    ultimaEm: paraOCliente ? new Date(agora).toISOString() : conversa.ultimaEm,
    mensagens: [...conversa.mensagens, { id, em: new Date(agora).toISOString(), ...mensagem }],
  }
}

// "Telefone cadastrado" (US-002, critério "dado que o telefone do remetente
// existe em um cadastro de cliente"): a PRÓPRIA conversa já chega com
// histórico (cenário de simulação que semeia um cliente pronto, ex.
// roteirosSimulacao.js — achado P0.2, banca10/simulacao: `cadastroConhecido`
// chegava falso para Eduardo Lima e Tiago Nunes, cada um com uma conversa só,
// mesmo com pedidos>0 e `desde` preenchido) OU outra conversa com o MESMO
// cadastro OU o MESMO telefone que já tenha alguma mensagem ou algum pedido.
// `mesmoCadastro`/mais de uma conversa por cadastro é regra do passo zero
// (seção 2); `telefone` fecha o caso do achado 1 (banca3/e1-e3): um lead do
// WhatsApp que acabou de ganhar telefone (CADASTRAR_ENDERECO/
// SALVAR_CADASTRO_RAPIDO) precisa ser reconhecido na conversa seguinte, e ela
// nasce sem `cadastroId` compartilhado nenhum — só o número em comum.
function cadastroJaConhecido(conversas, conversaAtual) {
  if (jaComprou(conversaAtual.cliente ?? {})) return true
  const { cadastroId } = conversaAtual
  const telefone = conversaAtual.cliente?.telefone || conversaAtual.cliente?.telefoneCanal || null
  return conversas.some((c) => {
    if (c.id === conversaAtual.id) return false
    const jaFalou = c.mensagens.length > 0 || (c.cliente?.pedidos ?? 0) > 0
    if (!jaFalou) return false
    if (cadastroId && c.cadastroId === cadastroId) return true
    return Boolean(telefone) && c.cliente?.telefone === telefone
  })
}

// Replica `comPagamentoReconhecido` do `reducer.js` (mesmo motivo de
// `comMensagemLocal`: função interna, não exportada, e a F7 não edita o
// arquivo para expor ela). Usada por SIMULAR_PAGAMENTO e pela conciliação por
// comprovante dentro de SIMULAR_MENSAGEM.
function comPagamentoReconhecidoLocal(estado, id, cobranca, resposta, { agora, mensagemId }) {
  const conversa = estado.conversas.find((c) => c.id === id)
  const janela = janelaPorId(estado.catalogo.janelas, conversa.pedido.janela)
  const libera = liberaEsteira(cobranca)
  const paraPago = (pedido) =>
    (libera && indiceDoPasso(pedido.estado) < indiceDoPasso('pago') ? 'pago' : pedido.estado)
  const contexto = contextoDePrevia(conversa, janela?.faixa)
  // RN-03/UC-01 passo 10 (rodada 10, item P1.4): mesma conta de
  // `comPagamentoReconhecido` (reducer.js) — a primeira compra paga promove
  // lead a cliente, nunca a confirmação de endereço.
  const converte = libera && ehLead(conversa)
  return mapear(estado, id, (c) => {
    let comConversao = c
    if (converte) {
      comConversao = comMensagemLocal({
        ...c, conta: 'cliente', cliente: { ...c.cliente, desde: mesPorExtenso(agora) },
      }, { dir: 'sistema', texto: MARCA_DE_CONVERSAO }, { id: `${mensagemId}-conversao`, agora })
    }
    const comCobranca = { ...comConversao, pedido: { ...comConversao.pedido, cobranca, estado: paraPago(comConversao.pedido) } }
    if (!libera || !resposta) return comCobranca
    return comMensagemLocal(comCobranca, {
      dir: 'out', status: 'lida', automatica: true, regra: resposta.regraId,
      texto: resposta.texto,
    }, { id: mensagemId, agora, contexto })
  })
}

// --- Rodada 11 · a resposta automática "digitando" (registro 92) ------------
// Antes a resposta do automático caía no mesmo instante da fala do cliente, e
// ninguém via a casa responder: só aparecia uma bolha a mais. Agora ela entra
// numa fila (`respostaPendente`) e a tela mostra "digitando" até
// `ENTREGAR_RESPOSTA_AUTOMATICA` soltar uma de cada vez
// (`aplicacao/useDigitacaoAutomatica.js`). Assumir no meio (US-004, RN-04)
// descarta a fila: o automático para na hora, inclusive o que ia dizer.
const saidaAutomatica = ({ texto, regra }) => ({ dir: 'out', status: 'lida', automatica: true, regra, texto })

function comFilaEntregue(conversa, agora, { descartar = false } = {}) {
  const fila = conversa.respostaPendente ?? []
  if (fila.length === 0) return conversa
  const semFila = { ...conversa, respostaPendente: [] }
  if (descartar) return semFila
  return fila.reduce((c, pendente) => comMensagemLocal(c, saidaAutomatica(pendente), { id: pendente.id, agora }), semFila)
}

// Aplica na conversa o que a fala do cliente trouxe (UC-01 passos 3 a 8,
// US-011): nome, telefone, endereço dentro da área, gosto que vira tag e, com
// o cadastro pronto, os itens citados viram comanda. Cada campo preenchido
// guarda QUANDO foi captado em `cliente.captado`, e é essa marca que a ficha
// lê para mostrar o selo "captado da conversa". Endereço fora ou no limite da
// área não entra no cadastro: vira `enderecoCapturado`, o mesmo caminho da
// exceção de área que a dona decide (UC-02).
function capturarDaFala(conversa, texto, {
  cardapio, prefixos, agora, mensagemId, numeroPedido, acabouDeSaudar,
}) {
  const em = new Date(agora).toISOString()
  const dados = extrairDados(texto, { aguardando: conversa.captura?.aguardando ?? null, cardapio })
  const captado = { ...(conversa.cliente.captado ?? {}) }
  const cliente = { ...conversa.cliente, tags: conversa.cliente.tags ?? [] }
  let nome = conversa.nome
  let captouAlgo = false

  if (dados.nome && !captado.nome) {
    nome = dados.nome; captado.nome = em; captouAlgo = true
  }
  if (dados.telefone && dados.telefone !== cliente.telefone) {
    cliente.telefone = dados.telefone; captado.telefone = em; captouAlgo = true
  }
  if (dados.endereco && !cliente.endereco) {
    if (situacaoDoCep(dados.endereco, prefixos) === SITUACOES.DENTRO) {
      cliente.endereco = dados.endereco; cliente.enderecoCapturado = null; captado.endereco = em; captouAlgo = true
    } else {
      cliente.enderecoCapturado = dados.endereco
    }
  }
  if (dados.gosto && !cliente.tags.includes(tagDeGosto(dados.gosto))) {
    cliente.tags = [...cliente.tags, tagDeGosto(dados.gosto)]; captado.gosto = em; captouAlgo = true
  }

  let atualizada = { ...conversa, nome, cliente: { ...cliente, captado } }

  // O cadastro nasce UMA vez, quando nome, telefone e endereço estão na ficha.
  // RN-03: continua `lead`; quem promove é a primeira compra paga.
  const cadastroCriado = cadastroCompleto(atualizada) && !conversa.captura?.cadastroEm
  if (cadastroCriado) {
    // Lead do WhatsApp: o número do canal vira o telefone do cadastro, a mesma
    // cópia de CADASTRAR_ENDERECO e SALVAR_CADASTRO_RAPIDO (registro 73). Sem
    // ela o cadastro automático nascia sem telefone e RN-02/US-002 não
    // reconheciam o mesmo número na conversa seguinte (registro 101).
    if (!atualizada.cliente.telefone && atualizada.canal === 'WhatsApp' && atualizada.cliente.telefoneCanal) {
      atualizada = { ...atualizada, cliente: { ...atualizada.cliente, telefone: atualizada.cliente.telefoneCanal } }
    }
    atualizada = comMensagemLocal(atualizada, { dir: 'sistema', texto: TEXTO_CADASTRO_CRIADO }, { id: `${mensagemId}-cadastro`, agora })
  }

  const itensAnotados = !atualizada.pedido && numeroPedido ? dados.itens : []
  if (itensAnotados.length > 0) {
    atualizada = { ...atualizada, pedido: itensAnotados.reduce((p, item) => comItem(p, item.sku), novoPedido(numeroPedido)) }
  }

  const resposta = respostaDaCaptura({ conversa: atualizada, itensAnotados, cadastroCriado, captouAlgo })
    ?? (acabouDeSaudar ? perguntaPara(proximoCampo(atualizada), atualizada.nome) : null)

  const captura = {
    aguardando: proximoCampo(atualizada),
    cadastroEm: cadastroCriado ? em : (conversa.captura?.cadastroEm ?? null),
    pedidoEm: itensAnotados.length > 0 ? em : (conversa.captura?.pedidoEm ?? null),
  }
  return { conversa: { ...atualizada, captura }, resposta, itensAnotados }
}

export const casosSimulacao = {
  // Cria a conversa (id `sim-…`, já resolvido por `acoes/simulacao.js`) e
  // leva a dona para ela — é o "abre a conversa" do rodapé da seção 7.
  [acao.SIMULAR_CONVERSA]: (estado, { conversa }) => ({
    ...estado,
    conversas: [conversa, ...estado.conversas],
    selecionadaId: conversa.id,
    agente: { estado: 'ocioso', sugestao: null, conversaId: null },
  }),

  // O evento. Tudo que a documentação manda sair sozinho (RN-01, RN-02,
  // RN-10/11, RN-52/53, US-032) passa por aqui.
  [acao.SIMULAR_MENSAGEM]: (estado, {
    id, texto, agora, mensagemId, comprovante = false, foto = false, endereco = null,
    audio = false, duracaoMs = null, numeroPedido = null,
  }) => {
    const conversaAntes = estado.conversas.find((c) => c.id === id)
    if (!conversaAntes) return estado

    const bloqueada = Boolean(conversaAntes.bloqueio)
    const automaticoPausado = Boolean(estado.automaticoPausado[id])
    // Rodada 11: resposta que ainda estava "digitando" sai antes da fala nova,
    // para a conversa manter a ordem; com o automático parado, some.
    const conversa = comFilaEntregue(conversaAntes, agora, { descartar: bloqueada || automaticoPausado })
    const primeiraMensagemDoCliente = !conversa.mensagens.some((m) => m.dir === 'in')
    const cadastroConhecido = cadastroJaConhecido(estado.conversas, conversa)

    // Endereço que o cliente digitou (US-012, UC-02 passo 2): vai para o
    // endereço capturado da ficha, onde a checagem de área decide entre o
    // botão de cadastrar e as três ações de exceção. Marca no payload, como
    // `comprovante`, e não regex sobre o texto.
    const comEndereco = endereco && !conversa.cliente?.endereco
      ? { ...conversa, cliente: { ...conversa.cliente, enderecoCapturado: endereco } }
      : conversa
    // Reabrir ao vivo (rodada 5, seção 5, F5): cliente escrevendo de novo é o
    // único gatilho de reabertura que a direção pede fora do botão "Reabrir
    // conversa" manual. `REABRIR_CONVERSA` (reducer.js) faz o mesmo para o
    // clique; aqui é o mesmo par de campos, só que pelo evento de mensagem que
    // já é desta ação. Sem âncora em `casos/encerramento.js` pra isto morar
    // (SIMULAR_MENSAGEM é o único lugar que sabe que uma mensagem DE CLIENTE
    // chegou), decisão registrada em auditoria/decisoes/36-f5-encerrar.md.
    const reabreEncerrada = comEndereco.estado === 'Encerrado'
      ? { estado: 'Em atendimento', responsavel: comEndereco.responsavel ?? 'Thatiane' }
      : {}
    // Rodada 7 · frente Anexos (fala do dono 24/09/2026: "receber áudio"):
    // áudio de verdade na bolha (toca, pausa, progride), não só uma marca
    // como `foto`/`midia` acima — por isso gera o WAV fictício aqui, não só
    // sinaliza um booleano.
    const audioSimulado = audio ? { formato: 'audio', arte: gerarAudioSimulado(duracaoMs ?? 4000), duracaoMs: duracaoMs ?? 4000 } : {}
    const conversaComMensagem = comMensagemLocal({ ...comEndereco, ...reabreEncerrada }, {
      dir: 'in', texto, ...(foto ? { midia: true } : {}), ...audioSimulado,
    }, { id: mensagemId, agora })

    const resultado = respostaParaMensagemDeCliente({
      conversaComMensagem,
      primeiraMensagemDoCliente,
      cadastroConhecido,
      bloqueada,
      automaticoPausado,
      comprovante,
      catalogo: estado.catalogo,
      regras: estado.regras,
      agora,
      prefixosCepAtendidos: PREFIXOS_CEP_ATENDIDOS,
      funcionamento: estado.funcionamento,
      lojaAberta: estado.lojaAberta,
      // US-001 (regra de negócio)/UC-01 passo 2: mesma origem que o botão
      // "Enviar cardápio" do composer usa (features/atendimento/Composer.jsx),
      // para o link da boas-vindas automática apontar para a rota real.
      origemCardapio: typeof window !== 'undefined' ? window.location.origin + window.location.pathname : '',
    })

    // Conciliação (US-032): dinheiro reconhecido sozinho, sem ela tocar.
    if (resultado.concilia && conversaComMensagem.pedido?.cobranca && !conversaComMensagem.pedido.cobranca.pagaEm) {
      const cobranca = aplicarPagamento(conversaComMensagem.pedido.cobranca, agora, conversaComMensagem.pedido.cobranca.valor)
      const comConversaAtualizada = { ...estado, conversas: estado.conversas.map((c) => (c.id === id ? conversaComMensagem : c)) }
      return comPagamentoReconhecidoLocal(
        comConversaAtualizada, id, cobranca, resultado.respostaAutomatica, { agora, mensagemId: `${mensagemId}-recibo` },
      )
    }

    // Rodada 10 · achado P2.6 (D3, US-003/US-008/US-009): quando o
    // classificador não decide nada (nem resposta automática, nem passagem,
    // nem loja fechada) e a fala tem cara de pergunta, o mínimo é sinalizar
    // "Precisa de você" com o motivo, em vez de deixar o cliente sem
    // resposta até o próximo aviso de esteira. Nunca troca uma decisão que
    // `respostaParaMensagemDeCliente` já tomou (a função em si continua
    // intocada, fronteira da frente 80), e nunca entra em cima do silêncio
    // DELIBERADO (RN-14 bloqueado, automático pausado, já passou para ela):
    // esses casos também chegam com os quatro campos vazios, mas calar é o
    // comportamento certo ali, não um defeito para corrigir.
    const jaEmSilencioDeProposito = bloqueada || automaticoPausado
      || Boolean(conversaComMensagem.passagem && !conversaComMensagem.passagem.assumida)

    // Rodada 11 · captura (registro 92). Só com o automático conduzindo: nada
    // de captar com ela no controle (Assumir, US-004), com a loja fechada
    // (o automático não atende), nem quando a fala já passou para ela
    // (restrição, saldo, área: RN-52, RN-53, RN-11). Só lead: cliente já tem
    // cadastro (US-002 é outro fluxo).
    const regraBoasVindas = regraDoGatilho(estado.regras, GATILHOS.PRIMEIRO_CONTATO)
    const saudou = Boolean(resultado.respostaAutomatica && regraBoasVindas
      && resultado.respostaAutomatica.regraId === regraBoasVindas.id)
    const automaticoConduz = !jaEmSilencioDeProposito && !resultado.passarParaDona
      && !resultado.aguardandoAbertura && !resultado.concilia
    const captura = automaticoConduz && ehLead(conversaComMensagem)
      ? capturarDaFala(conversaComMensagem, texto, {
        cardapio: estado.catalogo.cardapio, prefixos: PREFIXOS_CEP_ATENDIDOS, agora, mensagemId, numeroPedido,
        acabouDeSaudar: saudou,
      })
      : null
    // A pergunta da captura só sai sozinha ou logo depois da boas-vindas:
    // qualquer outra resposta automática (fora de área, status) fala por si.
    const respostaCaptura = captura?.resposta && (!resultado.respostaAutomatica || saudou) ? captura.resposta : null

    const semDecisaoNenhuma = !resultado.respostaAutomatica && !resultado.passarParaDona
      && !resultado.aguardandoAbertura && !respostaCaptura
    const resultadoComFallback = semDecisaoNenhuma && !jaEmSilencioDeProposito && ehPerguntaSemResposta(texto)
      ? { ...resultado, passarParaDona: MOTIVO_PERGUNTA_FORA_DO_ROTEIRO }
      : resultado

    let comEstado = mapear(estado, id, () => captura?.conversa ?? conversaComMensagem)

    // Itens anotados pelo automático baixam o saldo do mesmo jeito que
    // ADICIONAR_ITEM (reducer.js): o cardápio da tela não pode oferecer o
    // que já saiu.
    if (captura?.itensAnotados.length) {
      const cardapio = captura.itensAnotados.reduce((c, item) => baixarSaldo(c, item.sku), comEstado.catalogo.cardapio)
      comEstado = { ...comEstado, catalogo: { ...comEstado.catalogo, cardapio } }
    }

    const fila = []
    if (resultadoComFallback.respostaAutomatica) {
      fila.push({
        id: `${mensagemId}-auto`,
        texto: resultadoComFallback.respostaAutomatica.texto,
        regra: resultadoComFallback.respostaAutomatica.regraId,
      })
    }
    if (respostaCaptura) fila.push({ id: `${mensagemId}-captura`, texto: respostaCaptura, regra: 'captura' })
    if (fila.length > 0) {
      comEstado = mapear(comEstado, id, (c) => ({ ...c, respostaPendente: [...(c.respostaPendente ?? []), ...fila] }))
    }

    if (resultadoComFallback.passarParaDona) {
      comEstado = mapear(comEstado, id, (c) => (c.passagem && !c.passagem.assumida ? c : {
        ...c, passagem: { motivo: resultadoComFallback.passarParaDona, em: new Date(agora).toISOString(), assumida: false },
      }))
    }

    // Fila de "loja fechada" (pedido do dono, 24/09/2026): marca quando o
    // automático segurou a mensagem por horário ou estado da loja, e some
    // assim que outra resposta de verdade sai (ou passa para ela) nesta
    // mesma conversa.
    if (resultadoComFallback.aguardandoAbertura) {
      comEstado = mapear(comEstado, id, (c) => ({ ...c, aguardandoAbertura: true }))
    } else if (resultadoComFallback.respostaAutomatica || resultadoComFallback.passarParaDona || respostaCaptura) {
      comEstado = mapear(comEstado, id, (c) => (c.aguardandoAbertura ? { ...c, aguardandoAbertura: false } : c))
    }

    return comEstado
  },

  // Rodada 11 (registro 92): solta a próxima resposta "digitando". Com o
  // automático parado no meio da digitação (Assumir, ela escrevendo, US-004),
  // a fila inteira some: nada sai em nome da casa depois que ela pegou.
  [acao.ENTREGAR_RESPOSTA_AUTOMATICA]: (estado, { id, agora }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    const [proxima, ...resto] = conversa?.respostaPendente ?? []
    if (!proxima) return estado
    if (estado.automaticoPausado[id] || conversa.bloqueio) {
      return mapear(estado, id, (c) => ({ ...c, respostaPendente: [] }))
    }
    return mapear(estado, id, (c) => comMensagemLocal(
      { ...c, respostaPendente: resto }, saidaAutomatica(proxima), { id: proxima.id, agora },
    ))
  },

  // Rodada 10 · o evento do cliente simulado que reage (registro 79). Uma
  // ação só e genérica: mensagem "in" (o cliente fala) ou "out" (a casa
  // pergunta, caso da avaliação), às vezes com item para a comanda nascer
  // sozinha (item a/b) e às vezes abrindo a ocorrência sozinha (item d,
  // avaliação negativa, RN-35). Idempotente por `marcador`: sem isto, um
  // re-render do observador (`aplicacao/useReacaoClienteSimulado.js`)
  // agendaria a mesma fala duas vezes.
  [acao.SIMULAR_EVENTO_CLIENTE]: (estado, {
    id, agora, mensagemId, dir, texto, formato = null, valor = null, marcador,
    item = null, numeroPedido = null, abrirOcorrenciaComRelato = null, ocorrenciaId = null,
  }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa) return estado
    if ((conversa.reacoesClienteFeitas ?? []).includes(marcador)) return estado

    // "out" é a casa perguntando sozinha (caso da avaliação, item d): mesma
    // marca visual de automática/lida que qualquer outro aviso da esteira,
    // para não parecer que a Thatiane digitou isso na hora.
    const marcasDeSaida = dir === 'out' ? { automatica: true, status: 'lida' } : {}
    let comEstado = mapear(estado, id, (c) => comMensagemLocal({
      ...c, reacoesClienteFeitas: [...(c.reacoesClienteFeitas ?? []), marcador],
    }, {
      dir, texto, ...marcasDeSaida, ...(formato ? { formato } : {}), ...(valor ? { valor } : {}),
    }, { id: mensagemId, agora }))

    if (item) {
      comEstado = mapear(comEstado, id, (c) => ({
        ...c,
        pedido: comItem(c.pedido ?? novoPedido(numeroPedido), item.sku),
      }))
      comEstado = {
        ...comEstado,
        catalogo: { ...comEstado.catalogo, cardapio: baixarSaldo(comEstado.catalogo.cardapio, item.sku) },
      }
    }

    if (abrirOcorrenciaComRelato) {
      comEstado = mapear(comEstado, id, (c) => (ocorrenciaAberta(c.pedido?.ocorrencia) ? c : {
        ...c,
        pedido: {
          ...c.pedido,
          ocorrencia: abrirOcorrencia({
            id: ocorrenciaId ?? `ocorrencia-${mensagemId}`,
            pedidoNumero: c.pedido.numero,
            relatoCliente: abrirOcorrenciaComRelato,
            agora,
          }),
        },
      }))
    }

    return comEstado
  },

  // "Pix cai" fora do clique da dona: mesma conta de CONFIRMAR_PAGAMENTO, só
  // que o gatilho é o provedor avisando sozinho, não o botão "Marcar pago".
  [acao.SIMULAR_PAGAMENTO]: (estado, { id, agora, mensagemId, valorForcado = null }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.cobranca) return estado
    const valorPago = valorForcado ?? conversa.pedido.cobranca.valor
    const cobranca = aplicarPagamento(conversa.pedido.cobranca, agora, valorPago)
    const recibo = regraDoGatilho(estado.regras, GATILHOS.PAGAMENTO_CONFIRMADO)
    const janela = janelaPorId(estado.catalogo.janelas, conversa.pedido.janela)
    // `agora` aplica o respiro (RN-06, RN-22, US-028) dentro de contextoDePrevia.
    const resposta = recibo
      ? { texto: textoDaRegra(recibo, contextoDePrevia(conversa, janela?.faixa, agora)), regraId: recibo.id }
      : null
    return comPagamentoReconhecidoLocal(estado, id, cobranca, resposta, { agora, mensagemId })
  },

  // Relógio do painel (seção 7): "+10 min"/"+30 min" somam, "Agora" zera.
  // `acoes/simulacao.js` já resolve qual dos três campos mandar.
  [acao.DESLOCAR_RELOGIO]: (estado, { incrementoMs = null, definirMs = null, zerar = false, lojaSegueHorario = false }) => {
    // Integração 48: passo de cenário que devolve a loja ao horário (lojaAberta
    // null), o mesmo estado de antes do primeiro toque no controle do topo.
    if (lojaSegueHorario) estado = { ...estado, lojaAberta: null }
    if (zerar) return { ...estado, relogio: { ...estado.relogio, deslocamentoMs: 0 } }
    if (definirMs != null) return { ...estado, relogio: { ...estado.relogio, deslocamentoMs: definirMs } }
    return {
      ...estado,
      relogio: { ...estado.relogio, deslocamentoMs: (estado.relogio?.deslocamentoMs ?? 0) + (incrementoMs ?? 0) },
    }
  },

  // "Limpar simulações": tira tudo que tem id `sim-` (rodapé da seção 7). A
  // tela volta a mostrar a primeira conversa real quando a selecionada era
  // uma simulação.
  [acao.LIMPAR_SIMULACOES]: (estado) => {
    const restantes = estado.conversas.filter((c) => !c.id.startsWith('sim-'))
    const selecaoValia = restantes.some((c) => c.id === estado.selecionadaId)
    return {
      ...estado,
      conversas: restantes,
      selecionadaId: selecaoValia ? estado.selecionadaId : (restantes[0]?.id ?? null),
    }
  },
}
