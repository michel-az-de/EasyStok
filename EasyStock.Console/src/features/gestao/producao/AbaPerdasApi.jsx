import { useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Pilula } from '../../../componentes/Pilula'
import { usePerdasApi } from '../../../aplicacao/usePerdasApi'
import { MOTIVOS_PERDA } from '../../../aplicacao/perdas'
import { moeda } from '../../../dominio/formato'
import css from '../cardapio/abaCardapio.module.css'

// M2 › Perdas (M2.6, #1511). Lançar perda de prato ou insumo com o motivo da lista (D-M2-02; texto
// só em "Outro"). Sai por FEFO e grava o custo do lote; acima de R$ 50 é do Gerente (D-M2-06).
// O resumo do período não conta ajuste de contagem nem a baixa de insumo da produção. Lote vencido
// aparece como sugestão e só sai quando ela confirma. Desfazer é o estorno (Gerente).
const qtd = (n) => Number(n ?? 0).toLocaleString('pt-BR', { maximumFractionDigits: 3 })
const quando = (iso) => (iso ? new Date(iso).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : '')
const vazio = { produtoId: '', quantidade: '', motivo: 'PerdaNoPreparo', texto: '' }

export function AbaPerdasApi() {
  const gestao = usePerdasApi()
  const [rascunho, setRascunho] = useState(vazio)
  const [enviando, setEnviando] = useState(false)

  if (gestao.estado === 'carregando') return <p className={css.descricao}>Carregando as perdas…</p>
  if (gestao.estado === 'erro' && !gestao.resumo) {
    return (
      <div className={css.aba} role="alert">
        <p>Não consegui ler as perdas: {gestao.erro}</p>
        <Botao onClick={gestao.recarregar}>Tentar de novo</Botao>
      </div>
    )
  }

  const mudar = (campo) => (e) => setRascunho((atual) => ({ ...atual, [campo]: e.target.value }))
  const escolhido = gestao.opcoes.find((o) => o.produtoId === rascunho.produtoId)

  async function lancar(evento) {
    evento.preventDefault()
    setEnviando(true)
    const ok = await gestao.lancar({ ...rascunho, nome: escolhido?.nome })
    setEnviando(false)
    if (ok) setRascunho(vazio)
  }

  const resumo = gestao.resumo
  return (
    <div className={css.aba}>
      <p className={css.descricao}>
        O que se perdeu e por quê. A perda sai do lote mais antigo e entra com o custo dele. Acima de {moeda(50)} quem
        lança é o Gerente. Ajuste de contagem não é perda e não entra aqui.
      </p>
      {gestao.aviso && <p className={css.aviso} role="alert">{gestao.aviso} <Botao variante="texto" onClick={gestao.fecharAviso}>Fechar</Botao></p>}
      {gestao.feito && <output className={css.aviso}>{gestao.feito} <Botao variante="texto" onClick={gestao.fecharAviso}>Fechar</Botao></output>}

      {gestao.vencidos.length > 0 && (
        <section className={css.aba} aria-label="Lotes vencidos">
          <span className={css.nome}>Vencidos com saldo</span>
          <ul className={css.lista}>
            {gestao.vencidos.map((v) => (
              <li key={v.itemEstoqueId} className={css.linha}>
                <div className={css.resumo}>
                  <span className={css.nome}>{v.nome}</span>
                  <span className={css.detalhe}>{v.lote ?? 'sem lote'} · {qtd(v.quantidade)} · venceu há {v.diasVencido} dia(s) · {moeda(v.valor)}</span>
                </div>
                <div className={css.acoes}>
                  <Botao variante="texto" onClick={() => gestao.lancarVencido(v)}>Lançar como vencido</Botao>
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      <form className={css.aba} onSubmit={lancar} aria-label="Lançar perda">
        <div className={css.topo}>
          <select className={css.busca} aria-label="Prato ou insumo" value={rascunho.produtoId} onChange={mudar('produtoId')}>
            <option value="">Prato ou insumo</option>
            {gestao.opcoes.map((o) => (
              <option key={o.produtoId} value={o.produtoId}>{o.nome} ({o.tipo}, tem {qtd(o.saldo)}{o.unidade ? ` ${o.unidade}` : ''})</option>
            ))}
          </select>
          <input className={css.busca} inputMode="decimal" aria-label="Quantidade" placeholder="Quantidade" value={rascunho.quantidade} onChange={mudar('quantidade')} />
          <select className={css.busca} aria-label="Motivo" value={rascunho.motivo} onChange={mudar('motivo')}>
            {MOTIVOS_PERDA.map((m) => <option key={m.id} value={m.id}>{m.rotulo}</option>)}
          </select>
          {rascunho.motivo === 'Outro' && (
            <input className={css.busca} aria-label="Qual o motivo" placeholder="Qual o motivo" value={rascunho.texto} onChange={mudar('texto')} />
          )}
          <Botao tipo="submit" variante="primario" disabled={enviando}>{enviando ? 'Lançando…' : 'Lançar perda'}</Botao>
        </div>
      </form>

      <section className={css.aba} aria-label="Resumo do período">
        <div className={css.topo}>
          <label className={css.topo}>
            <span className={css.nome}>De</span>
            <input className={css.busca} type="date" aria-label="De" value={gestao.periodo.de ?? resumo?.de ?? ''}
              onChange={(e) => gestao.setPeriodo((p) => ({ ...p, de: e.target.value || null }))} />
          </label>
          <label className={css.topo}>
            <span className={css.nome}>Até</span>
            <input className={css.busca} type="date" aria-label="Até" value={gestao.periodo.ate ?? resumo?.ate ?? ''}
              onChange={(e) => gestao.setPeriodo((p) => ({ ...p, ate: e.target.value || null }))} />
          </label>
          <Pilula tom={resumo?.valor > 0 ? 'aviso' : 'ok'} fina>{moeda(resumo?.valor ?? 0)} perdidos</Pilula>
        </div>
        {(resumo?.porMotivo ?? []).length > 0 && (
          <p className={css.descricao}>{resumo.porMotivo.map((m) => `${m.rotulo}: ${moeda(m.valor)}`).join(' · ')}</p>
        )}
        {(resumo?.lancamentos ?? []).length === 0 ? (
          <p className={css.descricao}>Nenhuma perda no período.</p>
        ) : (
          <ul className={css.lista}>
            {resumo.lancamentos.map((l) => (
              <li key={l.id} className={css.linha}>
                <div className={css.resumo}>
                  <span className={css.nome}>{l.nome}</span>
                  <span className={css.detalhe}>{quando(l.data)} · {qtd(l.quantidade)} · {l.descricao ?? l.motivo}{l.lote ? ` · ${l.lote}` : ''}</span>
                </div>
                <div className={css.estados}>
                  {l.desfeita ? <Pilula fina>desfeita</Pilula> : <span className={css.detalhe}>{moeda(l.valor)}</span>}
                </div>
                {!l.desfeita && (
                  <div className={css.acoes}>
                    <Botao variante="texto" onClick={() => gestao.desfazer(l)}>Desfazer</Botao>
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}
