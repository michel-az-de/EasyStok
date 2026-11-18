import { useState } from 'react'
import { MenuDeAcoes } from '../../componentes/MenuDeAcoes'
import { REACOES_RAPIDAS, apagarParaTodos } from '../../dominio/acoesNaMensagem'
import css from './atendimento.module.css'

const CONFIRMAR_APAGAR = 'Apagar esta mensagem para todos? O cliente deixa de ver no WhatsApp, e isso não se desfaz.'

// #2833: o menu da bolha, no MenuDeAcoes. Seis reações em linha (tocar na que já está retira), copiar o texto e
// apagar para todos. A hora do prazo de 48 h é a do toque no gatilho (captura do ponteiro ou da tecla, antes do
// clique que abre), para a bolha seguir pura; sem esse toque o item de apagar não aparece. Na Meta, o item vira a
// linha que explica por que não dá.
export function MenuDaMensagem({ mensagem, podeReagir, aoReagir, aoApagar, alinhamento = 'direita' }) {
  const [tocadoEm, setTocadoEm] = useState(null)
  const medir = () => setTocadoEm(Date.now())
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
    // Apagar para todos não volta atrás: o cliente deixa de ver no WhatsApp. Pergunta antes, como o próprio WhatsApp.
    else if (item.acao === 'apagar') { if (window.confirm(CONFIRMAR_APAGAR)) aoApagar() }
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
    </span>
  )
}
