import { useEffect, useRef, useState } from 'react'
import { EVENTOS, tocarAviso } from '../infra/som'
import { motivoVisivel, precisaDeVoce } from '../dominio/automatico'
import { notificarPrecisaDeVoce } from '../infra/notificacaoNavegador'

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
const DURACAO_DESTAQUE_MS = 1500

export function useAvisoSonoro({
  conversas, som, agora, aberta = true, janelas = [], aoTocar,
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
      return [c.id, {
        pago: c.pedido?.estado === 'pago',
        precisa: precisaDeVoce(c, agora, true, aberta, janelas),
        ultimaMensagemClienteId: ultimaDoCliente?.id ?? null,
      }]
    }))
    const antes = anterior.current
    anterior.current = retrato
    // Primeira volta é só retrato: o balcão inteiro não vira trinta avisos.
    if (!antes) return

    for (const [id, atual] of retrato) {
      const velho = antes.get(id)
      // Conversa nova na lista só conta como "mensagem nova" se já chegou
      // com fala do cliente (evita som em conversa criada vazia); pago e
      // precisa nunca disparam na primeira aparição, para não confundir o
      // estado de nascença com uma mudança de agora.
      const mensagemNova = velho
        ? Boolean(atual.ultimaMensagemClienteId) && atual.ultimaMensagemClienteId !== velho.ultimaMensagemClienteId
        : Boolean(atual.ultimaMensagemClienteId)
      const pagouAgora = Boolean(velho) && atual.pago && !velho.pago
      const passouAgora = Boolean(velho) && atual.precisa && !velho.precisa

      // Achado 6 (rodada 10): quando a mesma mensagem faz "passou para você"
      // E conta como "novo atendimento" no mesmo retrato, só o mais
      // importante toca (precisa de ação > chegou mensagem), nunca os dois
      // timbres sobrepostos.
      if (passouAgora) {
        tocarAviso(EVENTOS.PASSOU_PARA_VOCE, som)
        aoTocar?.(EVENTOS.PASSOU_PARA_VOCE, id)
        destacarPorInstante(id)
        const conversa = conversas.find((c) => c.id === id)
        notificarPrecisaDeVoce(
          id, 'Precisa de você', motivoVisivel(conversa, agora, true, aberta, janelas) ?? conversa?.nome ?? '',
        )
      } else if (mensagemNova) {
        tocarAviso(EVENTOS.NOVO_ATENDIMENTO, som)
        aoTocar?.(EVENTOS.NOVO_ATENDIMENTO, id)
      }
      if (pagouAgora) {
        tocarAviso(EVENTOS.PAGAMENTO_CONFIRMADO, som)
        aoTocar?.(EVENTOS.PAGAMENTO_CONFIRMADO, id)
        destacarPorInstante(id)
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [conversas, som, agora, aberta, janelas])

  return destacados
}
