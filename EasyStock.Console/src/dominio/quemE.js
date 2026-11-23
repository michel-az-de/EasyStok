import { telefoneDaConversa } from './cadastroDoLead'

// #3209 (S84): o que o cartão do cliente mostra para dizer quem é, sem abrir nada. Só dado que a API já
// entrega: o telefone (do cadastro ou do número do WhatsApp, pela regra do cadastro do lead), os pedidos do
// dossiê e as imagens das mensagens já carregadas da conversa.
export const LIMITE_DE_MIDIAS = 6

// Foto apagada para todos (#2833) some do balão; aqui também.
const ehImagemCarregavel = (m) => Boolean(!m.apagadaEm && m.midia?.conversaId && m.midia.mensagemId && String(m.midia.mime ?? '').startsWith('image/'))

export function resumoQuemE(conversa) {
  const historico = (conversa.cliente?.historico ?? []).filter((p) => p.estado !== 'cancelado')
  const ultimo = [...historico].sort((a, b) => new Date(b.em) - new Date(a.em))[0]
  const imagens = (conversa.mensagens ?? []).filter(ehImagemCarregavel).sort((a, b) => new Date(b.em) - new Date(a.em))
  return {
    telefone: telefoneDaConversa(conversa),
    pedidos: { quantidade: conversa.cliente?.pedidos ?? 0, ultimo: ultimo ? { em: ultimo.em, total: ultimo.total } : null },
    midias: {
      itens: imagens.slice(0, LIMITE_DE_MIDIAS).map((m) => ({ ...m.midia, texto: m.texto })),
      restantes: Math.max(0, imagens.length - LIMITE_DE_MIDIAS),
    },
  }
}
