import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { PainelMovel, usePainelMovel } from '../../componentes/PainelMovel'
import { Icone } from '../../componentes/Icone'
import { lerPosicaoPostit } from '../../aplicacao/posicoesDosPostits'
import { paginaNotasDaMesa } from '../../aplicacao/mesaDeNotas'
import { useMesaDeNotas } from './MesaDeNotas'
import css from './NotasDoCliente.module.css'
const SEM_NOTAS = []

function NotaDoCliente({ conversaId, nota, indice }) {
  const controle = usePainelMovel(conversaId, `nota:${nota.id}`, { posicaoInicial: () => lerPosicaoPostit(conversaId, nota.id) })
  const [focar, setFocar] = useState(false)
  const botao = useRef(null)
  function soltar(e) {
    const r = e.currentTarget.closest('article').getBoundingClientRect()
    setFocar(true)
    controle.soltar({ x: r.left + (indice % 6) * 12, y: r.top })
  }
  return <>
    <article className={controle.solto ? css.referencia : css.nota}>
      {controle.solto ? <><Icone nome="nota" tamanho={18} /><span>Nota solta{nota.autor ? ` · ${nota.autor.trim().split(/\s+/)[0]}` : ''}</span></>
        : <><small>Nota{nota.autor ? ` · ${nota.autor}` : ''}</small><p className={css.texto}>{nota.texto}</p></>}
      {nota.id && <button type="button" ref={botao} className={controle.solto ? css.recolher : css.soltar}
        aria-label={controle.solto ? 'Recolher nota à ficha' : undefined} title={controle.solto ? 'Recolher à ficha' : undefined}
        onClick={(e) => controle.solto ? controle.recolher() : soltar(e)}>
        {controle.solto ? <Icone nome="arrow-left" tamanho={18} /> : 'Soltar na tela'}
      </button>}
      {controle.erro && !controle.solto && <output className={css.erro}>{controle.erro}</output>}
    </article>
    <PainelMovel controle={controle} titulo={nota.autor ? `Nota · ${nota.autor}` : 'Nota'} larguraInicial={280}
      className={css.solto} classeCorpo={css.corpo} focar={focar} atributos={{ 'data-postit-solto': nota.id }}
      aoRecolher={() => requestAnimationFrame(() => botao.current?.focus({ preventScroll: true }))}>
      <p className={css.texto}>{nota.texto}</p>{nota.em && <small>{nota.em}</small>}
    </PainelMovel>
  </>
}

function NotaRegistrada({ mesa, conversaId, clienteId, clienteNome, nota }) {
  const solta = mesa.estaSolta(clienteId, nota.id)
  return <article className={solta ? css.referencia : css.nota}>
    {solta ? <><Icone nome="nota" tamanho={18} /><span>Nota solta{nota.autor ? ` · ${nota.autor.trim().split(/\s+/)[0]}` : ''}</span></>
      : <><small>Nota{nota.autor ? ` · ${nota.autor}` : ''}</small><p className={css.texto}>{nota.texto}</p></>}
    {nota.id && <button type="button" className={solta ? css.recolher : css.soltar} data-foco-reserva={mesa.chaveDeFoco(clienteId, nota.id)}
      aria-label={solta ? 'Recolher nota à ficha' : undefined} title={solta ? 'Recolher à ficha' : undefined}
      onClick={(e) => solta ? mesa.recolher(clienteId, nota.id) : mesa.soltar({ conversaId, clienteId, clienteNome, nota }, e)}>
      {solta ? <Icone nome="arrow-left" tamanho={18} /> : 'Soltar na tela'}</button>}
  </article>
}

export function NotasDoCliente({ conversaId, clienteId, clienteNome, fonteApi = false, notas = SEM_NOTAS }) {
  const mesa = useMesaDeNotas()
  const [pagina, setPagina] = useState({ clienteId, itens: [], proximaPagina: 1, temMais: true, carregando: false, erro: null })
  const pedido = useRef(null)
  const identidade = mesa?.identidade ?? null
  const carregar = useCallback(async (numero) => {
    if (!fonteApi || !clienteId) return
    pedido.current?.abort()
    const controle = new AbortController(); pedido.current = controle
    setPagina(antes => ({ ...(numero === 1 ? { clienteId, itens: [], proximaPagina: 1, temMais: true } : antes), carregando: true, erro: null }))
    try {
      const resultado = await paginaNotasDaMesa(clienteId, numero, { sinal: controle.signal, identidadeEsperada: identidade })
      if (!controle.signal.aborted) setPagina(antes => ({ clienteId, itens: [...(numero === 1 ? [] : antes.itens), ...resultado.itens],
        proximaPagina: resultado.proximaPagina, temMais: resultado.temMais, carregando: false, erro: null }))
    } catch (erro) {
      if (!controle.signal.aborted) setPagina(antes => ({ ...antes, carregando: false, erro: erro.message || 'Não foi possível carregar as notas.' }))
    }
  }, [clienteId, fonteApi, identidade])
  useEffect(() => {
    carregar(1)
    return () => pedido.current?.abort()
  }, [carregar])
  const carregadas = pagina.clienteId === clienteId ? pagina.itens : SEM_NOTAS
  const lista = useMemo(() => [...new Map([...notas, ...carregadas].map(n => [n.id, n])).values()]
    .sort((a, b) => (Date.parse(b.criadoEm) || 0) - (Date.parse(a.criadoEm) || 0) || String(b.id).localeCompare(String(a.id))), [notas, carregadas])
  const atualizar = mesa?.atualizar
  useEffect(() => { atualizar?.(clienteId, clienteNome, lista, conversaId) }, [atualizar, clienteId, clienteNome, conversaId, lista])
  return <section className={css.coluna} data-destino-nota={conversaId} aria-label="Notas internas do cliente">
    {lista.map((nota, indice) => mesa
      ? <NotaRegistrada key={nota.id ?? indice} mesa={mesa} conversaId={conversaId} clienteId={clienteId} clienteNome={clienteNome} nota={nota} />
      : <NotaDoCliente key={nota.id ?? indice} conversaId={conversaId} nota={nota} indice={indice} />)}
    {fonteApi && pagina.erro && <output className={css.erro}>{pagina.erro}</output>}
    {fonteApi && pagina.temMais && <button type="button" className={css.soltar} disabled={pagina.carregando}
      onClick={() => carregar(pagina.proximaPagina ?? 1)}>{pagina.carregando ? 'Carregando notas…' : pagina.erro ? 'Tentar novamente' : 'Carregar mais notas'}</button>}
  </section>
}
