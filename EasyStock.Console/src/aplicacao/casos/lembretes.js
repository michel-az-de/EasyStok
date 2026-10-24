import { chaveLembrete, FONTES, ORIGENS } from '../../dominio/lembrete'
import * as acao from '../acoes'
import { comMensagem, mapear } from '../reducer/auxiliares'

// Lembretes da dona e os que o EasyStok devolveu (`SINCRONIZAR_LEMBRETES_API`). Valem nos dois modos.

export const casosLembretes = {
  [acao.CRIAR_LEMBRETE]: (estado, { lembrete }) => ({
    ...estado,
    revisaoLembretes: (estado.revisaoLembretes ?? 0) + 1,
    lembretes: [...estado.lembretes.filter((l) => l.id !== lembrete.id), lembrete],
  }),

  // Concluído sai da lista e fica no histórico da conversa como mensagem de
  // sistema. Ela precisa enxergar depois que aquilo foi feito, e onde.
  [acao.CONCLUIR_LEMBRETE]: (estado, { lembreteId, titulo, conversaId, agora, mensagemId }) => {
    const semEle = {
      ...estado,
      revisaoLembretes: (estado.revisaoLembretes ?? 0) + 1,
      lembretes: estado.lembretes.filter((l) => l.id !== lembreteId),
      lembretesConcluidos: { ...estado.lembretesConcluidos, [lembreteId]: true },
    }
    if (!conversaId) return semEle
    return mapear(semEle, conversaId, (c) => comMensagem(c, {
      dir: 'sistema', texto: 'Lembrete concluído: ' + titulo,
    }, { id: mensagemId, agora }))
  },

  // Os lembretes automáticos são retrato do estado das conversas: o que não
  // vale mais sai sozinho, o que foi adiado mantém a hora nova, e o que ela
  // concluiu não volta.
  // O que veio do EasyStok (`fonte`, modo API) também fica: quem troca isso é a
  // SINCRONIZAR_LEMBRETES_API.
  [acao.SINCRONIZAR_LEMBRETES]: (estado, { automaticos }) => {
    const manuais = estado.lembretes.filter((l) => l.origem === ORIGENS.MANUAL || l.fonte)
    const antigos = new Map(estado.lembretes.map((l) => [l.id, l]))
    const vivos = automaticos
      .filter((l) => !estado.lembretesConcluidos[l.id])
      .map((l) => antigos.get(l.id) ?? l)
    const proximos = [...manuais, ...vivos]
    const igual = proximos.length === estado.lembretes.length
      && proximos.every((l, i) => l === estado.lembretes[i])
    return igual ? estado : { ...estado, lembretes: proximos }
  },

  // Uma consulta iniciada antes de salvar não pode apagar o lembrete recém-criado.
  [acao.SINCRONIZAR_LEMBRETES_API]: (estado, { lembretes, revisao }) => {
    if (!Array.isArray(lembretes) || (revisao !== undefined && revisao !== (estado.revisaoLembretes ?? 0))) return estado
    const antigos = new Map(estado.lembretes.map((l) => [l.id, l]))
    const mesmo = (a, b) => a && a.quando === b.quando && a.titulo === b.titulo
      && a.detalhe === b.detalhe && a.conversaId === b.conversaId && a.visto === b.visto
    const chegaram = lembretes
      .filter((l) => !estado.lembretesConcluidos[l.id])
      .map((l) => (mesmo(antigos.get(l.id), l) ? antigos.get(l.id) : l))
    const proximos = [...estado.lembretes.filter((l) => l.fonte !== FONTES.LEMBRETE), ...chegaram]
    const vistos = { ...estado.vistos }
    for (const l of chegaram) if (l.visto) vistos[chaveLembrete(l)] = true
    const atuais = new Set(estado.lembretes)
    const igual = proximos.length === estado.lembretes.length && proximos.every((l) => atuais.has(l))
      && Object.keys(vistos).length === Object.keys(estado.vistos).length
    return igual ? estado : { ...estado, lembretes: proximos, vistos }
  },

  // Visto estilo Instagram (seção 8): a Frente B manda as chaves `id+quando`
  // dos itens de "Novos" quando o painel fecha, e elas somam ao conjunto.
  [acao.MARCAR_LEMBRETES_VISTOS]: (estado, { chaves }) => {
    if (!chaves?.length) return estado
    const vistos = { ...estado.vistos }
    for (const chave of chaves) vistos[chave] = true
    return { ...estado, vistos, revisaoLembretes: (estado.revisaoLembretes ?? 0) + 1 }
  },
}
