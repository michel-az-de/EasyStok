import { useMemo, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Modal } from '../../../componentes/Modal'
import { METODOS_CAIXA, nomeDoMetodoCaixa } from '../../../dominio/caixa'
import { situacaoDeEstoque } from '../../../dominio/cardapio'
import { moeda } from '../../../dominio/formato'
import css from './abaCaixa.module.css'

const OPCOES_METODO = METODOS_CAIXA.map((id) => ({ valor: id, rotulo: nomeDoMetodoCaixa(id) }))

// UC-11 · Lançar venda avulsa no caixa. Passo 2 do caso de uso: "Ela seleciona
// os itens vendidos e o cliente, se houver cadastro" — por isso o valor nunca
// é digitado à mão aqui, sai sempre da soma do cardápio (mesma conta de
// `totalDoPedido`), para não divergir do preço cadastrado.
export function ModalVendaAvulsa({ cardapio, aoFechar, aoLancar }) {
  const [itens, setItens] = useState({})
  const [nome, setNome] = useState('')
  const [meio, setMeio] = useState('dinheiro')

  const linhas = useMemo(
    () => Object.entries(itens).filter(([, qtd]) => qtd > 0).map(([sku, qtd]) => ({ sku, qtd })),
    [itens],
  )
  const total = linhas.reduce((soma, { sku, qtd }) => {
    const item = cardapio.find((i) => i.sku === sku)
    return soma + (item?.preco ?? 0) * qtd
  }, 0)

  const mudarQtd = (sku, delta) => setItens((atual) => {
    const proxima = Math.max((atual[sku] ?? 0) + delta, 0)
    return { ...atual, [sku]: proxima }
  })

  const podeLancar = linhas.length > 0

  return (
    <Modal
      titulo="Venda avulsa"
      descricao="Balcão ou combinada fora do WhatsApp. Entra na mesma fila da cozinha e baixa o mesmo estoque."
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao variante="texto" onClick={aoFechar}>Cancelar</Botao>
          <Botao
            variante="primario"
            icone="shopping-cart"
            disabled={!podeLancar}
            onClick={() => aoLancar({ nome, itens: linhas, meio })}
          >
            Lançar venda · {moeda(total)}
          </Botao>
        </>
      )}
    >
      <div className={css.formGrade}>
        <CampoTexto
          rotulo="Nome do cliente (opcional)"
          placeholder="Ex.: Cliente do balcão"
          value={nome}
          onChange={(e) => setNome(e.target.value)}
        />
        <CampoSelecao
          rotulo="Forma de pagamento"
          opcoes={OPCOES_METODO}
          value={meio}
          onChange={(e) => setMeio(e.target.value)}
        />
      </div>

      <ul className={css.listaItens}>
        {cardapio.map((item) => {
          const qtd = itens[item.sku] ?? 0
          const situacao = situacaoDeEstoque(item)
          return (
            <li key={item.sku} className={css.itemVenda}>
              <span className={css.infoItem}>
                <strong>{item.nome}</strong>
                <span className={css.dicaItem}>{item.porcao} · {moeda(item.preco)} · {situacao.rotulo}</span>
              </span>
              <span className={css.qtdControle}>
                <Botao variante="secundario" icone="menos" aria-label={`Tirar um ${item.nome}`} onClick={() => mudarQtd(item.sku, -1)} disabled={qtd === 0} />
                <span className={css.qtdValor}>{qtd}</span>
                <Botao variante="secundario" icone="mais" aria-label={`Somar um ${item.nome}`} onClick={() => mudarQtd(item.sku, 1)} />
              </span>
            </li>
          )
        })}
      </ul>
    </Modal>
  )
}
