import { useEffect, useState } from 'react'
import { FONTE_API } from '../infra/fonteDados'
import { pixDiretoDisponivel } from '../infra/api/pixDiretoApi'

// #2233: o chip "Pix na chave da loja" só aparece com a API dizendo que está disponível (flag
// `pagamento.pix_direto` do tenant e loja com chave). Uma leitura por carga da página; falha vale
// "indisponível", que é o console de antes. Ligar a flag depois pede recarregar a página.
let leitura = null

export function usePixDireto() {
  const [disponivel, setDisponivel] = useState(false)
  useEffect(() => {
    if (!FONTE_API) return undefined
    let vivo = true
    leitura ??= pixDiretoDisponivel().catch(() => { leitura = null; return false })
    leitura.then((valor) => { if (vivo) setDisponivel(valor) })
    return () => { vivo = false }
  }, [])
  return disponivel
}
