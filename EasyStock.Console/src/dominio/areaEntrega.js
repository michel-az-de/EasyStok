// Área de entrega (US-012/US-013, RN-09/RN-10/RN-11, UC-01 fluxo alternativo A,
// UC-02). Puro, sem rede: decide se um CEP atende, está no limite ou não
// atende, e o que fazer quando o lead insiste depois do aviso.
//
// A lista de bairros atendidos já existe em infra/catalogo.js
// (PREFIXOS_CEP_ATENDIDOS) e ninguém usava (auditoria/decisoes/22, item 2):
// este arquivo é o primeiro a usar. Domínio não importa infra (regra de
// ferramentas/verificar-camadas.mjs), então o prefixo chega por parâmetro:
// quem chama pega a lista em `catalogo.prefixosCepAtendidos`
// (infra/repositorioCatalogo.js).
//
// Chamado por dominio/agente.js (uma chamada, não reescrita) e por quem for
// tratar o evento "mensagem do cliente chegou" (Simular): `situacaoDoCep`
// aceita tanto um CEP cru quanto o texto livre da mensagem ou do endereço
// capturado, porque as duas origens chegam assim na conversa.

export const SITUACOES = {
  DENTRO: 'dentro',
  LIMITE: 'limite',
  FORA: 'fora',
  SEM_CEP: 'sem-cep',
}

// Acha um CEP de 8 dígitos em qualquer texto livre: com ou sem hífen, com ou
// sem o rótulo "CEP" na frente ("05433-000", "05433000", "CEP 05433-000",
// "Av. Pompeia 1870, Perdizes, CEP 05022-001"). Formato ruim (menos de 8
// dígitos, letra no meio do número) não casa e devolve null.
export function extrairCep(texto) {
  const casado = /\b(\d{5})-?(\d{3})\b/.exec(String(texto ?? ''))
  return casado ? `${casado[1]}${casado[2]}` : null
}

// "No limite": prefixo de 5 dígitos vizinho de um prefixo atendido, mesmo
// grão grosseiro que a lista já usa para achar bairro por número (comentário
// de infra/catalogo.js). Não é geocodificação, é a mesma aproximação que já
// existia, só que agora com um segundo balde além de "atende"/"não atende".
const DISTANCIA_DE_LIMITE = 2

function menorDistancia(prefixoCep, prefixos) {
  return prefixos.reduce(
    (menor, prefixo) => Math.min(menor, Math.abs(Number(prefixoCep) - Number(prefixo))),
    Infinity,
  )
}

export function situacaoDoCep(textoOuCep, prefixos = []) {
  const cep = extrairCep(textoOuCep)
  if (!cep) return SITUACOES.SEM_CEP
  const prefixoCep = cep.slice(0, 5)
  if (prefixos.includes(prefixoCep)) return SITUACOES.DENTRO
  return menorDistancia(prefixoCep, prefixos) <= DISTANCIA_DE_LIMITE ? SITUACOES.LIMITE : SITUACOES.FORA
}

// Rodada 10, achado 5 / item 5: US-013 pede endereço E distância no aviso de
// lead fora de área, e o sistema só tinha endereço. Geocodificação de
// verdade é fora do escopo de uma correção pequena (achado 5, "correção
// sugerida"); isto expõe o proxy que já existia por dentro
// (`menorDistancia`, usado só para classificar dentro/limite/fora) como um
// número de faixas de CEP, para não inventar uma distância em km que o
// domínio não sabe calcular.
export function faixasDeDistancia(textoOuCep, prefixos = []) {
  const cep = extrairCep(textoOuCep)
  if (!cep || prefixos.length === 0) return null
  return menorDistancia(cep.slice(0, 5), prefixos)
}

// RN-10 não distingue "limite" de "fora": as duas não têm entrega garantida,
// então as duas disparam o mesmo aviso e a mesma trava de promessa.
export const foraDaArea = (situacao) => situacao === SITUACOES.FORA || situacao === SITUACOES.LIMITE

// RN-10: resposta imediata, na hora, perguntando se o lead quer seguir mesmo
// assim. Curta de propósito: é a fala do automático, não um comunicado.
export function respostaDeAreaFora(nome) {
  return `Oi ${nome}! Essa região ainda não é atendida por aqui. `
    + 'Quer que eu veja com a Thatiane se dá uma exceção mesmo assim?'
}

// RN-11: pistas de que o lead insistiu depois do aviso. Lista curta e aberta,
// mesmo espírito das pistas de dominio/agente.js: não tenta cobrir toda
// insistência do português, cobre a mais comum.
const PISTAS_DE_INSISTENCIA = [
  'mesmo assim', 'quero mesmo', 'continua', 'sim, quero', 'pode seguir', 'insisto', 'sem problema', 'vamos sim',
  'ver com a thatiane', 'consegue ver', 'fala com a thatiane', 'pergunta pra ela', 'pergunta pra thatiane',
  'faz uma exceção', 'faz uma excecao', 'abre uma exceção', 'abre uma excecao', 'pago a mais', 'pago mais',
  'de qualquer jeito', 'por favor',
]
export const insistiuMesmoForaDaArea = (texto) =>
  PISTAS_DE_INSISTENCIA.some((p) => String(texto ?? '').toLowerCase().includes(p))

// Verdadeiro só quando: (1) alguma fala anterior do cliente já tinha CEP fora
// ou no limite, e o automático já avisou disso, e (2) a fala mais nova é
// insistência, não um CEP novo. Sem isto, qualquer "mesmo assim" solto
// escalaria para a dona.
export function precisaEscalarPorArea(conversa, prefixos = []) {
  const mensagens = conversa?.mensagens ?? []
  const doCliente = mensagens.filter((m) => m.dir === 'in')
  if (doCliente.length < 2) return false
  const ultima = doCliente.at(-1)
  const jaAvisado = doCliente.slice(0, -1).some((m) => foraDaArea(situacaoDoCep(m.texto, prefixos)))
  return jaAvisado && insistiuMesmoForaDaArea(ultima.texto)
}

// UC-02: as três ações de um toque da dona na ficha, e o motivo que cada uma
// grava no cadastro do lead. Nota interna (RN-08 não se aplica: nunca chega
// ao cliente), então o texto aqui é para Thatiane reler depois, não para o
// automático repetir.
export const DECISOES_DE_EXCECAO = {
  LIBERAR: 'liberar',
  ENCOMENDA_AGENDADA: 'encomenda-agendada',
  RECUSAR: 'recusar',
}

const MOTIVO_DA_DECISAO = {
  [DECISOES_DE_EXCECAO.LIBERAR]: 'Fora da área de entrega, liberado por exceção',
  [DECISOES_DE_EXCECAO.ENCOMENDA_AGENDADA]: 'Fora da área de entrega, virou encomenda agendada',
  [DECISOES_DE_EXCECAO.RECUSAR]: 'Fora da área de entrega, recusado com cortesia',
}

export const motivoDaDecisao = (decisao) => MOTIVO_DA_DECISAO[decisao] ?? 'Fora da área de entrega, decisão registrada'
