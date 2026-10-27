import { useEffect, useRef, useState } from 'react'
import { dataIsoNoFuso } from '../dominio/formato'
import {
  ESPERA_ENTRE_FOLHAS_MS, INTERVALO_DA_FILA_MS, NOME_DA_TRAVA, pendentesParaImprimir, somarImpressa,
} from '../dominio/impressaoAutomatica'
import { DOCUMENTO_COZINHA } from '../dominio/impressos'
import {
  confirmarImpressao, listarImpressoesPendentes, obterCanhotoParaImprimir, registrarFalhaImpressao,
} from '../infra/api/kdsApi'
import {
  gravarContagemAutomatica, lerContagemAutomatica, lerPreferenciasImpressao, ouvirPreferenciasImpressao,
} from '../infra/preferenciasImpressao'

// Impressão automática do canhoto (#2096). Ligada em Configurações › Impressão, NESTE aparelho:
// a aba que segura a trava lê a fila de impressão da API a cada 10 s, imprime cada pendente nova
// no papel do canhoto de cozinha das preferências e confirma `impressa` (ou registra `falhou`).
// `imprimir(html, { sinal })` chega de quem monta (o quadro oculto de `componentes/htmlNoNavegador`).
// Cada pendente é tentada uma vez por visita: nunca sai duas vezes a mesma folha.
const esperar = (ms, sinal) => new Promise((ok) => {
  const t = setTimeout(ok, ms)
  sinal.addEventListener('abort', () => { clearTimeout(t); ok() }, { once: true })
})

const hoje = () => dataIsoNoFuso(Date.now())

export function useImpressaoAutomatica({ imprimir }) {
  const [preferencias, setPreferencias] = useState(lerPreferenciasImpressao)
  const [contagem, setContagem] = useState(() => lerContagemAutomatica(hoje()))
  const [nestaAba, setNestaAba] = useState(false)
  const [falhas, setFalhas] = useState(0)
  const tentadas = useRef(new Set())
  const imprimirRef = useRef(imprimir)
  useEffect(() => { imprimirRef.current = imprimir }, [imprimir])

  useEffect(() => ouvirPreferenciasImpressao(() => {
    setPreferencias(lerPreferenciasImpressao())
    // A contagem também vem da outra aba que imprimiu.
    setContagem(lerContagemAutomatica(hoje()))
  }), [])

  const ligada = preferencias.canhotoAutomatico
  const desde = preferencias.canhotoAutomaticoDesde

  useEffect(() => {
    if (!ligada) return undefined
    const controle = new AbortController()
    const { signal: sinal } = controle

    const imprimirUma = async (item) => {
      tentadas.current.add(item.id)
      const atuais = lerPreferenciasImpressao()
      try {
        const html = await obterCanhotoParaImprimir(item.pedidoId, {
          modelo: atuais.papeis[DOCUMENTO_COZINHA], logoTermica: atuais.logoTermica,
        }, sinal)
        if (sinal.aborted) return
        await imprimirRef.current(html, { sinal })
      } catch (erro) {
        if (sinal.aborted) return
        setFalhas((n) => n + 1)
        await registrarFalhaImpressao(item.id, erro?.message).catch(() => {})
        return
      }
      const proxima = somarImpressa(lerContagemAutomatica(hoje()), hoje())
      gravarContagemAutomatica(proxima)
      setContagem(proxima)
      // A folha já saiu: se a confirmação falhar, a cozinha vê na gaveta e a próxima leitura não reimprime.
      await confirmarImpressao(item.id).catch(() => {})
    }

    const trabalhar = async () => {
      setNestaAba(true)
      while (!sinal.aborted) {
        try {
          const lista = await listarImpressoesPendentes()
          for (const item of pendentesParaImprimir(lista, { tentadas: tentadas.current, desde })) {
            if (sinal.aborted) break
            await imprimirUma(item)
            await esperar(ESPERA_ENTRE_FOLHAS_MS, sinal)
          }
        } catch { /* fila fora (rede, perfil): tenta de novo na próxima volta */ }
        await esperar(INTERVALO_DA_FILA_MS, sinal)
      }
      setNestaAba(false)
    }

    // Uma aba imprime; as outras esperam a trava e assumem quando aquela fecha.
    const travas = globalThis.navigator?.locks
    if (travas?.request) travas.request(NOME_DA_TRAVA, { signal: sinal }, trabalhar).catch(() => {})
    else trabalhar()
    return () => controle.abort()
  }, [ligada, desde])

  const contagemDeHoje = contagem.dia === hoje() ? contagem.total : 0
  return { ligada, nestaAba, contagem: contagemDeHoje, falhas }
}
