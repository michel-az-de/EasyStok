// Ponte da janela de Cozinha (`features/cozinha/TelaCozinha.jsx`) até o
// Balcão, por `infra/canalEntreJanelas.js` — mesmo padrão de
// `useEspelhoDeEntregas.js` (rodada 5, seção 6, usado aqui como modelo): o
// Balcão é sempre a janela "principal" do canal, então nada muda em
// `AtendimentoProvider.jsx` para a Cozinha existir. Só `avancarEsteira`
// (US-038/RN-31, um toque só) precisa de adaptador aqui, porque a Cozinha não
// tem um `criarAcoesX` próprio como Entregas tem em `acoes/entregas.js`.
import { useCallback, useEffect, useRef, useState } from 'react'
import { conectarEspelho } from '../infra/canalEntreJanelas'
import * as acao from './acoes'

const ESPERA_SEM_BALCAO_MS = 1000
const novoId = () => crypto.randomUUID()

export function useEspelhoDeCozinha() {
  const [estado, setEstado] = useState(null)
  const [semResposta, setSemResposta] = useState(false)
  const conexaoRef = useRef(null)

  useEffect(() => {
    conexaoRef.current = conectarEspelho({ aoReceberEstado: setEstado })
    const alarme = setTimeout(() => setSemResposta(true), ESPERA_SEM_BALCAO_MS)
    return () => {
      clearTimeout(alarme)
      conexaoRef.current?.fechar()
    }
  }, [])

  const despachar = useCallback((acaoObjeto) => conexaoRef.current?.despachar(acaoObjeto), [])

  const acoes = {
    // O cliente só é avisado quando ela marca (RN-32, D8): o mesmo reducer da
    // esteira do Balcão cuida disso, esta ponte só entrega a ação.
    avancarEsteira: (id, passo, agora, entregador) => despachar({
      tipo: acao.AVANCAR_ESTEIRA, id, passo, agora, entregador, mensagemId: novoId(), posEntregaId: novoId(),
    }),
  }

  return { estado, semBalcao: semResposta && !estado, acoes }
}
