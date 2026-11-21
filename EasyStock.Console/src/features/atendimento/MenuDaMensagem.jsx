import { useState } from 'react'
import { MenuDeAcoes } from '../../componentes/MenuDeAcoes'
import { ModalConfirmar } from '../../componentes/ModalConfirmar'
import { REACOES_RAPIDAS, apagarParaTodos } from '../../dominio/acoesNaMensagem'
import css from './atendimento.module.css'

const CONFIRMAR_APAGAR = 'O cliente deixa de ver esta mensagem no WhatsApp, e isso não se desfaz.'

// #2833: o menu da bolha, no MenuDeAcoes. Seis reações em linha (tocar na que já está retira), copiar o texto e
// apagar para todos. A hora do prazo de 48 h é a do toque no gatilho (captura do ponteiro ou da tecla, antes do
// clique que abre), para a bolha seguir pura; sem esse toque o item de apagar não aparece. Na Meta, o item vira a
// linha que explica por que não dá.
export function MenuDaMensagem({ mensagem, podeReagir, aoReagir, aoApagar, alinhamento = 'direita' }) {
  const [tocadoEm, setTocadoEm] = useState(null)
  const medir = () => setTocadoEm(Date.now())
  const [confirmando, setConfirmando] = useState(false)
  const apagar = aoApagar && tocadoEm !== null ? apagarParaTodos(mensagem, tocadoEm) : { mostra: false }
  const semApagar = aoApagar ? apagarParaTodos(mensagem, 0).motivo : undefined

  const reacoes = podeReagir && aoReagir && {
    titulo: 'Reagir',
    disposicao: 'faixa',
    itens: REACOES_RAPIDAS.map(({ emoji, nome }) => ({
      chave: `reagir-${nome}`, titulo: emoji, marcado: mensagem.reacao === emoji, acao: 'reagir', emoji,
    })),
  }
  const daMensagem = [
    mensagem.texto?.trim() && { chave: 'copiar', titulo: 'Copiar texto', acao: 'copiar' },
    apagar.mostra && { chave: 'apagar', titulo: 'Apagar para todos', detalhe: 'Até 48 h depois do envio', acao: 'apagar' },
    semApagar && { chave: 'sem-apagar', informativo: true, titulo: 'Apagar para todos', detalhe: semApagar },
  ].filter(Boolean)
  const grupos = [reacoes, daMensagem.length && { titulo: 'Mensagem', itens: daMensagem }].filter(Boolean)

  async function escolher(item) {
    if (item.acao === 'reagir') aoReagir(item.marcado ? null : item.emoji)
    // Apagar para todos não volta atrás: o cliente deixa de ver no WhatsApp. Pergunta antes, no modal do Console (#3011).
    else if (item.acao === 'apagar') setConfirmando(true)
    else if (item.acao === 'copiar') {
      try { await navigator.clipboard.writeText(mensagem.texto) } catch { /* sem permissão: nada a fazer */ }
    }
  }

  if (!grupos.length) return null
  return (
    <span className={css.menuDaMensagem} onPointerDownCapture={medir} onKeyDownCapture={medir}>
      <MenuDeAcoes
        rotulo="Mais opções da mensagem" alinhamento={alinhamento} className={css.gatilhoDaMensagem}
        grupos={grupos} aoEscolherGrupo={escolher}
      />
      {confirmando && (
        <ModalConfirmar
          titulo="Apagar para todos?" descricao={CONFIRMAR_APAGAR} rotuloConfirmar="Apagar para todos"
          aoConfirmar={aoApagar} aoFechar={() => setConfirmando(false)}
        />
      )}
    </span>
  )
}
