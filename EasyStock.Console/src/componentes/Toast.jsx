import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { BotaoIcone } from './BotaoIcone'
import { Icone } from './Icone'
import css from './Toast.module.css'

// #1807: pilha de avisos curtos no canto da tela (toast). A região viva existe sempre, mesmo
// vazia, para o leitor de tela anunciar o que entra (`role="status"`, educado: não interrompe).
// Quem decide quanto tempo cada aviso fica é a fila (`aplicacao/useFilaDeAvisos.js`); aqui só o
// desenho, o X e o clique que leva aonde o aviso aponta.
//
// itens: [{ id, titulo, corpo, icone?, tom?: 'marca' | 'sucesso' | 'alerta' | 'info', abrivel? }]
// #2185 (F28): `aoPausar` e `aoRetomar` seguram o relógio dos avisos enquanto o ponteiro ou o foco estão na
// pilha (o teclado não alcança um aviso de 6 s a tempo). Foco de clique não conta: o mouse já pausa pelo
// ponteiro, e um clique deixaria o foco na pilha, segurando os avisos depois de o ponteiro sair.
const SAIDA_MS = 150 // --dur-curta
const semMovimento = () => globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true

export function Toast({ itens, aoFechar, aoAbrir, aoPausar, aoRetomar, rotulo = 'Avisos', rodape = null }) {
  const pilha = useRef(null)
  // #2762 (A5): o aviso que saiu da fila fica mais --dur-curta, no mesmo lugar, só para sumir suavemente. É
  // cópia visual: `inert` e `aria-hidden`, sem clique nem foco. A fila do dono (`itens`) já está atualizada.
  const [saindo, setSaindo] = useState([])
  const anteriores = useRef(itens)
  const prazos = useRef(new Set())
  useLayoutEffect(() => {
    const antes = anteriores.current
    anteriores.current = itens
    if (semMovimento()) return
    const ficam = new Set(itens.map((item) => item.id))
    const idas = antes.map((item, indice) => ({ item, indice })).filter(({ item }) => !ficam.has(item.id))
    if (!idas.length) return
    setSaindo((atual) => [...atual, ...idas])
    const prazo = setTimeout(() => {
      prazos.current.delete(prazo)
      setSaindo((atual) => atual.filter((s) => !idas.includes(s)))
    }, SAIDA_MS)
    prazos.current.add(prazo)
  }, [itens])
  useEffect(() => { const todos = prazos.current; return () => todos.forEach(clearTimeout) }, [])
  const desenhados = itens.map((item) => ({ item, saindo: false }))
  for (const { item, indice } of [...saindo].sort((a, b) => a.indice - b.indice)) {
    if (!desenhados.some((d) => d.item.id === item.id)) desenhados.splice(Math.min(indice, desenhados.length), 0, { item, saindo: true })
  }
  // #2762 (A14): fechar pelo X desmonta o botão focado; o foco vai para a reserva da tela em vez de cair no body.
  const fechar = (evento, id) => {
    if (evento.currentTarget.closest('[data-toast]')?.contains(document.activeElement)) document.querySelector('[data-foco-reserva]')?.focus()
    aoFechar(id)
  }
  const dentro = useRef({ ponteiro: false, foco: false })
  const mudou = useCallback((parte, valor) => {
    const antes = dentro.current.ponteiro || dentro.current.foco
    dentro.current[parte] = valor
    const agora = dentro.current.ponteiro || dentro.current.foco
    if (agora && !antes) aoPausar?.()
    if (!agora && antes) aoRetomar?.()
  }, [aoPausar, aoRetomar])
  // Os ouvintes entram direto no nó: a região viva é só leitura para o leitor de tela, não um controle.
  useEffect(() => {
    const no = pilha.current
    if (!no) return undefined
    const entrouPonteiro = () => mudou('ponteiro', true)
    const saiuPonteiro = () => mudou('ponteiro', false)
    const entrouFoco = (evento) => { if (evento.target.matches?.(':focus-visible')) mudou('foco', true) }
    const saiuFoco = (evento) => { if (!no.contains(evento.relatedTarget)) mudou('foco', false) }
    no.addEventListener('pointerenter', entrouPonteiro)
    no.addEventListener('pointerleave', saiuPonteiro)
    no.addEventListener('focusin', entrouFoco)
    no.addEventListener('focusout', saiuFoco)
    return () => {
      no.removeEventListener('pointerenter', entrouPonteiro)
      no.removeEventListener('pointerleave', saiuPonteiro)
      no.removeEventListener('focusin', entrouFoco)
      no.removeEventListener('focusout', saiuFoco)
    }
  }, [mudou])
  // O botão focado saiu com o aviso: nem todo navegador avisa com focusout.
  useEffect(() => {
    if (dentro.current.foco && !pilha.current?.contains(document.activeElement)) mudou('foco', false)
  }, [itens, mudou])
  // Pilha vazia: o ponteiro que estava em cima de um aviso que saiu não segura o próximo.
  const vazia = itens.length === 0
  useEffect(() => {
    if (!vazia) return
    dentro.current = { ponteiro: false, foco: false }
    aoRetomar?.()
  }, [vazia, aoRetomar])
  return (
    <section ref={pilha} className={css.pilha} role="status" aria-live="polite" aria-label={rotulo}>
      {desenhados.map(({ item, saindo: indo }) => (
        <div key={item.id} data-toast className={`${css.aviso} ${css[item.tom ?? 'marca'] ?? ''} ${indo ? css.saindo : ''}`}
          inert={indo || undefined} aria-hidden={indo || undefined}>
          {item.icone && <span className={css.icone} aria-hidden="true"><Icone nome={item.icone} tamanho={20} /></span>}
          {item.abrivel && aoAbrir ? (
            <button type="button" className={css.texto} onClick={() => aoAbrir(item)}>
              <strong>{item.titulo}</strong>
              <span>{item.corpo}</span>
            </button>
          ) : (
            <p className={css.texto}>
              <strong>{item.titulo}</strong>
              <span>{item.corpo}</span>
            </p>
          )}
          <BotaoIcone rotulo={`Fechar aviso: ${item.titulo}`} dica="Fechar" icone="x" onClick={(evento) => fechar(evento, item.id)} />
        </div>
      ))}
      {itens.length > 0 && rodape}
    </section>
  )
}
