// Primeiros passos (#2070, só no modo API): o assistente que passa por todas as configurações da loja. O passo vem da
// rota (`#/m/configuracoes/primeiros-passos/<passo>`); sem ele, a tela retoma do primeiro em aberto. Cada passo monta
// a aba que já existe (`renderizarAba`, o PainelDeAjuste com os slots do App) ou uma peça própria, e grava pelas
// rotas de sempre. "Conferi, seguir" só marca o passo e fica bloqueado enquanto houver alteração não salva.
import { useCallback, useContext, useEffect, useId, useRef, useState } from 'react'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { ContextoRascunhosDosAjustes } from '../../../aplicacao/rascunhosDosAjustes'
import { useConfiguracaoInicialApi } from '../../../aplicacao/useConfiguracaoInicialApi'
import { Botao } from '../../../componentes/Botao'
import {
  AVISO_DA_PARTE_2, DESTINO_DESCONHECIDO, PARTE_AJUSTES, PARTE_ESSENCIAL, PASSO_FINAL, ROTULO_DA_PARTE, TEXTO_DESCONHECIDO,
  passoDaRetomada, passoDoMapa, passoVizinho, passosDaParte, passosDesconhecidos, pendenciaDoPasso, textoDoProgresso,
} from '../../../dominio/configuracaoInicial'
import { hashDoPasso } from '../../../dominio/rota'
import { FalhaDeLeitura } from '../PartesDoAjuste'
import { PassoBoasVindas } from './PassoBoasVindas'
import { PassoPronto } from './PassoPronto'
import css from './configuracaoInicial.module.css'

const AVISO_PENDENCIA = 'Há alterações não salvas neste passo. Salve ou desfaça antes de seguir.'
const MARCA_DA_SITUACAO = { conferido: 'conferido', depois: 'para depois' }
const linhaDe = (estado, id) => estado?.passos?.find((p) => p?.passo === id) ?? null

function Progresso({ estado, atual, aberta }) {
  const id = useId()
  const parte = (chave) => (
    <section className={css.parte} aria-labelledby={`${id}-${chave}`}>
      <h3 id={`${id}-${chave}`} className={css.tituloParte}>{ROTULO_DA_PARTE[chave]}</h3>
      {chave === PARTE_AJUSTES && <p className={css.apoio}>{AVISO_DA_PARTE_2}</p>}
      <ol className={css.passos}>
        {passosDaParte(chave).map((p) => {
          const linha = linhaDe(estado, p.id)
          const marca = linha?.disponivel === false ? 'sem acesso' : MARCA_DA_SITUACAO[linha?.situacao] ?? null
          return (
            <li key={p.id} className={css.itemPasso} aria-current={p.id === atual ? 'step' : undefined}>
              <a className={css.linkPasso} href={hashDoPasso(p.id)}>{p.rotulo}</a>
              {marca && <span className={css.marca}>{marca}</span>}
            </li>
          )
        })}
        {/* Passo que a API já tem e este console ainda não conhece. */}
        {chave === PARTE_AJUSTES && passosDesconhecidos(estado).map((passo) => (
          <li key={passo} className={css.itemPasso}><a className={css.linkPasso} href={DESTINO_DESCONHECIDO}>{TEXTO_DESCONHECIDO}</a></li>
        ))}
      </ol>
    </section>
  )
  return (
    <nav className={css.progresso} aria-label="Progresso dos primeiros passos">
      <details className={css.lista} open={aberta}>
        <summary>Todos os passos</summary>
        {parte(PARTE_ESSENCIAL)}
        {parte(PARTE_AJUSTES)}
        <p className={css.itemPasso} aria-current={atual === PASSO_FINAL ? 'step' : undefined}>
          <a className={css.linkPasso} href={hashDoPasso(PASSO_FINAL)}>Revisão final</a>
        </p>
      </details>
    </nav>
  )
}

export function TelaConfiguracaoInicial({ passo: passoDaRota = null, renderizarAba }) {
  const { estado, erro, ocupado, recarregar, marcar, adiar, concluir } = useConfiguracaoInicialApi()
  const { acoes, entrada } = useAcessoModulos()
  const rascunhos = useContext(ContextoRascunhosDosAjustes)
  const [pendenteLocal, setPendenteLocal] = useState(false)
  // O aviso pertence ao passo em que nasceu: trocar de passo não o carrega junto.
  const [falha, setFalha] = useState(null)
  // A lista de passos começa aberta onde cabe ao lado; no celular, recolhida acima do passo.
  const [listaAberta] = useState(() => typeof window !== 'undefined' && window.matchMedia?.('(min-width: 1100px)')?.matches === true)
  const tituloRef = useRef(null)
  const idTitulo = useId()
  const atual = passoDaRota ?? (estado ? passoDaRetomada(estado) : null)
  const pendente = pendenteLocal || pendenciaDoPasso(atual, rascunhos)
  const aoMudarPendencia = useCallback((valor) => setPendenteLocal(Boolean(valor)), [])

  // Sem passo na rota, a hash passa a dizer o passo da retomada: o Voltar do navegador volta um passo.
  useEffect(() => {
    if (!passoDaRota && estado) window.location.replace(hashDoPasso(passoDaRetomada(estado)))
  }, [passoDaRota, estado])
  // Trocar de passo leva o foco ao título dele.
  useEffect(() => { tituloRef.current?.focus() }, [atual])
  // Fechar ou recarregar a aba com alteração não salva pede confirmação ao navegador.
  useEffect(() => {
    if (!pendente) return undefined
    const antesDeSair = (evento) => { evento.preventDefault(); evento.returnValue = '' }
    window.addEventListener('beforeunload', antesDeSair)
    return () => window.removeEventListener('beforeunload', antesDeSair)
  }, [pendente])

  if (!estado) {
    return erro
      ? <FalhaDeLeitura mensagem={`Os primeiros passos não carregaram: ${erro}`} aoTentarDeNovo={recarregar} />
      : <output className={css.apoio}>Carregando os primeiros passos…</output>
  }

  const definicao = passoDoMapa(atual)
  const linha = linhaDe(estado, atual)
  const semAcesso = linha?.disponivel === false
  const final = atual === PASSO_FINAL
  const anterior = passoVizinho(atual, -1, estado)
  const proximo = passoVizinho(atual, 1, estado)
  const ocupada = ocupado !== null
  const aviso = falha?.passo === atual ? falha.texto : null
  const avisar = (texto) => setFalha({ passo: atual, texto })
  const irPara = (id) => {
    if (!id) return
    setPendenteLocal(false)
    window.location.hash = hashDoPasso(id)
  }
  const sair = () => { window.location.hash = entrada ?? '#/' }

  async function seguir(situacao) {
    if (situacao === 'conferido' && pendente) return
    const resultado = await marcar(atual, situacao)
    if (!resultado.ok) { avisar(`Não foi possível marcar o passo. ${resultado.erro.message}`); return }
    irPara(proximo)
  }
  // A dona (ou o suporte) adia: o assistente não abre mais sozinho. Quem não pode adiar só sai.
  async function continuarDepois() {
    if (estado.podeConcluir === true) {
      const resultado = await adiar()
      if (!resultado.ok) { avisar(`Não foi possível deixar para depois. ${resultado.erro.message}`); return }
    }
    sair()
  }
  async function concluirTudo() {
    const resultado = await concluir()
    if (!resultado.ok) {
      avisar(resultado.erro.status === 403
        ? 'Só a dona ou o suporte concluem os primeiros passos.'
        : `Não foi possível concluir. ${resultado.erro.message}`)
      return
    }
    sair()
  }

  return (
    <div className={css.tela}>
      <Progresso estado={estado} atual={atual} aberta={listaAberta} />
      <section className={css.passo} aria-labelledby={idTitulo}>
        <p className={css.contagem}>{textoDoProgresso(atual)}</p>
        <h2 id={idTitulo} ref={tituloRef} tabIndex={-1} className={css.titulo}>{definicao.rotulo}</h2>
        <p className={css.descricao}>{definicao.descricao}</p>
        {semAcesso
          ? <p className={css.semAcesso}>{linha.motivo ?? 'Seu perfil não abre este ajuste.'}</p>
          : (
            <div className={css.conteudo}>
              {definicao.monta.map((peca, indice) => (
                <div key={`${atual}-${indice}`} className={css.peca}>
                  {peca.componente === 'boas-vindas' && <PassoBoasVindas aoMudarPendencia={aoMudarPendencia} />}
                  {peca.componente === 'pronto' && <PassoPronto estado={estado} podeAbrirLoja={acoes?.controlarLoja === true} />}
                  {peca.aba && renderizarAba({ aba: peca.aba, secoes: peca.secoes ?? null, secao: peca.secao ?? null, aoMudarPendencia, compacto: true })}
                </div>
              ))}
            </div>
          )}
      </section>
      <output className={pendente ? css.pendencia : 'sr'} aria-live="polite">{pendente ? AVISO_PENDENCIA : ''}</output>
      {aviso && <p className={css.erro} role="alert">{aviso}</p>}
      <div className={css.acoes}>
        {final && <>
          <Botao variante="texto" disabled={!anterior} onClick={() => irPara(anterior)}>Voltar</Botao>
          {estado.podeConcluir === true
            ? <Botao variante="primario" className={css.primario} disabled={ocupada} onClick={concluirTudo}>Concluir</Botao>
            : <p className={css.apoio}>Só a dona conclui os primeiros passos.</p>}
        </>}
        {!final && semAcesso && <>
          <Botao variante="texto" disabled={!anterior} onClick={() => irPara(anterior)}>Voltar</Botao>
          <Botao variante="primario" className={css.primario} onClick={() => irPara(proximo)}>Seguir</Botao>
        </>}
        {!final && !semAcesso && <>
          <Botao variante="texto" disabled={!anterior || ocupada} onClick={() => irPara(anterior)}>Voltar</Botao>
          <Botao variante="texto" className={css.depois} disabled={ocupada} onClick={() => seguir('depois')}>Fazer depois</Botao>
          <Botao variante="primario" className={css.primario} disabled={pendente || ocupada} onClick={() => seguir('conferido')}>Conferi, seguir</Botao>
        </>}
      </div>
      <div className={css.saida}>
        <Botao variante="texto" disabled={ocupada} onClick={continuarDepois}>Continuar depois</Botao>
      </div>
    </div>
  )
}
