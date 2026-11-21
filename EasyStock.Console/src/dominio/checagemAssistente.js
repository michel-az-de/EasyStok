// #2496: checagem do Assistente numa conversa (GET api/atendimento/conversas/{id}/assistente). A API roda as
// mesmas etapas do turno do robô e devolve todas, em ordem. Cada etapa passa, barra ou não se aplica; a cor
// nunca informa sozinha, o rótulo vai junto.
const ESTADOS = Object.freeze({
  Passa: Object.freeze({ chave: 'passa', rotulo: 'Passa', icone: 'circle-check', tom: 'ok' }),
  Barra: Object.freeze({ chave: 'barra', rotulo: 'Barra', icone: 'circle-x', tom: 'perigo' }),
  NaoSeAplica: Object.freeze({ chave: 'fora', rotulo: 'Não se aplica', icone: 'menos', tom: 'neutro' }),
})

export const estadoDaEtapa = (etapa) => ESTADOS[etapa?.estado] ?? ESTADOS.NaoSeAplica

// A chave é da loja toda e o PUT da configuração é da policy Admin: a mesma regra de `podeVerAvisosDoCliente`.
export const podeLigarAssistente = (acoes) => acoes?.editarAtendimento === true

export const PERGUNTA_AO_DESLIGAR = 'Desligar o Assistente na loja toda? Todas as conversas passam para a equipe.'

export function contagemDasEtapas(etapas = []) {
  const contagem = { passa: 0, barra: 0, fora: 0 }
  for (const etapa of etapas) contagem[estadoDaEtapa(etapa).chave] += 1
  return contagem
}

// A frase do topo: se o robô responde a próxima mensagem do cliente e, se não, o porquê.
export function vereditoDaChecagem(checagem) {
  if (!checagem) return null
  const pendentes = checagem.entradasPendentes ?? 0
  const fila = pendentes === 0 ? null
    : pendentes === 1 ? '1 mensagem do cliente sem resposta.'
      : `${pendentes} mensagens do cliente sem resposta.`
  return checagem.responde
    ? { tom: 'ok', titulo: 'O Assistente responde a próxima mensagem.', detalhe: fila }
    : { tom: 'perigo', titulo: 'O Assistente não responde esta conversa.', detalhe: checagem.bloqueio ?? null }
}
