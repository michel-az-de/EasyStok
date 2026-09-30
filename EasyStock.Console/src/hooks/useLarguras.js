import { useCallback, useEffect, useState } from 'react'

// Largura das colunas Balcão e Ficha, e do painel do cardápio (seção 9 da
// direção visual). Uma chave só no localStorage para as três, lida e escrita
// dentro de try/catch: o navegador pode negar o acesso (modo privado, cota
// cheia), e a tela precisa continuar de pé sem a memória.
const CHAVE = 'cdb.larguras.v1'

// Ficha em 420 (rodada 4): 320 espremia o nome do prato a 0 px de largura com
// a alça de arrastar e o contador na mesma linha (achado do corte na coluna).
// Balcão em 360 (rodada 5, seção 1): os chips de canal cabem numa linha só a
// partir de 336 px úteis.
export const PADRAO = { balcao: 360, ficha: 420, cardapio: 400 }

function ler() {
  try {
    const bruto = localStorage.getItem(CHAVE)
    if (!bruto) return { ...PADRAO }
    const salvo = JSON.parse(bruto)
    return { ...PADRAO, ...salvo }
  } catch {
    return { ...PADRAO }
  }
}

function gravar(larguras) {
  try {
    localStorage.setItem(CHAVE, JSON.stringify(larguras))
  } catch {
    // Sem memória: a largura escolhida vale só até o reload.
  }
}

// Um hook só, chamado uma vez lá em cima (app/App.jsx) e distribuído por
// prop: duas chamadas separadas lendo a mesma chave se pisariam na hora de
// gravar, e uma largura resetaria a outra de volta ao padrão.
export function useLarguras() {
  const [larguras, setLarguras] = useState(ler)

  useEffect(() => { gravar(larguras) }, [larguras])

  const definir = useCallback((chave, valor) => {
    setLarguras((atual) => ({ ...atual, [chave]: valor }))
  }, [])

  return { larguras, definir }
}
