import { Suspense, lazy, useEffect, useMemo, useRef, useState } from 'react'
import { useHistoricoAssistente, usePainelAssistente } from '../../../aplicacao/usePainelAssistente'
import { BarraDeAbas } from '../../../componentes/BarraDeAbas'
import { Botao } from '../../../componentes/Botao'
import { CampoArea, CampoTexto } from '../../../componentes/Campo'
import { Icone } from '../../../componentes/Icone'
import { Pilula } from '../../../componentes/Pilula'
import {
  ROTEIRO_ETAPAS_MAXIMO, ROTEIRO_INSTRUCAO_MAXIMO, ROTEIRO_NOME_MAXIMO, cotaDoCampo, cotaParaCampo,
  desfechoDoTurno, desligadasDoFluxo, erroDoRoteiro, formatarTokens, linhaDoUso, mesmoRoteiro, moverEtapa, resumoDoUso,
} from '../../../dominio/painelAssistente'
import { GRUPOS, ORIENTACOES, desenharFluxo, idsDoGrupo } from '../../../dominio/desenhoDoFluxo'
import css from './painelAssistente.module.css'

const OBSERVACOES_MAXIMO = 500
// O canvas (React Flow) só baixa quando a aba Fluxo abre: a biblioteca não entra no bundle inicial do console.
const CanvasDoFluxo = lazy(() => import('./CanvasDoFluxo'))
const ABAS = [
  { id: 'uso', nome: 'Uso e cota', rotulo: 'Uso e cota' },
  { id: 'fluxo', nome: 'Fluxo', rotulo: 'Fluxo' },
  { id: 'historico', nome: 'Histórico', rotulo: 'Histórico' },
]

// #2853: painel do Assistente na Gestão › Atendimento. Uso do mês contra a cota, o fluxo do turno como mapa
// (etapas fixas, instruções, roteiro e ferramentas, cada nó com o seu ajuste) e o histórico de turnos.
// Quem não administra a loja vê tudo e não muda nada.
// `abaAntes` ({ id, rotulo, conteudo }): aba extra na frente, aberta primeiro. O modal da conversa (#2855) põe
// ali a checagem da conversa; as outras abas são as mesmas da Gestão.
export function PainelAssistente({ podeEditar, abaAntes = null, sinalRecarregar = 0 }) {
  const [aba, setAba] = useState(abaAntes?.id ?? 'uso')
  const painel = usePainelAssistente()
  const recarregar = painel.recarregar
  const ultimoSinal = useRef(sinalRecarregar)
  useEffect(() => {
    if (sinalRecarregar !== ultimoSinal.current) { ultimoSinal.current = sinalRecarregar; recarregar() }
  }, [sinalRecarregar, recarregar])
  const abas = abaAntes ? [{ id: abaAntes.id, nome: abaAntes.rotulo, rotulo: abaAntes.rotulo }, ...ABAS] : ABAS

  return (
    <section className={css.painel} {...(abaAntes ? { 'aria-label': 'Painel do Assistente' } : { 'aria-labelledby': 'painel-assistente-titulo' })}>
      {!abaAntes && (
        <div className={css.topo}>
          <h3 id="painel-assistente-titulo">Painel do Assistente</h3>
          <Botao icone="refresh-cw" onClick={painel.recarregar} disabled={painel.carregando}>Atualizar</Botao>
        </div>
      )}
      <BarraDeAbas abas={abas} ativa={aba} aoTrocar={setAba} idPainel="painel-assistente-conteudo" prefixoId="painel-assistente" rotulo="Partes do painel do Assistente" />
      <div id="painel-assistente-conteudo" role="tabpanel" aria-labelledby={`painel-assistente-${aba}`} className={css.conteudo} aria-busy={painel.carregando}>
        {painel.erro && aba !== abaAntes?.id && <p role="alert" className={css.erro}>Não consegui ler o Assistente agora: {painel.erro}</p>}
        {!painel.uso && painel.carregando && aba !== abaAntes?.id && <output className={css.nota}>Carregando…</output>}
        {abaAntes && aba === abaAntes.id && abaAntes.conteudo}
        {aba === 'uso' && painel.uso && <AbaUso uso={painel.uso} podeEditar={podeEditar} painel={painel} />}
        {aba === 'fluxo' && painel.fluxo && <AbaFluxo fluxo={painel.fluxo} podeEditar={podeEditar} painel={painel} />}
        {aba === 'historico' && <AbaHistorico />}
      </div>
    </section>
  )
}

function Retorno({ gravacao, qual }) {
  if (gravacao.salvando === qual) return <output className={css.nota}>Salvando…</output>
  if (gravacao.salvo === qual) return <output className={css.nota}>Salvo. O próximo turno do Assistente já usa.</output>
  if (gravacao.erro?.qual === qual) return <output role="alert" className={css.erro}>Não salvou: {gravacao.erro.mensagem}</output>
  return null
}

function AbaUso({ uso, podeEditar, painel }) {
  const resumo = resumoDoUso(uso)
  const [campo, setCampo] = useState(() => cotaParaCampo(uso.cotaTokensMes))
  const cota = cotaDoCampo(campo)
  const mudou = cota.valor !== null && cota.valor !== (Number(uso.cotaTokensMes) || 0)
  const largura = resumo.comCota ? Math.min(100, resumo.percentual) : 0

  return (
    <div className={css.uso}>
      <div className={`${css.consumo} ${css['consumo_' + resumo.tom]}`} data-tom={resumo.tom}>
        <p className={css.consumoTitulo}><strong>{resumo.titulo}</strong></p>
        {resumo.comCota && (
          <div className={css.barra} role="meter" aria-label="Consumo da cota do mês" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.min(100, resumo.percentual)} aria-valuetext={`${resumo.percentual}% da cota`}>
            <span className={css.barraCheia} style={{ width: `${largura}%` }} />
            <span className={css.barraAlerta} aria-hidden="true" />
          </div>
        )}
        <p className={css.consumoDetalhe}>{linhaDoUso(uso)}</p>
      </div>

      <form className={css.cota} onSubmit={(e) => { e.preventDefault(); if (mudou) painel.salvar('cota', { cotaTokensMes: cota.valor }) }}>
        <CampoTexto
          rotulo="Cota mensal (mil tokens)" inputMode="numeric" value={campo} disabled={!podeEditar}
          onChange={(e) => setCampo(e.target.value)} erro={cota.erro}
          dica={podeEditar ? 'Vazio tira a cota. Em 100% passa para a equipe.' : 'Só quem administra a loja muda a cota.'}
        />
        {podeEditar && (
          <div className={css.acoes}>
            <Botao variante="primario" tipo="submit" disabled={!mudou || painel.gravacao.salvando === 'cota'} motivo={!mudou ? 'Nada mudou.' : undefined}>Salvar cota</Botao>
            <Retorno gravacao={painel.gravacao} qual="cota" />
          </div>
        )}
      </form>
    </div>
  )
}

function AbaFluxo({ fluxo, podeEditar, painel }) {
  const [selecionado, setSelecionado] = useState('entrada')
  const [orientacao, setOrientacao] = useState(() => (window.matchMedia?.('(max-width: 899px)').matches ? ORIENTACOES.vertical : ORIENTACOES.horizontal))
  const [abertos, setAbertos] = useState([])
  const [foco, setFoco] = useState({ ids: null })
  const [roteiro, setRoteiro] = useState(fluxo.roteiro)
  const detalhe = useRef(null)
  const roteiroMudou = !mesmoRoteiro(roteiro, fluxo.roteiro)
  // Depois de salvar, o fluxo novo da API vira a base; rascunho sem mudança acompanha.
  const [base, setBase] = useState(fluxo.roteiro)
  if (base !== fluxo.roteiro) {
    setBase(fluxo.roteiro)
    if (mesmoRoteiro(roteiro, base)) setRoteiro(fluxo.roteiro)
  }
  const desenho = useMemo(() => desenharFluxo({ fluxo, roteiro, orientacao, abertos }), [fluxo, roteiro, orientacao, abertos])

  function selecionar(id) {
    setSelecionado(id)
    if (!id.startsWith('grupo:') && window.matchMedia?.('(max-width: 899px)').matches) {
      requestAnimationFrame(() => detalhe.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }))
    }
  }

  // Abre ou fecha o grupo (Verificações, Roteiro, Ferramentas) e enquadra o que apareceu.
  function alternarGrupo(chave, abrir, roteiroNovo = roteiro) {
    const proximos = abrir ? [...abertos.filter((g) => g !== chave), chave] : abertos.filter((g) => g !== chave)
    setAbertos(proximos)
    setFoco({ ids: abrir ? idsDoGrupo(desenharFluxo({ fluxo, roteiro: roteiroNovo, orientacao, abertos: proximos }), chave) : null })
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
    selecionar(dados.id)
    if (dados.tipo === 'ferramenta') alternarFerramenta({ nome: dados.titulo, ligada: dados.ligada }, fluxo, painel)
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

  return (
    <div className={css.fluxoCaixa}>
      <div className={css.barraDoFluxo}>
        <div role="group" aria-label="Direção do desenho" className={css.seletor}>
          {[[ORIENTACOES.horizontal, 'Horizontal'], [ORIENTACOES.vertical, 'Vertical']].map(([valor, rotulo]) => (
            <Botao key={valor} variante={orientacao === valor ? 'primario' : 'secundario'} aria-pressed={orientacao === valor} onClick={() => trocarOrientacao(valor)}>{rotulo}</Botao>
          ))}
        </div>
        {podeEditar && roteiro.length < ROTEIRO_ETAPAS_MAXIMO && <Botao variante="texto" icone="mais" onClick={acrescentarEtapa}>Acrescentar etapa</Botao>}
        <p className={css.nota}>Toque num nó para ajustar. Verificações, Roteiro e Ferramentas abrem ao tocar.</p>
      </div>
      <div className={css.fluxo}>
        <Suspense fallback={<output className={css.nota}>Carregando o desenho…</output>}>
          <CanvasDoFluxo desenho={desenho} dadosDoNo={dadosDoNo} foco={foco} rotuloDoDesenho="Fluxo do Assistente, na ordem do turno" />
        </Suspense>
        <aside ref={detalhe} className={css.detalhe} aria-label="Ajuste do item escolhido" aria-live="polite">
          <Detalhe
            id={selecionado} fluxo={fluxo} podeEditar={podeEditar} painel={painel}
            roteiro={roteiro} setRoteiro={setRoteiro} roteiroMudou={roteiroMudou} aoSelecionar={selecionar}
          />
        </aside>
      </div>
    </div>
  )
}

function Detalhe({ id, fluxo, podeEditar, painel, roteiro, setRoteiro, roteiroMudou, aoSelecionar }) {
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
    return <Bloco titulo="Assistente IA" icone="sparkles">
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
  if (id === 'prompt') return <DetalhePrompt fluxo={fluxo} podeEditar={podeEditar} painel={painel} />
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
      <h4>{icone && <Icone nome={icone} tamanho={20} />} {titulo}</h4>
      {children}
    </div>
  )
}

function DetalhePrompt({ fluxo, podeEditar, painel }) {
  const [observacoes, setObservacoes] = useState(fluxo.observacoesLoja ?? '')
  const mudou = observacoes.trim() !== (fluxo.observacoesLoja ?? '').trim()
  const passou = observacoes.length > OBSERVACOES_MAXIMO
  return (
    <Bloco titulo="Instruções da loja" icone="settings">
      <p>O texto fixo traz as regras de segurança e não muda por aqui. A loja escreve as observações, que entram no começo das instruções.</p>
      {podeEditar ? (
        <form onSubmit={(e) => { e.preventDefault(); if (mudou && !passou) painel.salvar('observacoes', { observacoesLoja: observacoes.trim() }) }}>
          <CampoArea rotulo="Observações da loja" rows={5} limite={OBSERVACOES_MAXIMO} value={observacoes}
            onChange={(e) => setObservacoes(e.target.value)}
            dica="Ex.: aos domingos não há entrega; a torta de limão sai só por encomenda. Não mudam as regras." />
          <div className={css.acoes}>
            <Botao variante="primario" tipo="submit" disabled={!mudou || passou || painel.gravacao.salvando === 'observacoes'} motivo={!mudou ? 'Nada mudou.' : undefined}>Salvar observações</Botao>
            <Retorno gravacao={painel.gravacao} qual="observacoes" />
          </div>
        </form>
      ) : fluxo.observacoesLoja && <p>Observações: {fluxo.observacoesLoja}</p>}
      <p className={css.nota}>Tom, nome da dona e nível de sugestão ficam em “Como o Assistente atende”, mais abaixo.</p>
      <details className={css.prompt}>
        <summary>Ver as instruções completas ({fluxo.prompt.length.toLocaleString('pt-BR')} caracteres)</summary>
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
    <Bloco titulo={ferramenta.nome} icone={ferramenta.travada ? 'lock' : null}>
      <p>{ferramenta.descricao}</p>
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
            <Botao icone="arrow-up" onClick={() => mover(-1)} disabled={indice === 0}>Subir</Botao>
            <Botao icone="arrow-down" onClick={() => mover(1)} disabled={indice === roteiro.length - 1}>Descer</Botao>
            <Botao variante="texto" icone="lixeira" onClick={tirar}>Tirar etapa</Botao>
          </div>
          {erro && <p role="alert" className={css.erro}>{erro}</p>}
          <div className={css.acoes}>
            <Botao variante="primario" disabled={!roteiroMudou || Boolean(erro) || painel.gravacao.salvando === 'roteiro'}
              motivo={!roteiroMudou ? 'Nada mudou no roteiro.' : erro ?? undefined}
              onClick={() => painel.salvar('roteiro', { roteiro: roteiro.map((e) => ({ nome: e.nome.trim(), instrucao: e.instrucao.trim(), ativo: e.ativo })) })}>
              Salvar roteiro
            </Botao>
            {roteiroMudou && <Botao variante="texto" onClick={() => { setRoteiro(fluxo.roteiro); aoSelecionar('prompt') }}>Desfazer</Botao>}
            {fluxo.roteiroPersonalizado && !roteiroMudou && (
              <Botao variante="texto" onClick={() => { if (window.confirm('Voltar ao roteiro padrão do EasyStok?')) painel.salvar('roteiro', { roteiro: [] }) }}>Voltar ao padrão</Botao>
            )}
            <Retorno gravacao={painel.gravacao} qual="roteiro" />
          </div>
          {roteiroMudou && <p className={css.pendencia}>Roteiro com mudanças não salvas.</p>}
        </>
      )}
    </Bloco>
  )
}

function AbaHistorico() {
  const { carregando, dados, erro, pagina, setPagina } = useHistoricoAssistente(true)
  const total = dados?.total ?? 0
  const paginas = Math.max(1, Math.ceil(total / 20))
  if (erro) return <p role="alert" className={css.erro}>Não consegui ler o histórico: {erro}</p>
  if (!dados) return <output className={css.nota}>Carregando…</output>
  if (total === 0) return <p className={css.nota}>Nenhum turno do Assistente registrado ainda.</p>
  return (
    <div className={css.historico} aria-busy={carregando}>
      <ul className={css.turnos}>
        {dados.itens.map((t) => {
          const desfecho = desfechoDoTurno(t.desfecho)
          return (
            <li key={t.id} className={css.turno}>
              <div className={css.turnoTopo}>
                <Pilula tom={desfecho.tom} tamanho="inline">{desfecho.rotulo}</Pilula>
                <time dateTime={t.criadoEm}>{new Date(t.criadoEm).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })}</time>
                <span className={css.nota}>{formatarTokens(t.tokensEntrada + t.tokensSaida)} tokens · {(t.duracaoMs / 1000).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} s</span>
              </div>
              {t.perguntaCliente && <p><strong>Cliente:</strong> {t.perguntaCliente}</p>}
              {t.resposta && <p><strong>Assistente:</strong> {t.resposta}</p>}
              {t.ferramentas?.length > 0 && <p className={css.nota}>Ferramentas: {t.ferramentas.map((f) => f.nome).join(', ')}</p>}
            </li>
          )
        })}
      </ul>
      <div className={css.acoes}>
        <Botao icone="arrow-left" onClick={() => setPagina(pagina - 1)} disabled={pagina <= 1 || carregando}>Mais novos</Botao>
        <span className={css.nota}>Página {pagina} de {paginas}</span>
        <Botao onClick={() => setPagina(pagina + 1)} disabled={pagina >= paginas || carregando}>Mais antigos</Botao>
      </div>
    </div>
  )
}
