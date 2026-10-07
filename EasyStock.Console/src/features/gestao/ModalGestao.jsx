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
import { SecaoWhatsApp } from './integracoes/SecaoWhatsApp'
import { AbaAtendimento } from './atendimento/AbaAtendimento'
import { AbaRespostas } from './respostas/AbaRespostas'
import { useAtendimento } from '../../aplicacao/contextos'
import css from './gestao.module.css'

// #1441: respostas prontas e automáticas saíram do caminho do atendimento e vivem aqui.
const ABA_RESPOSTAS = { id: 'respostas', rotulo: 'Respostas e automáticas' }

const ABAS_DEMONSTRACAO = [
  ABA_RESPOSTAS,
  { id: 'producao', rotulo: 'Produção e cardápio' },
  { id: 'caixa', rotulo: 'Caixa' },
  { id: 'janelas', rotulo: 'Janelas de entrega' },
  { id: 'fidelidade', rotulo: 'Fidelidade e cupons' },
  { id: 'integracoes', rotulo: 'Entregas e integrações' },
]

// Modo API (F02): a aba Atendimento é a única que já grava no EasyStok, por isso vem
// primeiro. No modo demonstração ela não aparece (não há expediente nem configuração).
const ABAS_API = [{ id: 'atendimento', rotulo: 'Atendimento' }, ...ABAS_DEMONSTRACAO]

// Modo API (F06, decisão do Felipe em 30/09): as abas sem backend não somem. Abrem com a
// faixa e os controles desabilitados até a fatia de cada uma entrar. Nada é gravado: as
// ações delas também avisam (`aplicacao/api/naoLigadas.js`), e Integrações nunca guarda chave.
const AINDA_NAO_LIGADO = {
  producao: 'Produção e cardápio ainda não estão ligados ao EasyStok nesta versão (F11). Nada aqui é gravado.',
  caixa: 'O caixa ainda não está ligado ao EasyStok nesta versão (F14). Nada aqui é gravado.',
  janelas: 'Esta aba ainda não está ligada. As janelas de verdade estão em Entregas › Janelas e frete.',
  fidelidade: 'Fidelidade e cupons ainda não estão ligados ao EasyStok nesta versão (F15). Nada aqui é gravado.',
  integracoes: 'As integrações ainda não estão ligadas nesta versão (F16). Nenhuma chave é guardada no navegador.',
}

function AbaNaoLigada({ texto, children }) {
  return (
    <>
      <p className={css.naoLigado} role="note">{texto}</p>
      <fieldset disabled className={css.desligado}>{children}</fieldset>
    </>
  )
}

// "A aba ativa é lembrada durante a sessão": sessionStorage (não
// localStorage, que sobrevive à sessão) no mesmo molde de `app/Moldura.jsx`
// (CHAVE_TEMA, CHAVE_SIMULAR_ESCONDIDO).
const CHAVE_ABA = 'casa-da-baba:gestao-aba'

function lerAbaSalva(abas) {
  try {
    const salva = window.sessionStorage.getItem(CHAVE_ABA)
    if (abas.some((aba) => aba.id === salva)) return salva
  } catch {
    // sem sessionStorage, a gaveta sempre abre na primeira aba
  }
  return abas[0].id
}

function gravarAba(id) {
  try {
    window.sessionStorage.setItem(CHAVE_ABA, id)
  } catch {
    // sem persistência, a aba lembrada vale só para esta renderização
  }
}

export function ModalGestao({ aoFechar, abaInicial = null }) {
  const { fonteApi } = useAtendimento()
  const ABAS = fonteApi ? ABAS_API : ABAS_DEMONSTRACAO
  const [ativa, setAtiva] = useState(() => (ABAS.some((aba) => aba.id === abaInicial) ? abaInicial : lerAbaSalva(ABAS)))
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
  }, [ativa, escolher, ABAS])

  // Um por frente, arquivo próprio (US da issue): o objeto só decide qual
  // elemento entra na árvore, os outros quatro nunca chegam a renderizar.
  const painelDaAba = useMemo(() => ({
    atendimento: <AbaAtendimento />,
    respostas: <AbaRespostas />,
    producao: <AbaProducao />,
    caixa: <AbaCaixa />,
    janelas: <AbaJanelas />,
    fidelidade: <AbaFidelidade />,
    integracoes: <AbaIntegracoes />,
  }), [])

  return (
    <Modal
      titulo="Gestão"
      descricao="Respostas, produção, caixa, janelas de entrega, fidelidade e integrações num só lugar."
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
        {/* #1417: a seção do WhatsApp já grava no EasyStok; fica fora do bloco desligado. */}
        {fonteApi && ativa === 'integracoes' && <SecaoWhatsApp />}
        {fonteApi && AINDA_NAO_LIGADO[ativa]
          ? <AbaNaoLigada texto={AINDA_NAO_LIGADO[ativa]}>{painelDaAba[ativa]}</AbaNaoLigada>
          : painelDaAba[ativa]}
      </div>
    </Modal>
  )
}
