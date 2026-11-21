import { useEffect, useMemo, useState } from 'react'
import { ORIENTACAO_DA_PRODUCAO, PAPEL_DA_PRODUCAO, camposDaEtiqueta, lerDesenho } from '../dominio/etiquetaDeProducao.js'
import { PONTOS_MM, carregarFonteDoMotor, desenharNoMotor, fonteDoMotorPronta } from '../infra/motorEtiqueta.js'

// #2247 (impressão v3, G1 item 7): a etiqueta de produção desenhada pelo motor do app.
// Devolve { png, larguraMm, alturaMm, recusa } ou { carregando: true } enquanto a fonte chega;
// sem etiqueta, { png: null, recusa: null }.
// Fonte que não carrega vira recusa com o motivo, nunca uma etiqueta com outra fonte.
export function useEtiquetaDoMotor(etiqueta, empresa) {
  const [fonte, setFonte] = useState(fonteDoMotorPronta)
  useEffect(() => {
    if (fonte) return undefined
    let viva = true
    carregarFonteDoMotor().then(() => { if (viva) setFonte(true) })
    return () => { viva = false }
  }, [fonte])
  return useMemo(() => {
    if (!etiqueta) return { png: null, recusa: null }
    if (!fonte) return { carregando: true }
    const campos = camposDaEtiqueta({ etiqueta, empresa })
    return lerDesenho(campos, desenharNoMotor({ campos, papel: PAPEL_DA_PRODUCAO, orientacao: ORIENTACAO_DA_PRODUCAO }), PONTOS_MM)
  }, [fonte, etiqueta, empresa])
}
