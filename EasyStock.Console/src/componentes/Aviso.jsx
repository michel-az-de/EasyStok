import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { Botao } from './Botao'
import { BotaoIcone } from './BotaoIcone'
import { Icone } from './Icone'
import css from './Aviso.module.css'

// #2146 (plano #2130, seção 3.3): o Aviso (snackbar) que confirma o que acabou de acontecer, no pé da
// tela e centralizado. Um por vez: o novo substitui o anterior. Some sozinho em 4 s, ou 8 s quando
// traz uma ação ("Desfazer", princípio 5: desfazer em vez de "tem certeza?"); com o ponteiro ou o foco
// em cima, espera. Não é o `Toast` (a pilha de notificações do sino, que leva aonde o aviso aponta):
// a unificação dos mecanismos de feedback fica para a Onda 2.
//
//   const aviso = useAviso()
//   const id = aviso.mostrar({ texto: 'Anexo removido', tom: 'info', acao: { rotulo: 'Desfazer', aoAgir } })
//   aviso.dispensar(id) // ao sair da tela, se o aviso ainda for este
//
// As duas regiões vivas existem sempre, vazias, para o leitor de tela anunciar o que entra:
// <output> (papel `status`, educado) para info e sucesso, `alert` para erro.
export const DURACAO_DO_AVISO_MS = 4000
export const DURACAO_COM_ACAO_MS = 8000
const SAIDA_MS = 150 // --dur-curta
const semMovimento = () => globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true
// Info é o neutro do snackbar e vai sem ícone; sucesso e erro levam o ícone além da cor.
const TONS = { info: null, sucesso: 'circle-check', erro: 'alerta' }

const ContextoAviso = createContext(null)
const SEM_PROVEDOR = Object.freeze({
  mostrar: () => {
    // oxlint-disable-next-line no-console -- aviso só de desenvolvimento.
    if (import.meta.env?.DEV) console.warn('useAviso fora do ProvedorDeAviso: o aviso não aparece.')
  },
  dispensar: () => {},
})

// oxlint-disable-next-line react/only-export-components -- o hook mora com o provedor que ele lê.
export function useAviso() {
  return useContext(ContextoAviso) ?? SEM_PROVEDOR
}

export function ProvedorDeAviso({ children }) {
  const [aviso, setAviso] = useState(null)
  const contador = useRef(0)
  const relogio = useRef(null)
  const restante = useRef(0)
  const contandoPrazo = useRef(null) // id do aviso que ainda conta prazo; null sem aviso ou com ele já saindo

  // Com `id`, só dispensa se aquele aviso ainda é o da tela: quem saiu não apaga o aviso de outro nem para
  // o relógio dele. Por isso o id é conferido aqui, antes do clearTimeout, e não na função de atualização.
  // #2762 (A5): dispensar não desmonta na hora: marca `saindo` e o aviso fica --dur-curta mais, só para
  // sumir suavemente (inert e aria-hidden, sem clique nem foco). Sem movimento reduzido sai direto.
  const dispensar = useCallback((id) => {
    if (contandoPrazo.current === null || (id !== undefined && contandoPrazo.current !== id)) return
    contandoPrazo.current = null
    clearTimeout(relogio.current)
    setAviso((atual) => {
      if (!atual || atual.saindo) return atual
      return semMovimento() ? null : { ...atual, saindo: true }
    })
  }, [])
  const saindoId = aviso?.saindo ? aviso.id : null
  useEffect(() => {
    if (saindoId === null) return undefined
    const prazo = setTimeout(() => setAviso((atual) => (atual?.id === saindoId ? null : atual)), SAIDA_MS)
    return () => clearTimeout(prazo)
  }, [saindoId])

  const armar = useCallback((id, duracao) => {
    clearTimeout(relogio.current)
    restante.current = duracao
    relogio.current = setTimeout(() => dispensar(id), duracao)
  }, [dispensar])

  const mostrar = useCallback(({ texto, tom = 'info', acao = null, duracao }) => {
    contador.current += 1
    const id = contador.current
    contandoPrazo.current = id
    setAviso({ id, texto, tom: tom in TONS ? tom : 'info', acao })
    armar(id, duracao ?? (acao ? DURACAO_COM_ACAO_MS : DURACAO_DO_AVISO_MS))
    return id
  }, [armar])

  useEffect(() => () => clearTimeout(relogio.current), [])

  const valor = useMemo(() => ({ mostrar, dispensar }), [mostrar, dispensar])
  const pausar = () => clearTimeout(relogio.current)
  const retomar = () => { if (aviso && !aviso.saindo) armar(aviso.id, restante.current) }
  // #2762 (A14): Desfazer e Fechar desmontam o botão focado; o foco vai para a reserva da tela em vez de cair
  // no body. Antes do `aoAgir`, que pode escolher outro destino.
  const devolverFoco = (evento) => {
    if (evento.currentTarget.closest('[data-aviso]')?.contains(document.activeElement)) document.querySelector('[data-foco-reserva]')?.focus()
  }

  const corpo = aviso && (
    // oxlint-disable-next-line jsx-a11y/no-static-element-interactions -- Hover e foco só pausam o prazo; a ação é o botão.
    <div
      key={aviso.id}
      className={`${css.aviso} ${css[aviso.tom] ?? ''} ${aviso.saindo ? css.saindo : ''}`}
      data-aviso={aviso.saindo ? undefined : aviso.tom}
      data-aviso-saindo={aviso.saindo || undefined}
      inert={aviso.saindo || undefined}
      aria-hidden={aviso.saindo || undefined}
      onPointerEnter={pausar}
      onPointerLeave={retomar}
      onFocus={pausar}
      onBlur={retomar}
    >
      {TONS[aviso.tom] && <Icone nome={TONS[aviso.tom]} tamanho={20} className={css.icone} />}
      <p className={css.texto}>{aviso.texto}</p>
      {aviso.acao && (
        <Botao variante="texto" className={css.acao} onClick={(evento) => { devolverFoco(evento); dispensar(aviso.id); aviso.acao.aoAgir() }}>
          {aviso.acao.rotulo}
        </Botao>
      )}
      <BotaoIcone rotulo="Fechar aviso" icone="fechar" className={css.fechar} onClick={(evento) => { devolverFoco(evento); dispensar(aviso.id) }} />
    </div>
  )

  return (
    <ContextoAviso.Provider value={valor}>
      {children}
      <div className={css.pe}>
        <output aria-live="polite" className={css.regiao}>{aviso?.tom !== 'erro' && corpo}</output>
        <div role="alert" className={css.regiao}>{aviso?.tom === 'erro' && corpo}</div>
      </div>
    </ContextoAviso.Provider>
  )
}
