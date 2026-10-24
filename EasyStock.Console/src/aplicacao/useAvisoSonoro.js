import { useEffect, useRef, useState } from 'react'
import { tocarAviso } from '../infra/som'
import { motivoVisivel, precisaDeVoce } from '../dominio/automatico'
import { notificar } from '../infra/notificacaoNavegador'
import { TIPOS_DE_AVISO, avisosDoRetrato } from '../dominio/aviso'
import { pagamentoDoRetrato } from '../dominio/avisoSonoro'

// Quando o som toca (D6, US-010, RN-25). O COMO mora em infra/som.js.
//
// O disparo NUNCA olha o nome da ação do reducer: compara o retrato de agora
// com o de antes e toca pela DIFERENÇA de estado. O dev de Simular está
// criando a ação de "mensagem chegando" e o nome dela pode mudar; comparando
// estado em vez de ação, este hook funciona com qualquer nome.
//
// Três momentos, da entrevista e de RN-25/UC-04/UC-05:
//   pedido que virou pago (o "barulhinho do dinheiro" do áudio 06);
//   mensagem nova do cliente em qualquer conversa (UC-05 passo 1);
//   conversa que passou a precisar dela: passagem, reclamação aberta, entrega
//   ou pedido atrasado, ou Pix vencendo, o mesmo motivo que acende "Precisa
//   de você" no Balcão (`precisaDeVoce`, dominio/automatico.js) — não só o
//   campo `passagem`.
//
// Rodada 10: chamado também de dentro das janelas espelho (Cozinha,
// Entregas, achado 1), por isso `janelas` chega por parâmetro em vez de vir
// de um contexto que essas janelas não têm. Devolve `destacados` (achado 1,
// "destaque"): ids que acabaram de mudar, por ~1,5 s, para o cartão piscar
// sem precisar de outro observador.
//
// #1807: cada mudança vira um aviso (`dominio/aviso.js`), com contato novo e
// pedido que mudou de etapa além dos três sons. O som e o destaque são desta
// janela; o resto (toast e navegador) vai para `aoAvisar`, o despachante do
// Provider. Sem `aoAvisar` (Cozinha e Entregas espelho), segue o de antes:
// só "precisa de você" vai ao navegador.
const DURACAO_DESTAQUE_MS = 1500

const avisarSoNoNavegador = (aviso) => {
  if (aviso.tipo === TIPOS_DE_AVISO.PASSOU_PARA_VOCE) notificar(aviso.tipo, aviso.conversaId, aviso.titulo, aviso.corpo)
}

export function useAvisoSonoro({
  conversas, som, agora, aberta = true, janelas = [], expediente = null, aoTocar, aoAvisar = avisarSoNoNavegador,
}) {
  const anterior = useRef(null)
  const [destacados, setDestacados] = useState(() => new Set())

  const destacarPorInstante = (id) => {
    setDestacados((atual) => new Set(atual).add(id))
    setTimeout(() => {
      setDestacados((atual) => {
        const novo = new Set(atual)
        novo.delete(id)
        return novo
      })
    }, DURACAO_DESTAQUE_MS)
  }

  useEffect(() => {
    const retrato = new Map((conversas ?? []).map((c) => {
      const ultimaDoCliente = (c.mensagens ?? []).filter((m) => m.dir === 'in').at(-1)
      const precisa = precisaDeVoce(c, agora, true, aberta, janelas, expediente)
      return [c.id, {
        ...pagamentoDoRetrato(c.pedido),
        precisa,
        ultimaMensagemClienteId: ultimaDoCliente?.id ?? null,
        etapa: c.pedido?.estado ?? null,
        nome: c.nome ?? '',
        motivo: precisa ? motivoVisivel(c, agora, true, aberta, janelas, expediente) : null,
      }]
    }))
    const antes = anterior.current
    anterior.current = retrato
    // Primeira volta (ou lista ainda vazia, modo API) é só retrato: `dominio/avisoSonoro.js`.
    for (const aviso of avisosDoRetrato(antes, retrato)) {
      if (aviso.som) {
        tocarAviso(aviso.som, som)
        aoTocar?.(aviso.som, aviso.conversaId)
      }
      if (aviso.destaca) destacarPorInstante(aviso.conversaId)
      aoAvisar?.(aviso)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [conversas, som, agora, aberta, janelas, expediente])

  return destacados
}
