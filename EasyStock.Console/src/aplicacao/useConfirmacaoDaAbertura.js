import { useState } from 'react'
import { aberturaPedeConfirmacao, textoDaConfirmacaoDaAbertura } from '../dominio/aberturaDaLoja'
import { moeda } from '../dominio/formato'

// J2 (#1990), item 6: abrir o caixa com valor muito longe da sugestão pede um segundo toque. O mesmo
// gesto na aba Caixa e no "Abrir a loja": `segurar()` no primeiro toque diz se parou para perguntar;
// mudar o valor (`desfazer()`) volta a pedir.
export function useConfirmacaoDaAbertura(saldoInicial, situacao) {
  const [confirmando, setConfirmando] = useState(false)
  const pede = aberturaPedeConfirmacao(saldoInicial, situacao)
  const perguntando = confirmando && pede
  return {
    aviso: perguntando ? textoDaConfirmacaoDaAbertura(saldoInicial, situacao) : null,
    rotulo: (padrao) => (perguntando ? `Sim, abrir com ${moeda(saldoInicial)}` : padrao),
    segurar: () => {
      if (!pede || confirmando) return false
      setConfirmando(true)
      return true
    },
    desfazer: () => setConfirmando(false),
  }
}
