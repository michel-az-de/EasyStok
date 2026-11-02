import { useEffect, useRef } from 'react'
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
// pilha (o teclado não alcança um aviso de 6 s a tempo).
export function Toast({ itens, aoFechar, aoAbrir, aoPausar, aoRetomar, rotulo = 'Avisos', rodape = null }) {
  const pilha = useRef(null)
  const dentro = useRef({ ponteiro: false, foco: false })
  // Os ouvintes entram direto no nó: a região viva é só leitura para o leitor de tela, não um controle.
  useEffect(() => {
    const no = pilha.current
    if (!no) return undefined
    const mudou = (parte, valor) => {
      const antes = dentro.current.ponteiro || dentro.current.foco
      dentro.current[parte] = valor
      const agora = dentro.current.ponteiro || dentro.current.foco
      if (agora && !antes) aoPausar?.()
      if (!agora && antes) aoRetomar?.()
    }
    const entrouPonteiro = () => mudou('ponteiro', true)
    const saiuPonteiro = () => mudou('ponteiro', false)
    const entrouFoco = () => mudou('foco', true)
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
  }, [aoPausar, aoRetomar])
  // Pilha vazia: o ponteiro que estava em cima de um aviso que saiu não segura o próximo.
  const vazia = itens.length === 0
  useEffect(() => {
    if (!vazia) return
    dentro.current = { ponteiro: false, foco: false }
    aoRetomar?.()
  }, [vazia, aoRetomar])
  return (
    <section ref={pilha} className={css.pilha} role="status" aria-live="polite" aria-label={rotulo}>
      {itens.map((item) => (
        <div key={item.id} className={`${css.aviso} ${css[item.tom ?? 'marca'] ?? ''}`}>
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
          <BotaoIcone rotulo={`Fechar aviso: ${item.titulo}`} dica="Fechar" icone="x" onClick={() => aoFechar(item.id)} />
        </div>
      ))}
      {itens.length > 0 && rodape}
    </section>
  )
}
