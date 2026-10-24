import { canalDaConversa } from '../../dominio/canal'
import { envioBloqueado } from '../../dominio/janela'
import { baixarLotesFifo, devolverLotesFifo, registrarDescoberto } from '../../dominio/producao'
import { ATENDENTE } from '../reducer/estadoInicial'

// Ajudas que os casos do reducer dividem: as dos casos do modo API e as da demonstração.

export const mapear = (estado, id, transformar) => ({
  ...estado,
  conversas: estado.conversas.map((c) => (c.id === id ? transformar(c) : c)),
})

// Rodada 13 (issue #43): sku sem lote nenhum não muda em nada — continua só
// no `estoque` plano de `dominio/cardapio.js`, comportamento herdado. Sku com
// produção lançada baixa do lote mais antigo (RN-50); o que não coube vira
// descoberto persistente (RN-48/RN-49).
export const temLoteDoSku = (producao, sku) => producao.lotes.some((l) => l.sku === sku && !l.insumo)

export function comBaixaDeProducao(producao, sku, quantidade, agora) {
  if (!temLoteDoSku(producao, sku)) return producao
  const { lotes, descoberto } = baixarLotesFifo(producao.lotes, sku, quantidade)
  return {
    ...producao,
    lotes,
    descobertos: descoberto > 0
      ? registrarDescoberto(producao.descobertos, sku, descoberto, new Date(agora).toISOString())
      : producao.descobertos,
  }
}

export function comDevolucaoDeProducao(producao, sku, quantidade) {
  if (!temLoteDoSku(producao, sku)) return producao
  return { ...producao, lotes: devolverLotesFifo(producao.lotes, sku, quantidade).lotes }
}

// Cadastro bloqueado não tem caminho de envio nenhum: nem composer, nem painel
// do agente, nem regra automática, nem mensagem programada. Quem decide é a
// MESMA permissão de escrita que o composer consulta, e não uma segunda conta
// feita aqui dentro.
export const recusaEnvio = (estado, id, agora) => {
  const conversa = estado.conversas.find((c) => c.id === id)
  if (!conversa) return true
  return envioBloqueado(conversa, agora, canalDaConversa(estado.catalogo.canais, conversa))
}

// Mensagem que ela manda ao cliente PARA o automático e grava o dono (RN-04).
// Vale para texto, mídia e programada: as três são a casa falando por ela.
// `aguardandoAbertura` (Frente Horário e loja) some quando ela mesma
// escreve: a fila era "ninguém olhou ainda", e agora alguém olhou.
export const comEscritaDaCasa = (estado, id) => ({
  ...mapear(estado, id, (c) => ({
    ...c,
    estado: c.estado === 'Aberto' ? 'Em atendimento' : c.estado,
    responsavel: c.responsavel ?? ATENDENTE,
    aguardandoAbertura: false,
  })),
  automaticoPausado: { ...estado.automaticoPausado, [id]: true },
})

// Mensagem que sai para o cliente mexe no relógio da conversa. Registro de
// sistema não mexe, porque ninguém respondeu nada.
export function comMensagem(conversa, mensagem, { id, agora }) {
  const paraOCliente = mensagem.dir !== 'sistema'
  return {
    ...conversa,
    atrasada: paraOCliente ? false : conversa.atrasada,
    ultimaEm: paraOCliente ? new Date(agora).toISOString() : conversa.ultimaEm,
    mensagens: [...conversa.mensagens, { id, em: new Date(agora).toISOString(), ...mensagem }],
  }
}
