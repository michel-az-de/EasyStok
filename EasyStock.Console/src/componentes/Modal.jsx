import { useEffect, useLayoutEffect, useRef } from 'react'
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
export function Modal({ titulo, descricao, aoFechar, rodape, largura, children }) {
  const dialogoRef = useRef(null)
  const focoAnterior = useRef(null)
  const aoFecharRef = useRef(aoFechar)
  // Grava a versão mais nova fora do corpo de render (linter: nada de ref
  // escrita durante o render), mas ainda síncrono, antes de qualquer clique.
  useLayoutEffect(() => { aoFecharRef.current = aoFechar })

  useEffect(() => {
    const dialogo = dialogoRef.current
    focoAnterior.current = document.activeElement
    if (!dialogo.open) dialogo.showModal()

    const aoCancelar = (evento) => { evento.preventDefault(); aoFecharRef.current() }
    // Clique no ::backdrop chega no próprio <dialog>, nunca no conteúdo.
    const aoClicar = (evento) => { if (evento.target === dialogo) aoFecharRef.current() }
    dialogo.addEventListener('cancel', aoCancelar)
    dialogo.addEventListener('click', aoClicar)

    return () => {
      dialogo.removeEventListener('cancel', aoCancelar)
      dialogo.removeEventListener('click', aoClicar)
      if (dialogo.open) dialogo.close()
      // O nó que abriu pode ter desmontado. Sem isto o foco cairia no body.
      const anterior = focoAnterior.current
      if (anterior?.isConnected) anterior.focus()
      else document.querySelector('[data-foco-reserva]')?.focus()
    }
  }, [])

  return (
    <dialog
      ref={dialogoRef}
      className={css.caixa}
      style={largura ? { width: largura } : undefined}
      aria-label={titulo}
    >
      {/* Distingue fechar a janela de sair da sessão e do botão do rodapé. */}
      <button type="button" className={css.fecharCanto} aria-label="Fechar janela" title="Fechar (Esc)" onClick={aoFechar}>
        <Icone nome="x" tamanho={18} />
      </button>
      {/* Integração: título e rodapé fora da rolagem. Folha comprida (resumo
          do Encerrar, canhoto) rolava o título para fora e escondia o botão de
          ação lá embaixo; agora só o miolo rola. */}
      <h2 className={css.titulo}>{titulo}</h2>
      <div className={css.conteudo}>
        {descricao && <p className={css.sub}>{descricao}</p>}
        {children}
      </div>
      {rodape && <div className={css.rodape}>{rodape}</div>}
    </dialog>
  )
}
