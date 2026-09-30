// Ponte da janela de Entregas (`features/entregas/TelaEntregas.jsx`) até o
// Balcão, por `infra/canalEntreJanelas.js` (seção 6, passo zero). Mora na
// camada `aplicacao` (que pode importar `infra`) e não em `features`
// (que não pode, `ferramentas/verificar-camadas.mjs`): é o mesmo motivo de
// `aplicacao/useAvisoSonoro.js` já morar aqui em vez de `hooks/`.
//
// `criarAcoesEntregas` (o arquivo da F6 em `acoes/entregas.js`) já é uma
// função pura de `despachar`: chamando ela de novo aqui, com o `despachar`
// do espelho no lugar do `dispatch` do reducer, a janela de Entregas ganha
// os MESMOS criadores de ação do Balcão sem duplicar lógica nenhuma. Só
// `avancarEsteira`, `trocarJanela`, `cancelarPedido`, `marcarEstorno` e
// `selecionar` não têm arquivo próprio (nascem direto dentro do Provider,
// que a F6 nunca edita) — por isso ganham um adaptador pequeno aqui, no
// mesmo formato de ação que `AtendimentoProvider.jsx` já usa.
import {
  useCallback, useEffect, useState,
} from 'react'
import { conectarEspelho } from '../infra/canalEntreJanelas'
import * as acao from './acoes'
import { criarAcoesEntregas } from './acoes/entregas'
import { criarAcoesIntegracoes } from './acoes/integracoes'

const ESPERA_SEM_BALCAO_MS = 1000
const novoId = () => crypto.randomUUID()

export function useEspelhoDeEntregas() {
  const [estado, setEstado] = useState(null)
  const [semResposta, setSemResposta] = useState(false)
  // Estado, não ref: a conexão É o resultado de sincronizar com um sistema
  // externo (o canal entre janelas) dentro do efeito abaixo — exatamente o
  // caso que `useState` cobre. Guardar em `ref` fazia `despachar` ler
  // `.current` para funcionar, e passar esse `despachar` para as fábricas de
  // ação (`criarAcoesEntregas`/`criarAcoesIntegracoes`) contava como "ler ref
  // durante o render" para o linter, mesmo a leitura só acontecendo quando a
  // ação é de fato disparada. Com estado, `despachar` fecha sobre `conexao`
  // como qualquer outro valor de render.
  const [conexao, setConexao] = useState(null)

  useEffect(() => {
    const conectada = conectarEspelho({ aoReceberEstado: setEstado })
    setConexao(conectada)
    const alarme = setTimeout(() => setSemResposta(true), ESPERA_SEM_BALCAO_MS)
    return () => {
      clearTimeout(alarme)
      conectada?.fechar()
    }
  }, [])

  const despachar = useCallback((acaoObjeto) => conexao?.despachar(acaoObjeto), [conexao])

  const acoes = {
    ...criarAcoesEntregas(despachar),
    ...criarAcoesIntegracoes(despachar),
    avancarEsteira: (id, passo, agora, entregador) => despachar({
      tipo: acao.AVANCAR_ESTEIRA, id, passo, agora, entregador, mensagemId: novoId(), posEntregaId: novoId(),
    }),
    cancelarPedido: (id, texto, agora) => despachar({
      tipo: acao.CANCELAR_PEDIDO, id, texto, agora, mensagemId: novoId(),
    }),
    marcarEstorno: (id, agora) => despachar({ tipo: acao.MARCAR_ESTORNO, id, agora }),
    selecionar: (id) => {
      despachar({ tipo: acao.SELECIONAR_CONVERSA, id })
      window.opener?.focus()
    },
  }

  return { estado, semBalcao: semResposta && !estado, acoes }
}
