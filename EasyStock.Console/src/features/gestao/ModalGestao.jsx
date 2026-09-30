// Casca da Gestão (rodada 13, issue das 5 frentes paralelas): o lugar comum
// para Produção e cardápio, Caixa, Janelas de entrega, Fidelidade e cupons e
// Entregas e integrações. Cada aba mora no próprio arquivo (uma por frente);
// este componente só monta o tablist e lembra qual aba estava aberta.
//
// Mesmo molde de ModalAutomacoes: `Modal` de sempre, auto-suficiente (só
// `aoFechar`), estado do modal em `app/App.jsx`.
import { useCallback, useMemo, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { AbaProducao } from './producao/AbaProducao'
import { AbaCaixa } from './caixa/AbaCaixa'
import { AbaJanelas } from './janelas/AbaJanelas'
import { AbaFidelidade } from './fidelidade/AbaFidelidade'
import { AbaIntegracoes } from './integracoes/AbaIntegracoes'
import css from './gestao.module.css'

const ABAS = [
  { id: 'producao', rotulo: 'Produção e cardápio' },
  { id: 'caixa', rotulo: 'Caixa' },
  { id: 'janelas', rotulo: 'Janelas de entrega' },
  { id: 'fidelidade', rotulo: 'Fidelidade e cupons' },
  { id: 'integracoes', rotulo: 'Entregas e integrações' },
]

// "A aba ativa é lembrada durante a sessão": sessionStorage (não
// localStorage, que sobrevive à sessão) no mesmo molde de `app/Moldura.jsx`
// (CHAVE_TEMA, CHAVE_SIMULAR_ESCONDIDO).
const CHAVE_ABA = 'casa-da-baba:gestao-aba'

function lerAbaSalva() {
  try {
    const salva = window.sessionStorage.getItem(CHAVE_ABA)
    if (ABAS.some((aba) => aba.id === salva)) return salva
  } catch {
    // sem sessionStorage, a gaveta sempre abre na primeira aba
  }
  return ABAS[0].id
}

function gravarAba(id) {
  try {
    window.sessionStorage.setItem(CHAVE_ABA, id)
  } catch {
    // sem persistência, a aba lembrada vale só para esta renderização
  }
}

export function ModalGestao({ aoFechar }) {
  const [ativa, setAtiva] = useState(lerAbaSalva)
  const referenciasDasAbas = useRef([])

  const escolher = useCallback((id) => {
    setAtiva(id)
    gravarAba(id)
  }, [])

  // Setas do teclado (padrão APG de tabs, ativação automática): a seta já
  // move o foco E troca a aba, sem precisar de Enter/Espaço depois. Home e
  // End vão direto para a primeira e a última, sem passar pelas do meio.
  const aoTeclar = useCallback((evento) => {
    const indiceAtual = ABAS.findIndex((aba) => aba.id === ativa)
    let proximo = null
    if (evento.key === 'ArrowRight') proximo = (indiceAtual + 1) % ABAS.length
    else if (evento.key === 'ArrowLeft') proximo = (indiceAtual - 1 + ABAS.length) % ABAS.length
    else if (evento.key === 'Home') proximo = 0
    else if (evento.key === 'End') proximo = ABAS.length - 1
    if (proximo == null) return
    evento.preventDefault()
    escolher(ABAS[proximo].id)
    referenciasDasAbas.current[proximo]?.focus()
  }, [ativa, escolher])

  // Um por frente, arquivo próprio (US da issue): o objeto só decide qual
  // elemento entra na árvore, os outros quatro nunca chegam a renderizar.
  const painelDaAba = useMemo(() => ({
    producao: <AbaProducao />,
    caixa: <AbaCaixa />,
    janelas: <AbaJanelas />,
    fidelidade: <AbaFidelidade />,
    integracoes: <AbaIntegracoes />,
  }), [])

  return (
    <Modal
      titulo="Gestão"
      descricao="Produção, caixa, janelas de entrega, fidelidade e integrações num só lugar."
      aoFechar={aoFechar}
      largura="min(880px, calc(100vw - 32px))"
      rodape={<Botao className={css.toque} onClick={aoFechar}>Fechar</Botao>}
    >
      {/* Rolagem horizontal no celular (390 px): a barra vira largura total
          da tela e as abas correm por baixo do dedo, sem quebrar linha. */}
      <div className={css.tablistRolavel}>
        <div
          role="tablist"
          aria-label="Áreas de gestão"
          tabIndex={-1}
          className={css.tablist}
          onKeyDown={aoTeclar}
        >
          {ABAS.map((aba, indice) => (
            <button
              key={aba.id}
              ref={(el) => { referenciasDasAbas.current[indice] = el }}
              type="button"
              role="tab"
              id={`gestao-aba-${aba.id}`}
              aria-selected={aba.id === ativa}
              aria-controls={`gestao-painel-${aba.id}`}
              tabIndex={aba.id === ativa ? 0 : -1}
              className={`${css.aba} ${aba.id === ativa ? css.abaAtiva : ''}`}
              onClick={() => escolher(aba.id)}
            >
              {aba.rotulo}
            </button>
          ))}
        </div>
      </div>
      <div
        role="tabpanel"
        id={`gestao-painel-${ativa}`}
        aria-labelledby={`gestao-aba-${ativa}`}
        tabIndex={0}
        className={css.painel}
      >
        {painelDaAba[ativa]}
      </div>
    </Modal>
  )
}
