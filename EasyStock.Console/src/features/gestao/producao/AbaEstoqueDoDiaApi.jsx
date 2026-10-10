import { useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { useEstoqueDoDiaApi } from '../../../aplicacao/useEstoqueDoDiaApi'
import css from '../cardapio/abaCardapio.module.css'

// M2 › Estoque do dia (M2.1, #1490). O que a Thati usa todo dia, lido do EasyStok: os alertas de
// venda sem saldo (avisa, não trava; RN-48), o saldo em porções por prato e os lotes com o
// vencimento destacado (US-065). "Ajustar" é a contagem dela com o motivo e fecha o alerta.
const porcoes = (n) => (n === 1 ? '1 porção' : `${n} porções`)
const dataCurta = (iso) => (iso ? `${iso.slice(8, 10)}/${iso.slice(5, 7)}` : 'sem validade')

function quandoVence(lote) {
  if (lote.vencido) return 'Venceu'
  if (lote.diasParaVencer === 0) return 'Vence hoje'
  if (lote.diasParaVencer === 1) return 'Vence amanhã'
  return `Vence ${dataCurta(lote.validade)}`
}

function FormularioAjuste({ alvo, aoSalvar, aoCancelar }) {
  const [contagem, setContagem] = useState(String(alvo.saldo ?? ''))
  const [motivo, setMotivo] = useState('')
  const [salvando, setSalvando] = useState(false)

  const salvar = (evento) => {
    evento.preventDefault()
    setSalvando(true)
    Promise.resolve(aoSalvar(contagem, motivo)).then((ok) => { if (ok !== false) aoCancelar() }).finally(() => setSalvando(false))
  }

  return (
    <form className={`${css.campos} ${css.formulario} ${css.ajusteEstoque}`} onSubmit={salvar}>
      <CampoTexto
        rotulo="Porções contadas"
        inputMode="numeric"
        aria-label={`Quantas porções de ${alvo.nome} você contou`}
        value={contagem}
        onChange={(e) => setContagem(e.target.value)}
      />
      <CampoTexto
        rotulo="Motivo do ajuste"
        placeholder="Ex.: contei o congelador"
        maxLength={500}
        value={motivo}
        onChange={(e) => setMotivo(e.target.value)}
      />
      <div className={css.rodapeForm}>
        <Botao variante="texto" onClick={aoCancelar}>Cancelar</Botao>
        <Botao tipo="submit" variante="primario" icone="check" disabled={salvando || !motivo.trim() || contagem === ''}>Salvar contagem</Botao>
      </div>
    </form>
  )
}

export function AbaEstoqueDoDiaApi() {
  const estoque = useEstoqueDoDiaApi()
  const [ajustando, setAjustando] = useState(null)

  if (estoque.estado === 'carregando') return <output className={css.descricao}>Carregando o estoque…</output>
  if (estoque.estado === 'erro' && estoque.pratos.length === 0) {
    return (
      <Vazio icone="alerta" role="alert" titulo="Não consegui ler o estoque"
        acao={<Botao icone="refresh-cw" onClick={estoque.recarregar}>Tentar de novo</Botao>}>
        {estoque.erro}
      </Vazio>
    )
  }

  const saldoDe = (sku) => estoque.pratos.find((p) => p.sku === sku)?.saldo ?? 0
  const lotes = estoque.pratos
    .flatMap((p) => p.lotes.map((l) => ({ ...l, prato: p.nome })))
    .sort((a, b) => (a.validade ?? '9999').localeCompare(b.validade ?? '9999'))

  return (
    <div className={css.aba}>
      {estoque.aviso && (
        <p className={css.aviso} role="alert">
          {estoque.aviso} <Botao variante="texto" onClick={estoque.fecharAviso}>Fechar</Botao>
        </p>
      )}

      <section aria-labelledby="estoque-alertas">
        <h3 id="estoque-alertas" className={css.nome}>Venda sem saldo</h3>
        {estoque.alertas.length === 0 ? (
          <p className={css.descricao}>Nenhum prato vendido sem produção lançada.</p>
        ) : (
          <ul className={css.lista}>
            {estoque.alertas.map((a) => (
              <li key={a.produtoId} className={css.linha}>
                <div className={css.resumo}>
                  <span className={css.nome}>{a.nome}</span>
                  <span className={css.detalhe}>{a.texto}</span>
                </div>
                {ajustando === a.produtoId ? (
                  <FormularioAjuste
                    alvo={{ nome: a.nome, saldo: saldoDe(a.sku) }}
                    aoSalvar={(contagem, motivo) => estoque.ajustar(a.sku, contagem, motivo)}
                    aoCancelar={() => setAjustando(null)}
                  />
                ) : (
                  <div className={css.acoes}>
                    <Botao variante="primario" icone="lapis" disabled={!a.sku} onClick={() => setAjustando(a.produtoId)}>Ajustar</Botao>
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section aria-labelledby="estoque-saldo">
        <h3 id="estoque-saldo" className={css.nome}>Saldo por prato</h3>
        {estoque.pratos.length === 0 ? (
          <p className={css.descricao}>Nenhum prato do cardápio está ligado ao estoque ainda. A primeira produção liga.</p>
        ) : (
          <ul className={css.lista}>
            {estoque.pratos.map((p) => (
              <li key={p.sku} className={css.linha}>
                <div className={css.resumo}>
                  <span className={css.nome}>{p.nome}</span>
                  <span className={css.detalhe}>{p.porcao}</span>
                </div>
                <span className={css.preco}>{porcoes(p.saldo)}</span>
                <div className={css.estados}>
                  {p.saldo === 0 && <Pilula tom="perigo" fina>Sem saldo</Pilula>}
                  {p.descoberto > 0 && <Pilula tom="aviso" fina>{porcoes(p.descoberto)} vendidas sem saldo</Pilula>}
                  {p.lotes.some((l) => l.vencendo) && <Pilula tom="aviso" fina>Lote vencendo</Pilula>}
                </div>
                {ajustando === p.sku ? (
                  <FormularioAjuste
                    alvo={p}
                    aoSalvar={(contagem, motivo) => estoque.ajustar(p.sku, contagem, motivo)}
                    aoCancelar={() => setAjustando(null)}
                  />
                ) : (
                  <div className={css.acoes}>
                    <Botao variante="texto" icone="lapis" onClick={() => setAjustando(p.sku)}>Ajustar</Botao>
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section aria-labelledby="estoque-lotes">
        <h3 id="estoque-lotes" className={css.nome}>Lotes ativos</h3>
        {lotes.length === 0 ? (
          <p className={css.descricao}>Nenhum lote com saldo.</p>
        ) : (
          <ul className={css.lista}>
            {lotes.map((l, i) => (
              <li key={`${l.codigo}-${i}`} className={css.linha}>
                <div className={css.resumo}>
                  <span className={css.nome}>{l.prato}</span>
                  <span className={css.detalhe}>{l.codigo ?? 'sem código'}</span>
                </div>
                <span className={css.preco}>{porcoes(l.quantidade)}</span>
                <div className={css.estados}>
                  <Pilula tom={l.vencido ? 'perigo' : l.vencendo ? 'aviso' : 'neutro'} fina>{quandoVence(l)}</Pilula>
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}
