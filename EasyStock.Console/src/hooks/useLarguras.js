import { useCallback, useEffect, useState } from 'react'
import { escolhidasDoSalvo, largurasDaTela, migrarLargurasAntigas, padraoPorTela } from './larguras'

// Largura das colunas Balcão e Ficha, e do painel do cardápio (seção 9 da
// direção visual). Uma chave só no localStorage para as três, lida e escrita
// dentro de try/catch: o navegador pode negar o acesso (modo privado, cota
// cheia), e a tela precisa continuar de pé sem a memória.
const CHAVE = 'cdb.larguras.v2'

function ler() {
  try {
    const atual = localStorage.getItem(CHAVE)
    if (atual != null) return escolhidasDoSalvo(JSON.parse(atual))
    const migradas = migrarLargurasAntigas(JSON.parse(localStorage.getItem('cdb.larguras.v1') ?? 'null'))
    localStorage.setItem(CHAVE, JSON.stringify(migradas))
    return migradas
  } catch {
    return {}
  }
}

function gravar(escolhidas) {
  try {
    localStorage.setItem(CHAVE, JSON.stringify(escolhidas))
  } catch {
    // Sem memória: a largura escolhida vale só até o reload.
  }
}

const larguraDaJanela = () => window.innerWidth

// Um hook só, chamado uma vez lá em cima (app/App.jsx) e distribuído por
// prop: duas chamadas separadas lendo a mesma chave se pisariam na hora de
// gravar, e uma largura resetaria a outra de volta ao padrão.
// #1697: guarda só o que a pessoa arrastou; o resto acompanha o tamanho da tela (`larguras.js`).
export function useLarguras() {
  const [escolhidas, setEscolhidas] = useState(ler)
  const [tela, setTela] = useState(larguraDaJanela)

  useEffect(() => {
    const aoMudarTela = () => setTela(larguraDaJanela())
    window.addEventListener('resize', aoMudarTela)
    return () => window.removeEventListener('resize', aoMudarTela)
  }, [])

  const definir = useCallback((chave, valor) => {
    setEscolhidas((atual) => {
      const novo = { ...atual, [chave]: valor }
      gravar(novo)
      return novo
    })
  }, [])

  return { larguras: largurasDaTela(escolhidas, tela), padrao: padraoPorTela(tela), definir, tela }
}
