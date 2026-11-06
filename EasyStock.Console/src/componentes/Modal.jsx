import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { Botao } from './Botao'
import { Icone } from './Icone'
import css from './Modal.module.css'

// Camada sobreposta. Usa <dialog>.showModal(), que entrega trap de foco e
// fundo inerte pelo próprio navegador, em vez de imitar isso na mão.
// `largura` é opcional (F2, ModalHistorico pede 880px): sem ela, o CSS padrão
// de 560px de `Modal.module.css` continua valendo para todo o resto da tela.
//
// `aoFechar` fica numa ref (rodada 8, defeito 65 "fora do escopo"): o efeito
// que chama showModal() só roda no mount, nunca de novo quando o pai
// re-renderiza e recria a função (ex.: `fechar` inline em App.jsx a cada
// tecla digitada). Antes, `[aoFechar]` nas deps fechava e chamava
// showModal() de novo a cada identidade nova, e showModal() sobe o dialog
// para o topo da pilha nativa (top layer, não CSS/z-index). Com um modal
// aberto de dentro deste (galeria → forma da peça, histórico → resumo do
// encerramento), o de fora reassumia o topo e escondia o de dentro, que
// seguia aberto e funcional, só invisível. Chamando showModal() uma vez só,
// o de fora nunca mais disputa o topo depois que o de dentro abre: o de
// dentro fica por cima, sai com o foco que showModal() já dá de graça, e Esc
// fecha só ele. A pilha de close-watcher nativa do navegador segue a mesma
// ordem do top layer.
//
// #2078 (B11/E4), dois ajustes opt-in para os modais de dinheiro, sem mudar os outros:
//   `focarCampo`: o foco vai para o campo marcado com `data-foco-inicial` (ou o primeiro campo), não
//     para o X, que o showModal() escolhe por ser o primeiro botão. Sem rolar a página.
//   `segurarFechamento`: com valor digitado, Esc e clique no fundo não fecham (perdiam o que ela
//     contou). O X e o Cancelar do rodapé continuam fechando: são gestos de propósito.
//
// #2258 (auditoria F4, T17): `confirmarDescarte` é o "Descartar alterações?" do sistema inteiro. Com
//   dado digitado, Esc, clique no fundo e o X não fecham calados nem descartam calados: o modal pergunta
//   ali mesmo, e o foco vai para "Continuar editando". Esc na pergunta volta para a edição.
//   `segurarFechamento` segue para o caso em que nem descartar pode (envio em andamento).
export function Modal({
  titulo, descricao, aoFechar, rodape, largura, className, focarCampo = false, segurarFechamento = false, confirmarDescarte = false, children,
}) {
  const id = useId()
  const dialogoRef = useRef(null)
  // Capture antes do commit: um campo com autoFocus já terá foco quando o efeito rodar.
  const focoAnterior = useRef(typeof document === 'undefined' ? null : document.activeElement)
  const aoFecharRef = useRef(aoFechar)
  const segurarRef = useRef(segurarFechamento)
  const descarteRef = useRef(confirmarDescarte)
  const [perguntando, setPerguntando] = useState(false)
  const perguntandoRef = useRef(false)
  // #2258 (banca a11y): a pergunta guarda o campo que estava em edição; "Continuar editando" e Esc
  //   devolvem o foco a ele, em vez de deixá-lo cair no body quando a pergunta some.
  const campoAntesDaPergunta = useRef(null)
  const abrirPergunta = () => {
    const ativo = typeof document === 'undefined' ? null : document.activeElement
    campoAntesDaPergunta.current = ativo?.matches?.('input, textarea, select, [contenteditable="true"]') ? ativo : null
    setPerguntando(true)
  }
  const voltarAEdicao = () => {
    setPerguntando(false)
    const dialogo = dialogoRef.current
    const campo = campoAntesDaPergunta.current?.isConnected ? campoAntesDaPergunta.current
      : dialogo?.querySelector('[data-foco-inicial]:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled)')
    campo?.focus({ preventScroll: true })
  }
  // Só vale na abertura, como o foco que o showModal() dá.
  const focarCampoRef = useRef(focarCampo)
  // Grava a versão mais nova fora do corpo de render (linter: nada de ref
  // escrita durante o render), mas ainda síncrono, antes de qualquer clique.
  useLayoutEffect(() => {
    aoFecharRef.current = aoFechar; segurarRef.current = segurarFechamento; descarteRef.current = confirmarDescarte
    perguntandoRef.current = perguntando
  })
  // Sem nada digitado, a pergunta não tem mais objeto.
  const perguntaValida = perguntando && confirmarDescarte && !segurarFechamento

  useEffect(() => {
    const dialogo = dialogoRef.current
    const dialogoPai = dialogo.parentElement?.closest('dialog[open]')
    if (!dialogo.open) dialogo.showModal()
    if (focarCampoRef.current) {
      const campo = dialogo.querySelector('[data-foco-inicial]:not(:disabled)')
        ?? dialogo.querySelector('input:not(:disabled), select:not(:disabled), textarea:not(:disabled)')
      campo?.focus({ preventScroll: true })
    }

    const fecharSeLivre = () => {
      if (segurarRef.current || !aoFecharRef.current) return
      if (perguntandoRef.current) { voltarAEdicao(); return }
      if (descarteRef.current) { abrirPergunta(); return }
      aoFecharRef.current()
    }
    const aoCancelar = (evento) => { evento.preventDefault(); fecharSeLivre() }
    // Clique no ::backdrop chega no próprio <dialog>, nunca no conteúdo.
    const aoClicar = (evento) => { if (evento.target === dialogo) fecharSeLivre() }
    // #2078 (revisão R2): o Chromium só deixa cancelar o `cancel` com ativação do usuário desde o
    // último; o 2º Esc seguido fecha o dialog sozinho e o React seguia achando que estava aberto (folha
    // sumida, valor perdido). Se o fechamento estava segurado (ou indisponível), a folha reabre com o
    // que foi digitado, que é o motivo de segurar; senão o pai fica sabendo e desmonta, como no Esc.
    const aoFecharNativo = () => {
      // O close anterior pode chegar depois da reabertura (inclusive no StrictMode).
      if (dialogo.open) return
      if (segurarRef.current || descarteRef.current || !aoFecharRef.current) {
        if (!dialogo.open) dialogo.showModal()
        if (descarteRef.current && !segurarRef.current) abrirPergunta()
      } else aoFecharRef.current()
    }
    dialogo.addEventListener('cancel', aoCancelar)
    dialogo.addEventListener('click', aoClicar)
    dialogo.addEventListener('close', aoFecharNativo)

    return () => {
      // Antes do close() abaixo: fechar ao desmontar não é o navegador fechando sozinho.
      dialogo.removeEventListener('close', aoFecharNativo)
      dialogo.removeEventListener('cancel', aoCancelar)
      dialogo.removeEventListener('click', aoClicar)
      if (dialogo.open) dialogo.close()
      // O nó que abriu pode ter desmontado. Sem isto o foco cairia no body.
      const anterior = focoAnterior.current
      const paiAberto = dialogoPai?.isConnected && dialogoPai.open ? dialogoPai : null
      if (anterior?.isConnected && anterior !== document.body && anterior !== document.documentElement
        && (!paiAberto || paiAberto.contains(anterior))) anterior.focus()
      else if (paiAberto) {
        const destino = paiAberto.querySelector('[data-foco-reserva]')
          ?? paiAberto.querySelector('button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex="0"]')
          ?? paiAberto
        destino.focus()
      } else document.querySelector('[data-foco-reserva]')?.focus()
    }
  }, [])

  return (
    <dialog
      ref={dialogoRef}
      className={[css.caixa, className].filter(Boolean).join(' ')}
      style={largura ? { width: largura } : undefined}
      aria-labelledby={`${id}-titulo`}
      aria-describedby={descricao ? `${id}-descricao` : undefined}
      aria-modal="true"
    >
      {/* Distingue fechar a janela de sair da sessão e do botão do rodapé. */}
      <button type="button" className={css.fecharCanto} aria-label="Fechar janela" title="Fechar (Esc)" disabled={!aoFechar}
        onClick={() => { if (confirmarDescarte && !segurarFechamento) abrirPergunta(); else aoFechar?.() }}>
        <Icone nome="x" tamanho={18} />
      </button>
      {/* Integração: título e rodapé fora da rolagem. Folha comprida (resumo
          do Encerrar, canhoto) rolava o título para fora e escondia o botão de
          ação lá embaixo; agora só o miolo rola. */}
      <h2 id={`${id}-titulo`} className={css.titulo}>{titulo}</h2>
      <div className={css.conteudo}>
        {perguntaValida && (
          <div className={css.descarte} role="alert">
            <p>Descartar o que foi digitado?</p>
            <div className={css.descarteAcoes}>
              {/* eslint-disable-next-line jsx-a11y/no-autofocus -- o foco vai para a opção que não perde nada */}
              <Botao variante="secundario" autoFocus onClick={voltarAEdicao}>Continuar editando</Botao>
              <Botao variante="perigo" onClick={() => { setPerguntando(false); aoFechar?.() }}>Descartar</Botao>
            </div>
          </div>
        )}
        {descricao && <p id={`${id}-descricao`} className={css.sub}>{descricao}</p>}
        {children}
      </div>
      {rodape && <div className={css.rodape}>{rodape}</div>}
    </dialog>
  )
}
