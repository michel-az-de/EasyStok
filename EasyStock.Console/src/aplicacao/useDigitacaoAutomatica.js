// Rodada 11 · atendimento automático visível (issue #8, registro 92). Mesmo
// molde de `useReacaoClienteSimulado.js`: hook de `aplicacao` que só observa
// `estado.conversas` e agenda, montado uma vez em `app/App.jsx` para seguir
// valendo com a gaveta de simulações fechada.
//
// Toda resposta automática do evento "mensagem do cliente chegou"
// (SIMULAR_MENSAGEM) nasce em `conversa.respostaPendente`, e a tela mostra a
// casa "digitando". Este hook solta a primeira da fila depois de
// `tempoDigitandoMs`; a próxima só é agendada quando a anterior sai. Com o
// automático parado (Assumir, US-004), o caso ENTREGAR_RESPOSTA_AUTOMATICA
// descarta a fila em vez de soltar.
import { useEffect, useRef } from 'react'
import { useAcoes } from './contextos'
import { tempoDigitandoMs } from '../dominio/captura'

export function useDigitacaoAutomatica({ conversas, agora }) {
  const { entregarRespostaAutomatica } = useAcoes()
  // Chave = id da mensagem pendente: cada uma é agendada uma vez só, mesmo
  // com o efeito rodando de novo a cada mudança do estado.
  const agendadasRef = useRef(new Set())
  const timersRef = useRef([])
  const agoraRef = useRef(agora)
  useEffect(() => { agoraRef.current = agora }, [agora])
  useEffect(() => () => timersRef.current.forEach(clearTimeout), [])

  useEffect(() => {
    for (const conversa of conversas) {
      const proxima = conversa.respostaPendente?.[0]
      if (!proxima || agendadasRef.current.has(proxima.id)) continue
      agendadasRef.current.add(proxima.id)
      timersRef.current.push(setTimeout(
        () => entregarRespostaAutomatica(conversa.id, agoraRef.current),
        tempoDigitandoMs(proxima.texto),
      ))
    }
    // `entregarRespostaAutomatica` é estável (useMemo do Provider).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [conversas])
}
