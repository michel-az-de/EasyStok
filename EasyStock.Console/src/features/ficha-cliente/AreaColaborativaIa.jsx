import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { AvatarAssistente } from '../../componentes/AvatarAssistente'
import { Botao } from '../../componentes/Botao'
import { BotaoIcone } from '../../componentes/BotaoIcone'
import { CampoTexto } from '../../componentes/Campo'
import { useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { identidadeDaMontagem, organizarFicha } from '../../aplicacao/montagemFicha'
import { comandaDaFichaIa, tituloDoBlocoIa } from '../../dominio/montagemFicha'
import { horaCurta, moeda } from '../../dominio/formato'
import { conversaEncerrada } from '../../dominio/conversa'
import css from './AreaColaborativaIa.module.css'

export function BlocoColaborativoIa({ bloco, conversaId = bloco.conversaId, aoAbrirComanda, aoProporNota, aoCalcularFrete, aoAbrirCardapio, aoRascunho, aoSoltar }) {
  const { selecionada, fonteApi } = useAtendimento()
  const { cardapio } = useCatalogo()
  const [erro, setErro] = useState(null)
  const [ocupado, setOcupado] = useState(false)
  const executando = useRef(false)
  const atual = useRef(null)
  const identidade = identidadeDaMontagem()
  const aberta = !conversaEncerrada(selecionada)
  const valido = Boolean(fonteApi && aberta && selecionada?.id === conversaId && bloco.identidadeSessao === identidade)
  useLayoutEffect(() => { atual.current = { conversaId: selecionada?.id, identidade, aberta } }, [selecionada?.id, identidade, aberta])
  const aindaAtual = () => valido && atual.current?.conversaId === conversaId
    && atual.current?.aberta && atual.current?.identidade === bloco.identidadeSessao && identidadeDaMontagem() === bloco.identidadeSessao
  async function agir(callback, ...args) {
    if (!callback || !aindaAtual() || executando.current) return
    executando.current = true; setOcupado(true); setErro(null)
    try {
      if (await callback(...args) === false && aindaAtual()) setErro('A ação não foi confirmada. Confira antes de tentar de novo.')
    } catch (e) { if (aindaAtual()) setErro(e.message || 'Não foi possível concluir a ação.') }
    finally { executando.current = false; if (aindaAtual()) setOcupado(false) }
  }
  const comanda = bloco.tipo === 'comanda' && valido ? comandaDaFichaIa(selecionada, cardapio, fonteApi) : null
  const acao = bloco.tipo === 'comanda' ? { rotulo: 'Ver comanda', callback: aoAbrirComanda, args: [] }
    : bloco.tipo === 'nota' ? { rotulo: 'Revisar nota', callback: aoProporNota, args: [bloco.texto] }
      : bloco.tipo === 'frete' ? { rotulo: 'Consultar frete', callback: aoCalcularFrete, args: [] }
        : bloco.acao === 'abrir_cardapio' ? { rotulo: 'Abrir cardápio', callback: aoAbrirCardapio, args: [] }
          : bloco.acao === 'rascunho' ? { rotulo: 'Usar no rascunho', callback: aoRascunho, args: [bloco.texto] } : null
  if (!valido) return null
  return <article className={css.bloco} aria-label={tituloDoBlocoIa(bloco)}>
    <header className={css.cabecalhoBloco}>
      <h4>{tituloDoBlocoIa(bloco)}</h4>
      {aoSoltar && <BotaoIcone icone="grip-vertical" rotulo={`Soltar ${tituloDoBlocoIa(bloco).toLocaleLowerCase('pt-BR')}`}
        disabled={ocupado} onClick={() => agir(aoSoltar, bloco)} />}
    </header>
    <p className={bloco.tipo === 'nota' ? css.nota : css.texto}>{bloco.texto}</p>
    {bloco.tipo === 'comanda' && (comanda ? <div className={css.comanda}>
      <strong>{comanda.numero ? `Comanda ${comanda.numero}` : 'Pedido em montagem'} · {comanda.nome}</strong>
      <ul>{comanda.itens.slice(0, 4).map((item, i) => <li key={`${item.id}:${i}`}>{item.quantidade} × {item.nome}</li>)}</ul>
      {comanda.itens.length > 4 && <p>{comanda.itens.length - 4} outros itens na comanda.</p>}
      {comanda.total != null && <p className={css.total}>{comanda.rotuloTotal} <strong>{moeda(comanda.total)}</strong></p>}
    </div> : <p>Nenhum pedido nesta conversa.</p>)}
    {bloco.tipo === 'frete' && <p className={css.endereco}>{selecionada.cliente?.endereco || 'Confira o endereço do cliente para consultar o frete.'}</p>}
    {acao && <Botao variante="texto" disabled={!acao.callback || ocupado} onClick={() => agir(acao.callback, ...acao.args)}>{acao.rotulo}</Botao>}
    {erro && <p role="alert" className={css.erro}>{erro}</p>}
  </article>
}

export function AreaColaborativaIa({ conversaId, blocosSoltos = [], ...callbacks }) {
  const { selecionada, fonteApi } = useAtendimento()
  const [orientacao, setOrientacao] = useState('')
  const [montagem, setMontagem] = useState(null)
  const [erro, setErro] = useState(null)
  const [carregando, setCarregando] = useState(false)
  const pedido = useRef(null)
  const contexto = useRef(null)
  const sequencia = useRef(0)
  const identidade = identidadeDaMontagem()
  const aberta = !conversaEncerrada(selecionada)
  useLayoutEffect(() => { contexto.current = { conversaId, identidade, aberta } }, [conversaId, identidade, aberta])
  useEffect(() => {
    setOrientacao(''); setMontagem(null); setErro(null); setCarregando(false)
    return () => { pedido.current?.abort(); pedido.current = null; sequencia.current++ }
  }, [conversaId, identidade, aberta])
  const mesmaConversa = () => contexto.current?.conversaId === conversaId && contexto.current?.identidade === identidade
    && contexto.current?.aberta && identidadeDaMontagem() === identidade
  async function organizar(evento) {
    evento.preventDefault()
    if (!fonteApi || selecionada?.id !== conversaId || pedido.current || !mesmaConversa()) return
    const controle = new AbortController(), numero = ++sequencia.current
    pedido.current = controle; setCarregando(true); setErro(null)
    try {
      const proposta = await organizarFicha(conversaId, orientacao, { sinal: controle.signal, identidadeEsperada: identidade })
      if (!controle.signal.aborted && mesmaConversa() && numero === sequencia.current) setMontagem(proposta)
    } catch (e) {
      if (!controle.signal.aborted && mesmaConversa() && numero === sequencia.current) setErro(e.message || 'Não foi possível organizar a ficha com IA agora.')
    } finally {
      if (numero === sequencia.current) { pedido.current = null; if (!controle.signal.aborted) setCarregando(false) }
    }
  }
  const visivel = montagem?.conversaId === conversaId && montagem.identidadeSessao === identidade ? montagem : null
  const movidos = new Set(blocosSoltos)
  if (!aberta) return null
  return <section className={css.area} aria-label="Organizar a ficha com IA">
    <form onSubmit={organizar}>
      <header className={css.cabecalho}><AvatarAssistente tamanho={32} decorativo /><h3>Assistente</h3>
        <Botao type="submit" data-organizar-ficha-ia={conversaId} disabled={!fonteApi || selecionada?.id !== conversaId || carregando}>{carregando ? 'Organizando…' : 'Organizar com IA'}</Botao>
      </header>
      <details className={css.orientar}><summary>Orientar</summary>
        <CampoTexto rotulo="Orientação (opcional)" placeholder="Ex.: preparar a retirada de sábado" maxLength={2000}
          value={orientacao} disabled={carregando} onChange={(e) => setOrientacao(e.target.value)} />
      </details>
    </form>
    {!fonteApi && <p>Conecte a loja para usar a IA.</p>}
    {carregando && <p role="status">Organizando a ficha…</p>}
    {erro && <p role="alert" className={css.erro}>{erro}</p>}
    {visivel && <><p className={css.data}>Proposta das {horaCurta(visivel.geradoEm)}</p><div className={css.blocos}>
      {visivel.blocos.filter((b) => !movidos.has(`${visivel.geracaoId}:${b.id}`)).map((b) => <BlocoColaborativoIa
        key={`${visivel.geracaoId}:${b.id}`} {...callbacks} conversaId={conversaId}
        bloco={{ ...b, conversaId, geracaoId: visivel.geracaoId, geradoEm: visivel.geradoEm, identidadeSessao: visivel.identidadeSessao }} />)}
    </div></>}
  </section>
}
