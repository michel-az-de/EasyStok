import { useEffect, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { useCatalogo } from '../../../aplicacao/contextos'
import { assinaturaDoPlano, comprasGuardadas, erroDasPorcoes, gerarCompras, guardarCompras, itensDeCompra, lerSugestao, levarParaProducao, motivosDosPendentes, planejar } from '../../../aplicacao/planejamento'
import { lerInsumos } from '../../../aplicacao/insumos'
import { hashDoModulo } from '../../../dominio/modulos'
import { moeda } from '../../../dominio/formato'
import css from '../cardapio/abaCardapio.module.css'

// M2 › Planejamento (M2.5, #1502). O EasyStok sugere quanto produzir por prato até a data:
// mínimo + pedidos agendados + descoberto − saldo (D-M2-05). Ela muda as porções, calcula os
// insumos e as faltas (mesma conta da calculadora do PWA), gera a lista de compras e leva as
// porções para a Produção do dia. Nada aqui mexe no estoque.
const qtd = (n) => Number(n ?? 0).toLocaleString('pt-BR', { maximumFractionDigits: 3 })
const porQue = (p) => [
  p.minimo ? `mínimo ${qtd(p.minimo)}` : null,
  p.agendados ? `${qtd(p.agendados)} ${p.agendados === 1 ? 'porção agendada' : 'porções agendadas'}` : null,
  p.descoberto ? `${qtd(p.descoberto)} ${p.descoberto === 1 ? 'porção vendida sem saldo' : 'porções vendidas sem saldo'}` : null,
  `saldo ${qtd(p.saldo)}`,
].filter(Boolean).join(' · ')

export function AbaPlanejamentoApi({ aoAbrirAba } = {}) {
  const { cardapio } = useCatalogo()
  const [ate, setAte] = useState('')
  // #1510: a data que busca é só a que ela escolhe; a que vem da resposta só preenche o campo
  // (antes disparava uma 2ª busca que apagava as porções já editadas).
  const [consulta, setConsulta] = useState('')
  const [tentativa, setTentativa] = useState(0)
  const [carga, setCarga] = useState({ estado: 'carregando', erro: null })
  const [linhas, setLinhas] = useState([])
  const [plano, setPlano] = useState(null)
  const [aviso, setAviso] = useState(null)
  const [ocupado, setOcupado] = useState(false)
  // #1585: a última lista gerada, com as porções e a data que a pediram.
  const [compras, setCompras] = useState(comprasGuardadas)

  useEffect(() => {
    let vivo = true
    lerSugestao(consulta || null)
      .then((r) => {
        if (!vivo) return
        if (r.ate) setAte((atual) => atual || r.ate)
        setLinhas(r.pratos.map((p) => ({ ...p, porcoes: String(p.sugestao) })))
        setPlano(null)
        setCarga({ estado: 'ok', erro: null })
      })
      .catch((e) => { if (vivo) setCarga({ estado: 'erro', erro: e.message }) })
    return () => { vivo = false }
  }, [consulta, tentativa])

  // #1510: porção mudada invalida o cálculo; a lista de compras não sai com faltas antigas.
  const mudar = (sku) => (e) => {
    setLinhas((atual) => atual.map((l) => (l.sku === sku ? { ...l, porcoes: e.target.value } : l)))
    setPlano(null)
  }

  async function calcular() {
    if (ocupado) return
    const erro = erroDasPorcoes(linhas)
    if (erro) { setAviso(erro); return }
    setOcupado(true)
    setAviso(null)
    try {
      setPlano(await planejar(linhas, cardapio ?? []))
    } catch (e) {
      setAviso(e.message)
    } finally {
      setOcupado(false)
    }
  }

  async function comprar() {
    if (ocupado) return
    setOcupado(true)
    const cadastrados = await lerInsumos().catch(() => [])
    const itens = itensDeCompra(plano?.insumos ?? [], cadastrados)
    const r = await gerarCompras(itens, ate)
    setOcupado(false)
    if (r.ok) setCompras(guardarCompras(linhas, ate, { nome: r.nome, itens }))
    setAviso(r.ok ? `Lista "${r.nome ?? 'de compras'}" criada com ${r.itens} ${r.itens === 1 ? 'item' : 'itens'}.` : r.erro)
  }

  function lancar() {
    if (levarParaProducao(linhas) === 0) { setAviso('Diga quantas porções de ao menos um prato.'); return }
    if (aoAbrirAba) aoAbrirAba('producao-do-dia')
    else window.location.hash = hashDoModulo('producao', 'producao')
  }

  if (carga.estado === 'carregando') return <output className={css.descricao}>Calculando a sugestão…</output>
  if (carga.estado === 'erro') return (
    <div className={css.aba} role="alert">
      <p className={css.aviso}>Não consegui ler a sugestão: {carga.erro}</p>
      <Botao icone="refresh-cw" onClick={() => { setCarga({ estado: 'carregando', erro: null }); setTentativa((t) => t + 1) }}>Tentar novamente</Botao>
    </div>
  )

  // A lista já gerada vale enquanto as porções e a data forem as mesmas.
  const listaGerada = compras?.assinatura === assinaturaDoPlano(linhas, ate) ? compras : null

  return (
    <div className={css.aba}>
      <p className={css.descricao}>
        Quanto produzir de cada prato para atender os pedidos agendados até a data, cobrir o que saiu sem saldo e
        voltar ao mínimo. Mude as porções à vontade: calcular não mexe no estoque.
      </p>
      <label className={css.dataPlanejamento}>
        <span className={css.nome}>Pedidos até</span>
        <input className={css.busca} type="date" aria-label="Pedidos até" disabled={ocupado} value={ate} onChange={(e) => { setAte(e.target.value); setConsulta(e.target.value) }} />
      </label>
      {aviso && <p className={css.aviso} role="alert">{aviso} <Botao variante="texto" onClick={() => setAviso(null)}>Fechar</Botao></p>}

      {linhas.length === 0 ? (
        <p className={css.descricao}>Nenhum prato ligado ao estoque ainda: a primeira produção liga o prato. Lance em <a className={css.link} href={hashDoModulo('producao', 'producao')}>Produção do dia</a>.</p>
      ) : (
        <ul className={css.lista}>
          {linhas.map((l) => (
            <li key={l.sku} className={css.linha}>
              <div className={css.resumo}>
                <span className={css.nome}>{l.nome}</span>
                <span className={css.detalhe}>{porQue(l)}</span>
              </div>
              <div className={css.estados}>
                {l.sugestao > 0 && <Pilula tom="ok" fina>sugestão {qtd(l.sugestao)}</Pilula>}
              </div>
              <div className={css.campoQuantidade}>
                <CampoTexto disabled={ocupado} rotulo="Porções" inputMode="numeric" aria-label={`Porções de ${l.nome}`} value={l.porcoes} onChange={mudar(l.sku)} />
              </div>
            </li>
          ))}
        </ul>
      )}

      <div className={css.topo}>
        <Botao variante="primario" icone="nota" onClick={calcular} disabled={ocupado || linhas.length === 0}>Calcular insumos</Botao>
        <Botao icone="cooking-pot" onClick={lancar} disabled={ocupado || linhas.length === 0}>Revisar na produção</Botao>
      </div>

      {plano && (
        <section className={css.aba} aria-label="Insumos do planejamento">
          {plano.insumos.length === 0 ? (
            <p className={css.descricao}>Os pratos planejados não têm receita. Monte as <a className={css.link} href={hashDoModulo('producao', 'receitas')}>Receitas</a> para calcular os insumos.</p>
          ) : (
            <ul className={css.lista}>
              {plano.insumos.map((i) => (
                <li key={i.insumoId} className={css.linha}>
                  <div className={css.resumo}>
                    <span className={css.nome}>{i.nome}</span>
                    <span className={css.detalhe}>precisa {qtd(i.precisa)} {i.unidade} · tem {qtd(i.saldo)} {i.unidadeSaldo}{i.aviso ? ` · ${i.aviso}` : ''}</span>
                  </div>
                  <div className={css.estados}>
                    {i.falta > 0
                      ? <Pilula tom="aviso" fina>falta {qtd(i.falta)} {i.unidade}</Pilula>
                      : <Pilula tom="ok" fina>tem</Pilula>}
                    {i.custo != null && <span className={css.detalhe}>{moeda(i.custo)}</span>}
                  </div>
                </li>
              ))}
            </ul>
          )}
          {motivosDosPendentes(plano.pendentes, linhas).map((texto) => <p key={texto} className={css.descricao}>{texto}</p>)}
          <div className={css.topo}>
            {plano.custoTotal != null && <span className={css.detalhe}>Custo estimado {moeda(plano.custoTotal)}</span>}
            <Botao icone="shopping-cart" onClick={comprar} disabled={ocupado || listaGerada !== null}>{listaGerada ? 'Lista de compras gerada' : 'Gerar lista de compras'}</Botao>
          </div>
          {listaGerada && (
            <section className={css.secao} aria-label="Lista de compras gerada">
              <h3 className={css.tituloSecao}>{listaGerada.nome ?? 'Lista de compras'}</h3>
              <ul className={css.lista}>
                {listaGerada.itens.map((i) => (
                  <li key={i.produtoId} className={css.linha}>
                    <div className={css.resumo}>
                      <span className={css.nome}>{i.texto}</span>
                      <span className={css.detalhe}>{i.categoria === 'Mínimo' ? 'repõe o mínimo' : 'falta para a produção'}</span>
                    </div>
                    <span className={css.preco}>{qtd(i.quantidade)} {i.unidade}</span>
                  </li>
                ))}
              </ul>
              <p className={css.descricao}>Para gerar outra lista, mude as porções ou a data.</p>
            </section>
          )}
        </section>
      )}
    </div>
  )
}
