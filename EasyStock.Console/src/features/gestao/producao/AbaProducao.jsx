// Frente Produção e cardápio (rodada 13, issue #43, áudios 13/14, UC-08,
// UC-09, RN-44 a RN-51, D7). Na tela é uma coisa só (o item do cardápio com
// sua produção e saldo); por trás são Produto (estoque) e CardapioItem
// (vitrine) do mapa EasyStok, ligados pelo mesmo sku, mais o LoteProducao
// novo desta rodada (dominio/producao.js).
import { useMemo, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoArea, CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { Vazio } from '../../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../../aplicacao/contextos'
import { itemPorSku, itensAtivos } from '../../../dominio/cardapio'
import {
  DESTINOS_PORCAO, rotuloDestino, rotuloDoSaldo, situacaoDeVencimento, textoDescoberto,
} from '../../../dominio/producao'
import { dataHora } from '../../../dominio/formato'
import css from './producao.module.css'

// "800 g" / "1,5 kg" -> grama inteira. Sem casamento, cai em 500 g (porção
// mais comum da massa de exemplo) em vez de travar o formulário.
function gramasDaPorcao(texto) {
  const m = /([\d.,]+)\s*(kg|g)/i.exec(texto ?? '')
  if (!m) return 500
  const valor = parseFloat(m[1].replace(',', '.'))
  return Math.round(m[2].toLowerCase() === 'kg' ? valor * 1000 : valor)
}

const formatoPeso = (g) => `${Math.round(g).toLocaleString('pt-BR')} g`

function numero(valor, minimo = -Infinity) {
  const n = Number(valor)
  return Number.isFinite(n) && n >= minimo ? n : null
}

const novaLinhaPorcao = (item) => ({
  destino: 'comer-agora', quantidade: '', pesoPorcaoG: String(gramasDaPorcao(item?.porcao)), rotulo: '',
})

// UC-08: peso real produzido, dividido em porções de venda, vira lote com
// identificador e validade (RN-44, RN-45, RN-46). Alt A: insumo intermediário
// (molho, massa laminada) não pede porcionamento, só peso e validade.
function FormularioProducao({ item, aoFechar, aoRegistrar }) {
  const [pesoRealG, setPesoRealG] = useState('')
  const [identificador, setIdentificador] = useState('')
  const [validadeDias, setValidadeDias] = useState('3')
  const [insumo, setInsumo] = useState(false)
  const [porcoes, setPorcoes] = useState(() => [novaLinhaPorcao(item)])
  const [erro, setErro] = useState(null)

  const pesoRealNum = numero(pesoRealG, 0.01) ?? 0
  const linhas = porcoes.map((p) => ({
    ...p, quantidadeNum: numero(p.quantidade, 1), pesoPorcaoNum: numero(p.pesoPorcaoG, 1),
  }))
  const pesoPorcionadoG = linhas.reduce((soma, p) => soma + (p.quantidadeNum ?? 0) * (p.pesoPorcaoNum ?? 0), 0)
  const sobraG = pesoRealNum - pesoPorcionadoG

  const mudarLinha = (indice, campo, valor) => setPorcoes(
    (atual) => atual.map((p, i) => (i === indice ? { ...p, [campo]: valor } : p)),
  )
  const sugerirQuantidade = (indice) => {
    const peso = numero(porcoes[indice].pesoPorcaoG, 1)
    if (!peso || !pesoRealNum) return
    mudarLinha(indice, 'quantidade', String(Math.floor(pesoRealNum / peso)))
  }
  const incluirLinha = () => setPorcoes((atual) => [...atual, novaLinhaPorcao(item)])
  const removerLinha = (indice) => setPorcoes(
    (atual) => (atual.length > 1 ? atual.filter((_, i) => i !== indice) : atual),
  )

  function confirmar() {
    if (!pesoRealNum || pesoRealNum <= 0) { setErro('Informe o peso real produzido.'); return }
    if (!insumo) {
      if (linhas.length === 0 || linhas.some((p) => !p.quantidadeNum || !p.pesoPorcaoNum)) {
        setErro('Cada porção precisa de quantidade e peso maiores que zero.')
        return
      }
      if (sobraG < 0) { setErro('As porções somam mais peso do que foi produzido.'); return }
    }
    aoRegistrar({
      sku: item.sku,
      identificador: identificador.trim(),
      pesoRealG: pesoRealNum,
      insumo,
      validadeDias: numero(validadeDias, 0),
      porcoes: insumo
        ? [{
          destino: 'comer-agora', quantidade: 1, pesoPorcaoG: pesoRealNum, rotulo: 'Insumo',
        }]
        : linhas.map((p) => ({
          destino: p.destino, quantidade: p.quantidadeNum, pesoPorcaoG: p.pesoPorcaoNum, rotulo: p.rotulo,
        })),
    })
  }

  return (
    <div className={css.cartao}>
      <h4 className={css.tituloCartao}>Registrar produção · {item.nome}</h4>
      <p className={css.legenda}>
        O peso real vira um lote com número e validade, já dividido em porções de venda.
      </p>

      <div className={css.grade}>
        <CampoTexto
          rotulo="Peso real produzido (g)" tipo="number" min="0" value={pesoRealG}
          onChange={(e) => setPesoRealG(e.target.value)} placeholder="Ex.: 1072"
        />
        <CampoTexto
          rotulo="Identificador do lote" value={identificador}
          onChange={(e) => setIdentificador(e.target.value)} placeholder={`Lote ${item.sku}`}
        />
        <CampoTexto
          rotulo="Validade (dias)" tipo="number" min="0" value={validadeDias}
          onChange={(e) => setValidadeDias(e.target.value)}
          dica="Perto do fim da validade o lote fica destacado."
        />
      </div>

      <label className={css.opcao}>
        <input type="checkbox" checked={insumo} onChange={(e) => setInsumo(e.target.checked)} />
        É insumo intermediário (molho, massa laminada): não abastece o cardápio sozinho
      </label>

      {!insumo && (
        <div className={css.porcoes}>
          <p className={css.legenda}>
            Porcionado {formatoPeso(pesoPorcionadoG)}
            {pesoRealNum > 0 && (sobraG >= 0
              ? ` · sobra ${formatoPeso(sobraG)}`
              : ` · ${formatoPeso(-sobraG)} além do peso real produzido`)}
          </p>
          {porcoes.map((p, indice) => (
            // eslint-disable-next-line react/no-array-index-key -- linha de porção nasce sem id próprio
            <div key={indice} className={css.linhaPorcao}>
              <CampoSelecao
                rotulo="Destino" opcoes={DESTINOS_PORCAO} value={p.destino}
                onChange={(e) => mudarLinha(indice, 'destino', e.target.value)}
              />
              <CampoTexto
                rotulo="Peso (g)" tipo="number" min="1" value={p.pesoPorcaoG}
                onChange={(e) => mudarLinha(indice, 'pesoPorcaoG', e.target.value)}
              />
              <CampoTexto
                rotulo="Qtd." tipo="number" min="1" value={p.quantidade}
                onChange={(e) => mudarLinha(indice, 'quantidade', e.target.value)}
              />
              <CampoTexto
                rotulo="Rótulo" value={p.rotulo} placeholder={`${p.pesoPorcaoG || '?'} g`}
                onChange={(e) => mudarLinha(indice, 'rotulo', e.target.value)}
              />
              <Botao variante="texto" onClick={() => sugerirQuantidade(indice)}>Sugerir qtd.</Botao>
              {porcoes.length > 1 && (
                <Botao
                  variante="texto" icone="x" aria-label="Remover esta linha de porção"
                  onClick={() => removerLinha(indice)}
                >
                  Remover
                </Botao>
              )}
            </div>
          ))}
          <Botao variante="texto" icone="plus" onClick={incluirLinha}>Adicionar linha de porção</Botao>
        </div>
      )}

      {erro && <p className={css.erro} role="alert">{erro}</p>}

      <div className={css.acoesForm}>
        <Botao onClick={aoFechar}>Cancelar</Botao>
        <Botao variante="primario" onClick={confirmar}>Registrar lote</Botao>
      </div>
    </div>
  )
}

// UC-09 passos 6-7: contagem física vence o sistema. Motivo obrigatório
// (mínimo 3 letras, mesmo padrão do estorno de estoque do EasyStok) e fecha
// o alerta persistente do sku.
function FormularioAjuste({
  item, saldoAtual, aoFechar, aoAjustar,
}) {
  const [novoSaldo, setNovoSaldo] = useState(String(saldoAtual))
  const [motivo, setMotivo] = useState('')
  const [erro, setErro] = useState(null)

  function confirmar() {
    const n = numero(novoSaldo, 0)
    if (n === null) { setErro('Informe a contagem física em porções.'); return }
    if (motivo.trim().length < 3) { setErro('Descreva o motivo (mínimo 3 letras).'); return }
    aoAjustar({ sku: item.sku, novoSaldoTotal: n, motivo: motivo.trim() })
  }

  return (
    <div className={css.cartao}>
      <h4 className={css.tituloCartao}>Ajustar contagem · {item.nome}</h4>
      <p className={css.legenda}>
        Conte o que tem de verdade e lance o ajuste. O saldo é corrigido e o
        alerta fecha, com data e motivo registrados.
      </p>
      <div className={css.grade}>
        <CampoTexto
          rotulo="Porções contadas agora" tipo="number" min="0" value={novoSaldo}
          onChange={(e) => setNovoSaldo(e.target.value)}
        />
      </div>
      <CampoArea
        rotulo="Motivo" value={motivo} onChange={(e) => setMotivo(e.target.value)}
        placeholder="Ex.: contagem no congelador, sobrou da produção de ontem"
      />
      {erro && <p className={css.erro} role="alert">{erro}</p>}
      <div className={css.acoesForm}>
        <Botao onClick={aoFechar}>Cancelar</Botao>
        <Botao variante="primario" onClick={confirmar}>Salvar ajuste</Botao>
      </div>
    </div>
  )
}

export function AbaProducao() {
  const { cardapio } = useCatalogo()
  const { producao, agora } = useAtendimento()
  const { registrarProducao, ajustarContagemProducao } = useAcoes()
  const [registrarPara, setRegistrarPara] = useState(null)
  const [ajustarPara, setAjustarPara] = useState(null)

  const itens = useMemo(() => itensAtivos(cardapio), [cardapio])

  const descobertosLista = useMemo(() => Object.entries(producao.descobertos)
    .map(([sku, dado]) => ({ sku, ...dado, item: itemPorSku(cardapio, sku) }))
    .filter((d) => d.item), [producao.descobertos, cardapio])

  const lotesPorSku = useMemo(() => {
    const mapa = new Map()
    producao.lotes.forEach((lote) => mapa.set(lote.sku, [...(mapa.get(lote.sku) ?? []), lote]))
    return mapa
  }, [producao.lotes])

  const lotesOrdenados = useMemo(
    () => [...producao.lotes].sort((a, b) => new Date(a.produzidoEm) - new Date(b.produzidoEm)),
    [producao.lotes],
  )

  function registrar(dados) {
    registrarProducao(dados, agora)
    setRegistrarPara(null)
  }
  function ajustar(dados) {
    ajustarContagemProducao(dados, agora)
    setAjustarPara(null)
  }

  const itemRegistrar = registrarPara ? itemPorSku(cardapio, registrarPara) : null
  const itemAjustar = ajustarPara ? itemPorSku(cardapio, ajustarPara) : null

  return (
    <div className={css.aba}>
      <p className={css.intro}>
        Você produz em peso e vende em porções, para comer agora ou congelado.
        Registre a produção e o saldo do cardápio se atualiza sozinho.
      </p>

      {descobertosLista.length > 0 && (
        <section className={css.secao}>
          <h4 className={css.tituloSecao}>Alertas de produção</h4>
          <ul className={css.listaAlertas}>
            {descobertosLista.map(({
              sku, item, quantidade, ultimaVendaEm,
            }) => (
              <li key={sku} className={css.linhaAlerta}>
                <Pilula tom="perigo" icone="alerta">
                  {textoDescoberto(item.nome, item.porcao, quantidade)}
                </Pilula>
                {ultimaVendaEm && (
                  <span className={css.legendaFina}>
                    última venda descoberta: {dataHora(Date.parse(ultimaVendaEm))}
                  </span>
                )}
                <Botao variante="texto" icone="lapis" onClick={() => setAjustarPara(sku)}>
                  Registrar contagem
                </Botao>
              </li>
            ))}
          </ul>
        </section>
      )}

      {itemRegistrar && (
        <FormularioProducao item={itemRegistrar} aoFechar={() => setRegistrarPara(null)} aoRegistrar={registrar} />
      )}
      {itemAjustar && (
        <FormularioAjuste
          item={itemAjustar}
          saldoAtual={itemAjustar.estoque}
          aoFechar={() => setAjustarPara(null)}
          aoAjustar={ajustar}
        />
      )}

      <section className={css.secao}>
        <h4 className={css.tituloSecao}>Saldo por item, a partir dos lotes</h4>
        <table className={css.tabela} aria-label="Saldo de produção por item do cardápio">
          <thead>
            <tr className={css.linhaCabecalho}>
              <th>Item</th>
              <th>Porção</th>
              <th>Saldo</th>
              <th>Origem</th>
              <th>Ações</th>
            </tr>
          </thead>
          <tbody>
            {itens.map((item) => {
              const temLote = (lotesPorSku.get(item.sku) ?? []).length > 0
              return (
                <tr key={item.sku} className={css.linha}>
                  <td>{item.nome}</td>
                  <td>{item.porcao}</td>
                  <td>{rotuloDoSaldo(item.estoque)}</td>
                  <td>
                    {temLote
                      ? <Pilula tom="ragu">lote lançado</Pilula>
                      : <Pilula tom="neutro" fina>sem produção lançada</Pilula>}
                  </td>
                  <td className={css.acoesLinha}>
                    <Botao variante="texto" icone="panela" onClick={() => setRegistrarPara(item.sku)}>
                      Registrar produção
                    </Botao>
                    <Botao variante="texto" icone="lapis" onClick={() => setAjustarPara(item.sku)}>
                      Ajustar contagem
                    </Botao>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </section>

      <section className={css.secao}>
        <h4 className={css.tituloSecao}>Lotes ativos</h4>
        {lotesOrdenados.length === 0 ? (
          <Vazio titulo="Nenhum lote lançado ainda">
            Registre a primeira produção pela tabela acima. Todo lote nasce com
            identificador, data e validade.
          </Vazio>
        ) : (
          <ul className={css.listaLotes}>
            {lotesOrdenados.map((lote) => {
              const item = itemPorSku(cardapio, lote.sku)
              const situacao = situacaoDeVencimento(lote, agora)
              return (
                <li key={lote.id} className={css.linhaLote}>
                  <div className={css.cabecalhoLote}>
                    <strong>{lote.identificador}</strong>
                    <span className={css.legendaFina}>{lote.insumo ? 'Insumo · ' : ''}{item?.nome ?? lote.sku}</span>
                    {situacao !== 'ok' && (
                      <Pilula tom={situacao === 'vencido' ? 'perigo' : 'aviso'} icone="hourglass">
                        {situacao === 'vencido' ? 'Vencido' : 'Perto do vencimento'}
                      </Pilula>
                    )}
                  </div>
                  <p className={css.legendaFina}>
                    Produzido em {dataHora(Date.parse(lote.produzidoEm))} · peso real {formatoPeso(lote.pesoRealG)}
                    {lote.sobraG > 0 && ` · sobra ${formatoPeso(lote.sobraG)}`}
                    {lote.validadeEm && ` · validade ${dataHora(Date.parse(lote.validadeEm))}`}
                  </p>
                  {!lote.insumo && (
                    <ul className={css.porcoesLote}>
                      {lote.porcoes.map((p) => (
                        <li key={p.id}>
                          {rotuloDestino(p.destino)}: {p.saldo} de {p.quantidade} ({p.rotulo})
                        </li>
                      ))}
                    </ul>
                  )}
                </li>
              )
            })}
          </ul>
        )}
      </section>
    </div>
  )
}
