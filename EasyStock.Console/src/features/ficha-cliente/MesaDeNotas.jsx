import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react'
import { useAtendimento } from '../../aplicacao/contextos'
import { EVENTO_SESSAO_ALTERADA, guardarReferenciasDaMesa, identidadeAtualDaMesa, identidadeDaMesa, idNotaNaMesa,
  lerReferenciasDaMesa, obterNotaDaMesa } from '../../aplicacao/mesaDeNotas'
import { PainelMovel, usePainelMovel } from '../../componentes/PainelMovel'
import { chavePainelMovel, guardarPainelMovel, lerPainelMovel } from '../../hooks/estadoPainelMovel'
import { lerPosicaoPostit } from '../../aplicacao/posicoesDosPostits'
import css from './NotasDoCliente.module.css'
import { focarNotasDaConversa } from './focarCampoDaFicha'

const ContextoMesa = createContext(null)
export const useMesaDeNotas = () => useContext(ContextoMesa)

function NotaNaMesa({ escopo, registro, aoRecolher }) {
  const controle = usePainelMovel(escopo, `nota:${registro.clienteId}:${registro.nota.id}`)
  const soltar = controle.soltar
  const solto = controle.solto
  const origem = useRef(registro.posicao ?? {})
  useEffect(() => { if (!solto) soltar(origem.current) }, [soltar, solto])
  const controleComFoco = { ...controle, devolverFoco: () => controle.devolverFoco?.() || focarNotasDaConversa(registro.conversaId) }
  return <PainelMovel controle={controleComFoco} titulo={`Nota · ${registro.clienteNome}`} larguraInicial={280}
    className={css.solto} classeCorpo={css.corpo} focar={registro.focar === true}
    atributos={{ 'data-postit-solto': registro.nota.id, 'data-cliente-nota': registro.clienteId }} aoRecolher={aoRecolher}>
    <p className={css.texto}>{registro.nota.texto}</p>
    <small>{[registro.nota.autor, registro.nota.em].filter(Boolean).join(' · ')}</small>
  </PainelMovel>
}

function MesaDaSessao({ children, escopo, identidade, fonteApi, conversas, selecionadaId }) {
  const dadosLocais = fonteApi ? SEM_CONVERSAS : conversas
  const [notas, setNotas] = useState({})
  const notasAtuais = useRef(notas)
  useEffect(() => { notasAtuais.current = notas }, [notas])
  useEffect(() => {
    setNotas(antes => {
      let proximo = antes
      for (const [id, registro] of Object.entries(antes)) {
        if (registro.focar) { if (proximo === antes) proximo = { ...antes }; proximo[id] = { ...registro, focar: false } }
      }
      return proximo
    })
  }, [selecionadaId])
  const [referencias, setReferencias] = useState(() => lerReferenciasDaMesa(escopo))
  const referenciasAtuais = useRef(referencias)
  const [erro, setErro] = useState(null)
  const [tentativa, setTentativa] = useState(0)
  const [liberada, setLiberada] = useState(true)
  const alterarReferencias = useCallback((fn) => {
    const proximo = fn(referenciasAtuais.current)
    referenciasAtuais.current = proximo; setReferencias(proximo)
    if (!guardarReferenciasDaMesa(escopo, proximo)) setErro('As posições não puderam ser guardadas neste navegador.')
  }, [escopo])
  const recolher = useCallback((clienteId, notaId) => {
    const id = idNotaNaMesa(clienteId, notaId)
    const painelId = `nota:${clienteId}:${notaId}`
    const posicao = lerPainelMovel(escopo, painelId)
    if (posicao) guardarPainelMovel(escopo, painelId, { ...posicao, solto: false })
    setNotas(antes => { const copia = { ...antes }; delete copia[id]; return copia })
    alterarReferencias(antes => antes.filter(r => idNotaNaMesa(r.clienteId, r.notaId) !== id))
  }, [alterarReferencias, escopo])
  const soltar = useCallback((registro, eventoOuPosicao = {}) => {
    if (!liberada || !registro?.nota?.id || !registro.clienteId || !registro.conversaId || !registro.clienteNome) return
    const r = eventoOuPosicao?.currentTarget?.getBoundingClientRect?.()
    const posicao = r ? { x: r.left, y: r.top } : eventoOuPosicao
    const id = idNotaNaMesa(registro.clienteId, registro.nota.id)
    setNotas(antes => ({ ...antes, [id]: { ...registro, posicao, focar: true } }))
    alterarReferencias(antes => [...antes.filter(r => idNotaNaMesa(r.clienteId, r.notaId) !== id),
      { clienteId: registro.clienteId, conversaId: registro.conversaId, notaId: registro.nota.id }])
  }, [alterarReferencias, liberada])
  const atualizar = useCallback((clienteId, clienteNome, recebidas, conversaId) => {
    if (!liberada) return
    const restauradas = []
    if (clienteId && clienteNome && conversaId) {
      for (const nota of recebidas) {
        if (!nota.id) continue
        const painelId = `nota:${clienteId}:${nota.id}`
        let posicao = lerPainelMovel(escopo, painelId)
        if (!posicao) {
          const legada = lerPosicaoPostit(conversaId, nota.id)
          if (legada && guardarPainelMovel(escopo, painelId, { ...legada, solto: true })) posicao = { ...legada, solto: true }
        }
        if (posicao?.solto) restauradas.push({ clienteId, clienteNome, conversaId, nota, focar: false })
      }
    }
    const novas = restauradas.filter(n => !referenciasAtuais.current.some(r => r.clienteId === n.clienteId && r.notaId === n.nota.id))
    if (novas.length) alterarReferencias(antes => [...antes, ...novas.map(n => ({ clienteId: n.clienteId, conversaId: n.conversaId, notaId: n.nota.id }))])
    setNotas(antes => {
      let proximo = antes
      for (const restaurada of restauradas) {
        const id = idNotaNaMesa(restaurada.clienteId, restaurada.nota.id)
        if (!antes[id]) { if (proximo === antes) proximo = { ...antes }; proximo[id] = restaurada }
      }
      for (const nota of recebidas) {
        const id = idNotaNaMesa(clienteId, nota.id), atual = antes[id]
        if (atual && (atual.nota !== nota || atual.clienteNome !== clienteNome)) {
          if (proximo === antes) proximo = { ...antes }
          proximo[id] = { ...atual, nota, clienteNome }
        }
      }
      return proximo
    })
  }, [alterarReferencias, escopo, liberada])

  useEffect(() => {
    const controle = new AbortController()
    let ativo = true
    const sessaoMudou = () => {
      if (fonteApi && identidadeAtualDaMesa() !== identidade) {
        ativo = false; controle.abort(); setNotas({}); setLiberada(false)
      }
    }
    window.addEventListener(EVENTO_SESSAO_ALTERADA, sessaoMudou)
    async function restaurar() {
      const pendentes = referenciasAtuais.current.filter(r => r.conversaId === selecionadaId
        && !Object.hasOwn(notasAtuais.current, idNotaNaMesa(r.clienteId, r.notaId)))
      for (let i = 0; i < pendentes.length && ativo; i += 4) {
        await Promise.all(pendentes.slice(i, i + 4).map(async r => {
          try {
            let registro
            if (fonteApi) registro = await obterNotaDaMesa(r.clienteId, r.notaId, { sinal: controle.signal, identidadeEsperada: identidade })
            else {
              const conversa = dadosLocais.find(c => c.id === r.conversaId && (c.clienteId ?? c.id) === r.clienteId)
              const nota = conversa?.cliente?.notas?.find(n => n.id === r.notaId)
              if (nota) registro = { clienteId: r.clienteId, clienteNome: conversa.nome, nota }
            }
            if (ativo && registro && referenciasAtuais.current.some(atual => atual.clienteId === r.clienteId && atual.notaId === r.notaId)) {
              setNotas(antes => Object.hasOwn(antes, idNotaNaMesa(r.clienteId, r.notaId)) ? antes
                : { ...antes, [idNotaNaMesa(r.clienteId, r.notaId)]: { ...registro, conversaId: r.conversaId, focar: false } })
            }
          } catch (falha) {
            if (!ativo || controle.signal.aborted) return
            if ([403, 404].includes(falha.status)) recolher(r.clienteId, r.notaId)
            else setErro('Algumas notas não puderam ser restauradas. Tente novamente.')
          }
        }))
      }
    }
    restaurar()
    return () => { ativo = false; controle.abort(); window.removeEventListener(EVENTO_SESSAO_ALTERADA, sessaoMudou) }
  }, [escopo, fonteApi, identidade, tentativa, recolher, dadosLocais, selecionadaId])

  return <ContextoMesa.Provider value={{ soltar, recolher, atualizar, escopo, identidade,
    chaveDeFoco: (clienteId, notaId) => chavePainelMovel(escopo, `nota:${clienteId}:${notaId}`),
    estaSolta: (clienteId, notaId) => Object.hasOwn(notas, idNotaNaMesa(clienteId, notaId))
      && notas[idNotaNaMesa(clienteId, notaId)].conversaId === selecionadaId }}>
    {children}
    {liberada && Object.entries(notas).filter(([, registro]) => registro.conversaId === selecionadaId).map(([id, registro]) => <NotaNaMesa key={id} escopo={escopo} registro={registro}
      aoRecolher={() => recolher(registro.clienteId, registro.nota.id)} />)}
    {liberada && erro && <output className={css.avisoMesa}>{erro} <button type="button" onClick={() => { setErro(null); setTentativa(n => n + 1) }}>Tentar novamente</button></output>}
  </ContextoMesa.Provider>
}

const SEM_CONVERSAS = []
export function MesaDeNotas({ children }) {
  const { sessao, fonteApi, selecionada, conversas = SEM_CONVERSAS } = useAtendimento()
  const identidade = identidadeDaMesa(sessao)
  if (fonteApi && !identidade) return children
  const escopo = fonteApi ? `sessao:${identidade}` : 'demonstracao'
  return <MesaDaSessao key={escopo} escopo={escopo} identidade={identidade} fonteApi={fonteApi} conversas={conversas}
    selecionadaId={selecionada?.id ?? null}>{children}</MesaDaSessao>
}
