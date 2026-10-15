import { useAtendimento } from './contextos'
import { acaoDisponivel } from './api/naoLigadas'

// #1474: a tela pergunta se mostra o botão de uma ação. No modo API, ação que só avisaria
// ("ainda não ligado") não aparece; na demonstração, tudo aparece.
export function useAcaoDisponivel() {
  const { fonteApi } = useAtendimento()
  return (nome) => acaoDisponivel(nome, { fonteApi })
}
