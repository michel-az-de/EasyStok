import { useState } from 'react'
import { Bloco } from '../../componentes/Bloco'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import {
  motivoDoBloqueio, ocupacaoDeHoje, proximaLivre, rotuloDaOcupacao,
} from '../../dominio/entrega'
import css from './ficha.module.css'

// Barra de ocupação. Acompanha o texto, nunca substitui: quem não enxerga a
// barra lê a mesma coisa na linha de cima.
function BarraOcupacao({ ocupacao }) {
  return (
    <span className={css.trilho} aria-hidden="true">
      <span
        className={`${css.preenchimento} ${ocupacao.estourou ? css.preenchimentoEstourado : ''}`}
        style={{ width: Math.min(ocupacao.percentual, 100) + '%' }}
      />
    </span>
  )
}

// Lista sempre visível no lugar do <select>. A janela cheia FICA na lista,
// desabilitada, com a contagem e a próxima livre ao lado: sumir com ela é o erro
// que a Baymard mede em mercado online, e era o que a dica antiga prometia.
export function SeletorJanela({ pedido, editavel, aoEscolher, aoForcarEncaixe }) {
  const { janelas, cardapio } = useCatalogo()
  const { conversas, agora } = useAtendimento()
  const [encaixe, setEncaixe] = useState(null)

  const ocupacoes = ocupacaoDeHoje(janelas, conversas, agora, pedido.janela, cardapio)

  return (
    <Bloco titulo="Janela de entrega">
      <ul className={css.janelas}>
        {ocupacoes.map((ocupacao) => {
          const motivo = motivoDoBloqueio(ocupacao)
          const livre = motivo ? proximaLivre(ocupacoes, ocupacao.id) : null
          return (
            <li key={ocupacao.id} className={`${css.janela} ${css[ocupacao.chave] ?? ''}`}>
              <button
                type="button"
                className={css.escolhaJanela}
                aria-pressed={ocupacao.confirmada}
                disabled={!editavel || Boolean(motivo)}
                onClick={() => aoEscolher(ocupacao.id)}
              >
                <span className={css.faixaJanela}>
                  {ocupacao.faixa}
                  {ocupacao.confirmada && <Icone nome="check" rotulo="Janela escolhida" />}
                </span>
                <span className={css.vagas}>{rotuloDaOcupacao(ocupacao)}</span>
                <BarraOcupacao ocupacao={ocupacao} />
              </button>

              {motivo && (
                <p className={css.motivoJanela}>
                  <span>{motivo}</span>
                  {livre && <b>Próxima livre: {livre.faixa}.</b>}
                  {editavel && (
                    <Botao onClick={() => setEncaixe({ ocupacao, motivo })}>
                      Encaixar mesmo assim
                    </Botao>
                  )}
                </p>
              )}
            </li>
          )
        })}
      </ul>

      {/* Encaixe fora de vaga é decisão dela, com confirmação e registro. No
          atendimento automático isso não existe: o agente passa para ela. */}
      {encaixe && (
        <Modal
          titulo="Encaixar fora de vaga?"
          descricao={encaixe.motivo}
          aoFechar={() => setEncaixe(null)}
          rodape={(
            <>
              <Botao onClick={() => setEncaixe(null)}>Não encaixar</Botao>
              <Botao
                variante="primario"
                onClick={() => {
                  aoForcarEncaixe(encaixe.ocupacao.id, encaixe.ocupacao.faixa, encaixe.motivo)
                  setEncaixe(null)
                }}
              >
                <Icone nome="check" /> Encaixar e registrar
              </Botao>
            </>
          )}
        >
          <p className={css.corpoBloco}>
            A janela de {encaixe.ocupacao.faixa} fica com {encaixe.ocupacao.ocupadas + 1} pedidos
            para {encaixe.ocupacao.capacidade} de capacidade.
            {encaixe.ocupacao.vagas === 0
              ? ' Fica registrado que a capacidade estourou.'
              : ' Cabe na capacidade, mas entra depois do corte. Fica registrado.'}
          </p>
          <p className={css.corpoBloco}>
            Você continua podendo mandar para outra faixa, se preferir conversar antes.
          </p>
        </Modal>
      )}
    </Bloco>
  )
}
