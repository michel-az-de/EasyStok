import { Suspense, lazy, useEffect, useMemo, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { BotaoIcone } from '../../../componentes/BotaoIcone'
import { CampoArea, CampoTexto } from '../../../componentes/Campo'
import { Icone } from '../../../componentes/Icone'
import {
  ROTEIRO_ETAPAS_MAXIMO, ROTEIRO_INSTRUCAO_MAXIMO, ROTEIRO_NOME_MAXIMO, desligadasDoFluxo, erroDoRoteiro, mesmoRoteiro, moverEtapa,
} from '../../../dominio/painelAssistente'
import { GRUPOS, ORIENTACOES, rotuloDaFerramenta, desenharFluxo, idsDoGrupo } from '../../../dominio/desenhoDoFluxo'
import { FluxoExpandido } from './FluxoExpandido'
import { LegendaDoFluxo } from './LegendaDoFluxo'
import { Retorno } from './Retorno'
import css from './painelAssistente.module.css'

const OBSERVACOES_MAXIMO = 500
// O canvas (React Flow) só baixa quando a aba Fluxo abre: a biblioteca não entra no bundle inicial do console.
const CanvasDoFluxo = lazy(() => import('./CanvasDoFluxo'))

export function AbaFluxo({ fluxo, podeEditar, painel }) {
  const [selecionado, setSelecionado] = useState('entrada')
  const [orientacao, setOrientacao] = useState(() => (window.matchMedia?.('(max-width: 899px)').matches ? ORIENTACOES.vertical : ORIENTACOES.horizontal))
  const [abertos, setAbertos] = useState([])
  const [expandido, setExpandido] = useState(false)
  const [selecoes, setSelecoes] = useState(0)
  const botaoExpandir = useRef(null)
  const [foco, setFoco] = useState({ ids: null })
  const [roteiro, setRoteiro] = useState(fluxo.roteiro)
  const detalhe = useRef(null)
  const grade = useRef(null)
  const [aviso, setAviso] = useState('')
  // Rascunho das observações da loja aqui em cima: uma fonte só, aberta no aside ou no Expandir, nada digitado se perde.
  const observacoesDaLoja = fluxo.observacoesLoja ?? ''
  const [observacoes, setObservacoes] = useState(observacoesDaLoja)
  const [baseObservacoes, setBaseObservacoes] = useState(observacoesDaLoja)
  if (baseObservacoes !== observacoesDaLoja) {
    setBaseObservacoes(observacoesDaLoja)
    if (observacoes === baseObservacoes) setObservacoes(observacoesDaLoja)
  }
  const roteiroMudou = !mesmoRoteiro(roteiro, fluxo.roteiro)
  // Depois de salvar, o fluxo novo da API vira a base; rascunho sem mudança acompanha.
  const [base, setBase] = useState(fluxo.roteiro)
  if (base !== fluxo.roteiro) {
    setBase(fluxo.roteiro)
    if (mesmoRoteiro(roteiro, base)) setRoteiro(fluxo.roteiro)
  }
  // #2857: em tela estreita o vertical vira coluna (cresce só para baixo); a largura é lida uma vez, como a orientação.
  const [compacto] = useState(() => Boolean(window.matchMedia?.('(max-width: 599px)').matches))
  const desenho = useMemo(() => desenharFluxo({ fluxo, roteiro, orientacao, abertos, compacto }), [fluxo, roteiro, orientacao, abertos, compacto])

  // Escolhido um nó: o ajuste aparece. Lado a lado (largura sobrando) ele já está à vista; em coluna rola só o corpo
  // rolável até ele. O foco vai para o título do ajuste e um aviso curto diz o que mudou.
  function selecionar(id) {
    setSelecionado(id)
    setSelecoes((n) => n + 1)
    if (id.startsWith('grupo:')) return
    requestAnimationFrame(() => requestAnimationFrame(() => {
      const titulo = document.querySelector('[data-ajuste-titulo]')
      if (!titulo) return
      const alvo = detalhe.current
      const colunas = grade.current ? getComputedStyle(grade.current).gridTemplateColumns.trim().split(/\s+/).length : 2
      if (alvo && colunas < 2) rolarAte(alvo)
      titulo.focus({ preventScroll: true })
      setAviso(`Ajuste aberto: ${titulo.textContent.trim()}`)
    }))
  }

  // Abre ou fecha o grupo (Verificações, Roteiro, Ferramentas) e enquadra o que apareceu.
  function alternarGrupo(chave, abrir, roteiroNovo = roteiro) {
    const proximos = abrir ? [...abertos.filter((g) => g !== chave), chave] : abertos.filter((g) => g !== chave)
    setAbertos(proximos)
    // Abrir enquadra o grupo aberto agora; fechar volta para o último grupo ainda aberto (ou o desenho todo).
    const alvo = abrir ? chave : proximos[proximos.length - 1]
    setFoco({ ids: alvo ? idsDoGrupo(desenharFluxo({ fluxo, roteiro: roteiroNovo, orientacao, abertos: proximos, compacto }), alvo) : null })
  }

  function ativar(id) {
    selecionar(id)
    const chave = Object.keys(GRUPOS).find((g) => GRUPOS[g] === id)
    if (chave) alternarGrupo(chave, !abertos.includes(chave))
  }

  function trocarOrientacao(nova) {
    setOrientacao(nova)
    setFoco({ ids: null })
  }

  function alternarNo(dados) {
    // A chave liga ou desliga sem escolher o nó nem tirar o foco dela.
    if (dados.tipo === 'ferramenta') alternarFerramenta({ nome: dados.nome, ligada: dados.ligada }, fluxo, painel)
    else if (dados.tipo === 'roteiro') setRoteiro(roteiro.map((e, i) => (i === dados.indice ? { ...e, ativo: !e.ativo } : e)))
  }

  const dadosDoNo = (dados) => ({
    ...dados, selecionado: dados.id === selecionado, podeEditar, ocupado: painel.gravacao.salvando === 'ferramentas',
    aoAtivar: ativar, aoAlternar: alternarNo,
  })

  function acrescentarEtapa() {
    const novo = [...roteiro, { nome: 'Nova etapa', instrucao: '', ativo: true }]
    setRoteiro(novo)
    alternarGrupo('roteiro', true, novo)
    selecionar(`roteiro:${roteiro.length}`)
  }

  const ajuste = (
    <>
      {!expandido && <p role="status" className="sr">{aviso}</p>}
      <Detalhe
        id={selecionado} fluxo={fluxo} podeEditar={podeEditar} painel={painel}
        roteiro={roteiro} setRoteiro={setRoteiro} roteiroMudou={roteiroMudou} aoSelecionar={selecionar}
        observacoes={observacoes} setObservacoes={setObservacoes}
      />
    </>
  )

  return (
    <div className={css.fluxoCaixa}>
      <div className={css.barraDoFluxo}>
        {!compacto && <div role="group" aria-label="Direção do desenho" className={css.seletor}>
          {[[ORIENTACOES.horizontal, 'Horizontal'], [ORIENTACOES.vertical, 'Vertical']].map(([valor, rotulo]) => (
            <Botao key={valor} variante={orientacao === valor ? 'primario' : 'secundario'} aria-pressed={orientacao === valor} onClick={() => trocarOrientacao(valor)}>{rotulo}</Botao>
          ))}
        </div>}
        {podeEditar && roteiro.length < ROTEIRO_ETAPAS_MAXIMO && <Botao variante="texto" icone="mais" onClick={acrescentarEtapa}>Acrescentar etapa</Botao>}
        <LegendaDoFluxo />
          <Botao ref={botaoExpandir} variante="secundario" icone="ampliar" onClick={() => setExpandido(true)}>Expandir</Botao>
        <p className={css.nota}>Toque num nó para ajustar. Verificações, Roteiro e Ferramentas abrem ao tocar.</p>
      </div>
      <div ref={grade} className={css.fluxo}>
        <Suspense fallback={<output className={css.nota}>Carregando o desenho…</output>}>
          <CanvasDoFluxo interativo={!compacto} desenho={desenho} dadosDoNo={dadosDoNo} foco={foco} rotuloDoDesenho="Fluxo do Assistente, na ordem do turno" />
        </Suspense>
        {!expandido && <aside ref={detalhe} className={css.detalhe} aria-label="Ajuste do item escolhido">{ajuste}</aside>}
      </div>
      {expandido && (
        <FluxoExpandido
          aoFechar={() => setExpandido(false)} orientacao={orientacao} aoTrocarOrientacao={trocarOrientacao}
          desenho={desenho} dadosDoNo={dadosDoNo} foco={foco} rotuloDoDesenho="Fluxo do Assistente, na ordem do turno"
          chaveDoAjuste={`${selecionado}#${selecoes}`} retornoDoFoco={botaoExpandir}
          ajuste={ajuste} aviso={aviso}
        />
      )}
    </div>
  )
}

// Rola o corpo rolável mais próximo (o do modal ou o da página) até o ajuste; nunca a página por trás de um diálogo.
function rolarAte(el) {
  let pai = el.parentElement
  while (pai && pai !== document.body) {
    if (pai.tagName === 'DIALOG') return
    const o = getComputedStyle(pai).overflowY
    if ((o === 'auto' || o === 'scroll') && pai.scrollHeight > pai.clientHeight) break
    pai = pai.parentElement
  }
  const raiz = pai && pai !== document.body ? pai : document.scrollingElement
  if (!raiz) return
  const margem = parseFloat(getComputedStyle(el).scrollMarginTop) || 0
  const topo = raiz === document.scrollingElement ? 0 : raiz.getBoundingClientRect().top
  const base = raiz === document.scrollingElement ? window.innerHeight : raiz.getBoundingClientRect().bottom
  const r = el.getBoundingClientRect()
  // Como block 'nearest': se o ajuste já está visível não rola; senão rola o mínimo, sem passar o topo dele da margem do cabeçalho fixo.
  const sobe = r.top - topo - margem
  const desce = Math.min(r.bottom - base, sobe)
  const delta = sobe < 0 ? sobe : Math.max(0, desce)
  if (delta === 0) return
  const destino = Math.max(0, raiz.scrollTop + delta)
  const suave = !window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
  raiz.scrollTo({ top: destino, behavior: suave ? 'smooth' : 'auto' })
}

function Detalhe({ id, fluxo, podeEditar, painel, roteiro, setRoteiro, roteiroMudou, aoSelecionar, observacoes, setObservacoes }) {
  if (id === 'entrada') {
    return <Bloco titulo="Mensagem do cliente chega">
      <p>Cada mensagem nova no WhatsApp abre um turno. O Assistente passa pelas etapas em ordem e para na primeira que barra.</p>
      <p>Assistente na loja: <strong>{fluxo.ativo ? 'ligado' : 'desligado'}</strong>. Modelo de IA: <strong>{fluxo.modeloDisponivel ? 'disponível' : 'indisponível no servidor'}</strong>.</p>
    </Bloco>
  }
  if (id === 'saida') {
    return <Bloco titulo="Resposta ou equipe">
      <p>Com tudo passando, o modelo lê as instruções, o roteiro e o histórico, usa as ferramentas ligadas e responde.</p>
      <p>Quando uma etapa barra, ou quando o modelo usa escalar_para_dona, a conversa vai para a equipe com o motivo.</p>
    </Bloco>
  }
  if (id === 'assistente') {
    return <Bloco titulo="Assistente IA" icone="agente">
      <p>Com todas as verificações passando, o modelo lê as instruções, o roteiro e o histórico, usa as ferramentas ligadas e responde.</p>
      <p>Chave da loja: <strong>{fluxo.ativo ? 'ligado' : 'desligado'}</strong>. Modelo de IA: <strong>{fluxo.modeloDisponivel ? 'disponível' : 'indisponível no servidor'}</strong>.</p>
    </Bloco>
  }
  if (id === 'equipe') {
    return <Bloco titulo="Equipe" icone="usuarios">
      <p>Recebe a conversa quando uma verificação barra com aviso, ou quando o Assistente pede ajuda com escalar_para_dona. Vai com o motivo.</p>
    </Bloco>
  }
  if (id === GRUPOS.portoes) {
    return <Bloco titulo="Verificações" icone="lock">
      <p>{fluxo.portoes.length} etapas fixas, sempre na mesma ordem. A primeira que barra para o robô; as marcadas com o ícone de equipe passam a conversa para você.</p>
    </Bloco>
  }
  if (id === GRUPOS.roteiro) {
    return <Bloco titulo="Roteiro" icone="extrato">
      <p>{roteiro.length} etapas que o Assistente segue na ordem{fluxo.roteiroPersonalizado ? '' : ' (roteiro padrão)'}. Toque numa etapa para editar.</p>
    </Bloco>
  }
  if (id === GRUPOS.ferramentas) {
    return <Bloco titulo="Ferramentas" icone="plug">
      <p>{fluxo.ferramentas.filter((f) => f.ligada).length} de {fluxo.ferramentas.length} ligadas. Use a chave em cada ferramenta para ligar ou desligar.</p>
    </Bloco>
  }
  if (id.startsWith('portao:')) {
    const p = fluxo.portoes.find((x) => `portao:${x.codigo}` === id)
    if (!p) return null
    return <Bloco titulo={p.titulo} icone="lock">
      <p>{p.checa}</p>
      <p>{p.escala ? 'Quando barra, passa a conversa para a equipe com o motivo.' : 'Quando barra, o robô só não responde; a conversa fica como está.'}</p>
      <p className={css.nota}>Etapa fixa: protege o cliente e a loja. {p.quemMuda ? `Onde mudar o que ela checa: ${p.quemMuda}.` : 'Não tem ajuste.'}</p>
    </Bloco>
  }
  if (id === 'prompt') return <DetalhePrompt fluxo={fluxo} podeEditar={podeEditar} painel={painel} observacoes={observacoes} setObservacoes={setObservacoes} />
  if (id.startsWith('ferramenta:')) {
    const f = fluxo.ferramentas.find((x) => `ferramenta:${x.nome}` === id)
    return f ? <DetalheFerramenta ferramenta={f} fluxo={fluxo} podeEditar={podeEditar} painel={painel} /> : null
  }
  if (id.startsWith('roteiro:')) {
    const indice = Number(id.slice('roteiro:'.length))
    if (!roteiro[indice]) return null
    return <DetalheRoteiro indice={indice} roteiro={roteiro} setRoteiro={setRoteiro} roteiroMudou={roteiroMudou}
      fluxo={fluxo} podeEditar={podeEditar} painel={painel} aoSelecionar={aoSelecionar} />
  }
  return null
}

function Bloco({ titulo, icone = null, children }) {
  return (
    <div className={css.bloco}>
      <h4 tabIndex={-1} data-ajuste-titulo>{icone && <Icone nome={icone} tamanho={20} />} {titulo}</h4>
      {children}
    </div>
  )
}

function DetalhePrompt({ fluxo, podeEditar, painel, observacoes, setObservacoes }) {
  const mudou = observacoes.trim() !== (fluxo.observacoesLoja ?? '').trim()
  const passou = observacoes.length > OBSERVACOES_MAXIMO
  return (
    <Bloco titulo="Instruções da loja" icone="settings">
      <p>A loja escreve as observações, que entram no começo das instruções.</p>
      {podeEditar ? (
        <form onSubmit={(e) => { e.preventDefault(); if (mudou && !passou) painel.salvar('observacoes', { observacoesLoja: observacoes.trim() }) }}>
          <CampoArea rotulo="Observações da loja" rows={5} limite={OBSERVACOES_MAXIMO} value={observacoes}
            onChange={(e) => setObservacoes(e.target.value)}
            dica="Ex.: aos domingos não há entrega; a torta de limão sai só por encomenda. Não mudam as regras." />
          <div className={css.acoes}>
            {mudou && <Botao variante="primario" tipo="submit" disabled={passou || painel.gravacao.salvando === 'observacoes'}>Salvar observações</Botao>}
            <Retorno gravacao={painel.gravacao} qual="observacoes" />
          </div>
        </form>
      ) : fluxo.observacoesLoja && <p>Observações: {fluxo.observacoesLoja}</p>}
      <p className={css.nota}>Tom, nome da dona e nível de sugestão ficam em “Como o Assistente atende”, mais abaixo.</p>
      <details className={css.prompt}>
        <summary>Ver as instruções completas ({fluxo.prompt.length.toLocaleString('pt-BR')} caracteres)</summary>
        <p className={css.nota}>O texto fixo traz as regras de segurança e não muda por aqui.</p>
        <pre tabIndex={0}>{fluxo.prompt}</pre>
      </details>
    </Bloco>
  )
}

// Liga ou desliga uma ferramenta da loja: pergunta antes de desligar e grava só a lista de desligadas.
function alternarFerramenta(ferramenta, fluxo, painel) {
  const ligar = !ferramenta.ligada
  if (!ligar && !window.confirm(`Desligar ${ferramenta.nome}? O Assistente deixa de usar em todas as conversas e passa à equipe o que dependia dela.`)) return
  const desligadas = new Set(desligadasDoFluxo(fluxo.ferramentas))
  if (ligar) desligadas.delete(ferramenta.nome); else desligadas.add(ferramenta.nome)
  painel.salvar('ferramentas', { ferramentasDesligadas: [...desligadas].sort() })
}

function DetalheFerramenta({ ferramenta, fluxo, podeEditar, painel }) {
  const alternar = () => alternarFerramenta(ferramenta, fluxo, painel)
  return (
    <Bloco titulo={rotuloDaFerramenta(ferramenta.nome)} icone={ferramenta.travada ? 'lock' : null}>
      {ferramenta.descricao && <p>{ferramenta.descricao}</p>}
      {ferramenta.travada
        ? <p className={css.nota}>Sempre ligada: é o caminho do cliente até a equipe.</p>
        : (
          <div className={css.chave}>
            <input id="ferramenta-chave" type="checkbox" role="switch" checked={ferramenta.ligada} aria-checked={ferramenta.ligada}
              disabled={!podeEditar || painel.gravacao.salvando === 'ferramentas'} onChange={alternar} />
            <label htmlFor="ferramenta-chave">{ferramenta.ligada ? 'Ligada' : 'Desligada'} na loja</label>
          </div>
        )}
      {!podeEditar && !ferramenta.travada && <p className={css.nota}>Só quem administra a loja liga e desliga ferramentas.</p>}
      <Retorno gravacao={painel.gravacao} qual="ferramentas" />
    </Bloco>
  )
}

function DetalheRoteiro({ indice, roteiro, setRoteiro, roteiroMudou, fluxo, podeEditar, painel, aoSelecionar }) {
  const etapa = roteiro[indice]
  const erro = useMemo(() => erroDoRoteiro(roteiro), [roteiro])
  const mudar = (campos) => setRoteiro(roteiro.map((e, i) => (i === indice ? { ...e, ...campos } : e)))
  const mover = (passo) => { setRoteiro(moverEtapa(roteiro, indice, passo)); aoSelecionar(`roteiro:${indice + passo}`) }
  const tirar = () => { setRoteiro(roteiro.filter((_, i) => i !== indice)); aoSelecionar(roteiro.length > 1 ? `roteiro:${Math.max(0, indice - 1)}` : 'prompt') }
  const nome = useRef(null)
  useEffect(() => { if (etapa.nome === 'Nova etapa' && !etapa.instrucao) nome.current?.select() }, [etapa.nome, etapa.instrucao])

  return (
    <Bloco titulo={`Etapa ${indice + 1} do roteiro`}>
      <p className={css.nota}>O Assistente segue o roteiro na ordem, pulando o que já estiver resolvido. Etapa desligada sai das instruções.</p>
      <CampoTexto ref={nome} rotulo="Nome" value={etapa.nome} limite={ROTEIRO_NOME_MAXIMO} disabled={!podeEditar} onChange={(e) => mudar({ nome: e.target.value })} />
      <CampoArea rotulo="Instrução para o Assistente" rows={4} value={etapa.instrucao} limite={ROTEIRO_INSTRUCAO_MAXIMO} disabled={!podeEditar}
        onChange={(e) => mudar({ instrucao: e.target.value })} />
      <div className={css.chave}>
        <input id="roteiro-chave" type="checkbox" role="switch" checked={etapa.ativo} aria-checked={etapa.ativo} disabled={!podeEditar}
          onChange={(e) => mudar({ ativo: e.target.checked })} />
        <label htmlFor="roteiro-chave">Etapa {etapa.ativo ? 'ligada' : 'desligada'}</label>
      </div>
      {podeEditar && (
        <>
          <div className={css.acoes}>
            <BotaoIcone tom="secundario" icone="arrow-up" rotulo="Subir etapa" onClick={() => mover(-1)} disabled={indice === 0} />
            <BotaoIcone tom="secundario" icone="arrow-down" rotulo="Descer etapa" onClick={() => mover(1)} disabled={indice === roteiro.length - 1} />
            <BotaoIcone tom="secundario" icone="lixeira" rotulo="Tirar etapa" onClick={tirar} />
          </div>
          {erro && <p role="alert" className={css.erro}>{erro}</p>}
          <div className={css.acoes}>
            {roteiroMudou && (
              <>
                <Botao variante="primario" disabled={Boolean(erro) || painel.gravacao.salvando === 'roteiro'} motivo={erro ?? undefined}
                  onClick={() => painel.salvar('roteiro', { roteiro: roteiro.map((e) => ({ nome: e.nome.trim(), instrucao: e.instrucao.trim(), ativo: e.ativo })) })}>
                  Salvar roteiro
                </Botao>
                <Botao variante="secundario" onClick={() => { setRoteiro(fluxo.roteiro); aoSelecionar('prompt') }}>Desfazer</Botao>
              </>
            )}
            {fluxo.roteiroPersonalizado && !roteiroMudou && (
              <Botao variante="secundario" onClick={() => { if (window.confirm('Voltar ao roteiro padrão do EasyStok?')) painel.salvar('roteiro', { roteiro: [] }) }}>Voltar ao padrão</Botao>
            )}
            <Retorno gravacao={painel.gravacao} qual="roteiro" />
          </div>
          {roteiroMudou && <p className={css.pendencia}>Roteiro com mudanças não salvas.</p>}
        </>
      )}
    </Bloco>
  )
}
