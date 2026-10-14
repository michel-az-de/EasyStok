import { useCallback, useState } from 'react'

// Lado, modo e largura dos painéis do Balcão (#1806), lembrados por aparelho. Uma chave por
// painel: o cardápio mora em `app/App.jsx` e o assistente na própria feature, e dois hooks
// gravando a mesma chave se pisariam (mesmo motivo de `hooks/useLarguras.js`).
// Leitura e escrita dentro de try/catch: o navegador pode negar o acesso (modo privado, cota
// cheia) ou não ter storage; a tela segue de pé com o padrão.
export const CHAVE_CARDAPIO = 'cdb.painel.cardapio.v1'
export const CHAVE_ASSISTENTE = 'cdb.painel.assistente.v1'

const armazenamentoDe = (armazenamento) => (armazenamento === undefined ? globalThis.localStorage : armazenamento)

export function lerPreferencia(chave, normalizar, armazenamento) {
  try {
    const guardado = armazenamentoDe(armazenamento).getItem(chave)
    return normalizar(guardado === null ? null : JSON.parse(guardado))
  } catch {
    return normalizar(null)
  }
}

export function gravarPreferencia(chave, preferencia, normalizar, armazenamento) {
  try {
    armazenamentoDe(armazenamento).setItem(chave, JSON.stringify(normalizar(preferencia)))
    return true
  } catch {
    // Sem memória: a escolha vale só até o reload.
    return false
  }
}

export function usePreferenciaDePainel(chave, normalizar) {
  const [preferencia, setPreferencia] = useState(() => lerPreferencia(chave, normalizar))
  const definir = useCallback((parcial) => {
    setPreferencia((atual) => {
      const nova = normalizar({ ...atual, ...parcial })
      gravarPreferencia(chave, nova, normalizar)
      return nova
    })
  }, [chave, normalizar])
  return [preferencia, definir]
}
