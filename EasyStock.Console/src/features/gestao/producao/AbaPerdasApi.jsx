import { useState } from 'react'
import { Modal } from '../../../componentes/Modal'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { usePerdasApi } from '../../../aplicacao/usePerdasApi'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { MOTIVOS_PERDA } from '../../../aplicacao/perdas'
import { moeda } from '../../../dominio/formato'
import css from '../cardapio/abaCardapio.module.css'

// M2 › Perdas (M2.6, #1511). Lançar perda de prato ou insumo com o motivo da lista (D-M2-02; texto
// só em "Outro"). Sai por FEFO e grava o custo do lote; acima de R$ 50 é do Gerente (D-M2-06).
// O resumo do período não conta ajuste de contagem nem a baixa de insumo da produção. Lote vencido
// aparece como sugestão e só sai quando ela confirma. Desfazer é o estorno (Gerente).
const qtd = (n) => Number(n ?? 0).toLocaleString('pt-BR', { maximumFractionDigits: 3 })
const quando = (iso) => (iso ? new Date(iso).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : '')
const vazio = { produtoId: '', porcao: '', quantidade: '', motivo: 'PerdaNoPreparo', texto: '' }

export function AbaPerdasApi() {
  const gestao = usePerdasApi()
  const [rascunho, setRascunho] = useState(vazio)
  const [enviando, setEnviando] = useState(false)
  const [lancamentoAberto, setLancamentoAberto] = useState(false)
  // #1584: desfazer é o estorno de saída, que a API só aceita do Gerente; quem não tem a ação
  // não vê o botão. Baixar o lote e desfazer pedem confirmação em linha, como "Excluir janela".
  const podeDesfazer = useAcessoModulos().acoes.editarProducao === true
  const [confirmando, setConfirmando] = useState(null) // null | 'vencido:<lote>' | 'desfazer:<perda>'

  async function executar(acao) {
    if (enviando) return false
    setEnviando(true)
    try { return await acao() } finally { setEnviando(false) }
  }
  const confirmar = (acao) => executar(acao).then((ok) => { if (ok) setConfirmando(null) })

  if (gestao.estado === 'carregando') return <output className={css.descricao}>Carregando as perdas…</output>
  if (gestao.estado === 'erro' && !gestao.resumo) {
    return (
      <Vazio icone="alerta" role="alert" titulo="Não consegui ler as perdas"
        acao={<Botao icone="refresh-cw" onClick={gestao.recarregar}>Tentar de novo</Botao>}>
        {gestao.erro}
      </Vazio>
    )
  }

  const mudar = (campo) => (e) => setRascunho((atual) => ({ ...atual, [campo]: e.target.value }))
  const escolhido = gestao.opcoes.find((o) => o.produtoId === rascunho.produtoId)
  const porcoes = escolhido?.porcoes ?? []

  async function lancar(evento) {
    evento.preventDefault()
    const ok = await executar(() => gestao.lancar({ ...rascunho, nome: escolhido?.nome, porcoes }))
    if (ok) { setRascunho(vazio); setLancamentoAberto(false) }
  }

  const resumo = gestao.resumo
  return (
    <div className={css.aba}>
      {gestao.estado === 'erro' && <p className={css.aviso} role="alert">
        As perdas não foram atualizadas. Os dados abaixo são da última consulta concluída. {gestao.erro}
        {' '}<Botao variante="texto" icone="refresh-cw" disabled={enviando} onClick={gestao.recarregar}>Recarregar perdas</Botao>
      </p>}
      <p className={css.descricao}>
        O que se perdeu e por quê. A baixa segue a validade dos lotes e considera o custo de cada lote. Acima de {moeda(50)} quem
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
                  <span className={css.detalhe}>{v.lote ?? 'sem lote'} · {qtd(v.quantidade)} · venceu há {v.diasVencido} {v.diasVencido === 1 ? 'dia' : 'dias'} · {moeda(v.valor)}</span>
                </div>
                <div className={css.acoes}>
                  {confirmando === `vencido:${v.itemEstoqueId}` ? (
                    <>
                      <span className={css.descricao}>Lançar o lote inteiro como perda por vencimento?</span>
                      <Botao variante="secundario" disabled={enviando} onClick={() => confirmar(() => gestao.lancarVencido(v))}>Registrar perda</Botao>
                      <Botao variante="texto" disabled={enviando} onClick={() => setConfirmando(null)}>Manter lote</Botao>
                    </>
                  ) : (
                    <Botao variante="texto" disabled={enviando} aria-label={`Registrar perda de todo o lote ${v.lote ?? ''} de ${v.nome}`} onClick={() => setConfirmando(`vencido:${v.itemEstoqueId}`)}>Registrar perda do lote</Botao>
                  )}
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      <Botao variante="primario" icone="mais" aria-haspopup="dialog" onClick={() => { gestao.fecharAviso(); setLancamentoAberto(true) }}>Lançar perda</Botao>
      {lancamentoAberto && <Modal titulo="Lançar perda" aoFechar={enviando ? undefined : () => setLancamentoAberto(false)} largura="min(720px, calc(100vw - 24px))"><form className={css.aba} onSubmit={lancar} aria-label="Lançar perda">
        {gestao.aviso && <p className={css.aviso} role="alert">{gestao.aviso}</p>}
        <div className={`${css.campos} ${css.formulario} ${css.lancamentoPerda}`}>
          <CampoSelecao disabled={enviando} rotulo="Prato ou insumo" value={rascunho.produtoId}
            onChange={(e) => setRascunho((atual) => ({ ...atual, produtoId: e.target.value, porcao: '' }))}
            opcoes={[{ valor: '', rotulo: 'Escolha o prato ou insumo' }, ...gestao.opcoes.map((o) => ({
              valor: o.produtoId, rotulo: `${o.nome} (${o.tipo}, tem ${qtd(o.saldo)}${o.unidade ? ` ${o.unidade}` : ''})`,
            }))]} />
          {porcoes.length > 0 && (
            <div className={css.linhaCompleta}>
              <CampoSelecao disabled={enviando} rotulo="Porção" value={rascunho.porcao} onChange={mudar('porcao')}
                opcoes={[{ valor: '', rotulo: 'Escolha a porção perdida' }, ...porcoes.map((s) => ({ valor: s.chave, rotulo: `${s.rotulo} (tem ${qtd(s.saldo)})` }))]} />
            </div>
          )}
          <CampoTexto disabled={enviando} inputMode="decimal" rotulo={escolhido?.unidade ? `Quantidade (${escolhido.unidade})` : 'Quantidade'} value={rascunho.quantidade} onChange={mudar('quantidade')} />
          <CampoSelecao disabled={enviando} rotulo="Motivo" value={rascunho.motivo} onChange={mudar('motivo')}
            opcoes={MOTIVOS_PERDA.map((m) => ({ valor: m.id, rotulo: m.rotulo }))} />
          {rascunho.motivo === 'Outro' && (
            <div className={css.linhaCompleta}><CampoTexto disabled={enviando} rotulo="Qual o motivo" dica="Descreva o que aconteceu com pelo menos 3 caracteres." value={rascunho.texto} onChange={mudar('texto')} /></div>
          )}
          <div className={css.rodapeForm}>
            <Botao tipo="submit" variante="primario" icone="plus" disabled={enviando}>{enviando ? 'Lançando…' : 'Lançar perda'}</Botao>
          </div>
        </div>
      </form></Modal>}

      <section className={css.aba} aria-label="Resumo do período">
        <div className={css.campos}>
          <CampoTexto disabled={enviando} tipo="date" rotulo="Data inicial" value={gestao.periodo.de ?? resumo?.de ?? ''}
            onChange={(e) => gestao.setPeriodo((p) => ({ ...p, de: e.target.value || null }))} />
          <CampoTexto disabled={enviando} tipo="date" rotulo="Data final" value={gestao.periodo.ate ?? resumo?.ate ?? ''}
            onChange={(e) => gestao.setPeriodo((p) => ({ ...p, ate: e.target.value || null }))} />
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
                {!l.desfeita && podeDesfazer && (
                  <div className={css.acoes}>
                    {confirmando === `desfazer:${l.id}` ? (
                      <>
                        <span className={css.descricao}>Desfazer esta perda? A quantidade volta para o estoque.</span>
                        <Botao variante="secundario" disabled={enviando} onClick={() => confirmar(() => gestao.desfazer(l))}>Desfazer perda</Botao>
                        <Botao variante="texto" disabled={enviando} onClick={() => setConfirmando(null)}>Manter perda</Botao>
                      </>
                    ) : (
                      <Botao variante="texto" icone="undo-2" disabled={enviando} aria-label={`Desfazer perda de ${l.nome}`} onClick={() => setConfirmando(`desfazer:${l.id}`)}>Desfazer</Botao>
                    )}
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
