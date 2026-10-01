// Ponte da janela do Cardápio por link (`features/cardapio-link/TelaCardapioLink.jsx`)
// até o Balcão, por `infra/canalEntreJanelas.js` — mesmo desenho de
// `useEspelhoDeEntregas.js` (rodada 5, seção 6, usado como modelo aqui).
// Mora em `aplicacao` (que pode importar `infra`) e não em `features`
// (regra de `ferramentas/verificar-camadas.mjs`).
import { useCallback, useEffect, useRef, useState } from 'react'
import { conectarEspelho } from '../infra/canalEntreJanelas'
import { FONTE_API } from '../infra/fonteDados'
import * as acao from './acoes'
import { criarAcoesCardapioLink } from './acoes/cardapioLink'

const ESPERA_SEM_BALCAO_MS = 1000
const novoId = () => crypto.randomUUID()

export function useEspelhoCardapioLink() {
  const [estado, setEstado] = useState(null)
  const [semResposta, setSemResposta] = useState(false)
  const conexaoRef = useRef(null)

  useEffect(() => {
    // Modo API (F06): sem canal entre janelas. O Balcão não publica estado e a página
    // mostra a própria faixa de "sem Balcão" em vez de mandar ação por fora da API.
    conexaoRef.current = FONTE_API ? null : conectarEspelho({ aoReceberEstado: setEstado })
    const alarme = setTimeout(() => setSemResposta(true), ESPERA_SEM_BALCAO_MS)
    return () => {
      clearTimeout(alarme)
      conexaoRef.current?.fechar()
    }
  }, [])

  const despachar = useCallback((acaoObjeto) => conexaoRef.current?.despachar(acaoObjeto), [])

  const acoes = {
    ...criarAcoesCardapioLink(despachar),
    // Pagamento simulado (Pix e cartão) reaproveita CONFIRMAR_PAGAMENTO, o
    // mesmo caminho que a Ficha já usa para reconhecer dinheiro: mesmo som,
    // mesmo sinal verde, mesma esteira, sem duplicar regra nenhuma aqui.
    confirmarPagamento: (id, agora) => despachar({
      tipo: acao.CONFIRMAR_PAGAMENTO, id, agora, mensagemId: novoId(),
    }),
  }

  return { estado, semBalcao: semResposta && !estado, acoes }
}
