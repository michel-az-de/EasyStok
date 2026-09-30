import { Bloco } from '../../componentes/Bloco'
import { Botao } from '../../componentes/Botao'
import { partesDoEndereco } from '../../dominio/formato'
import { DECISOES_DE_EXCECAO, SITUACOES } from '../../dominio/areaEntrega'
import css from './ficha.module.css'

// UC-02: quando o CEP capturado está fora ou no limite, o botão único de
// confirmar vira as três ações de um toque da dona (liberar, virar encomenda
// agendada, recusar com cortesia), com o bairro à mostra para ela decidir.
export function BlocoEnderecoCapturado({
  endereco, situacaoArea, faixasDeDistancia = null, aoConfirmar, aoDecidirArea,
}) {
  const foraOuLimite = situacaoArea === SITUACOES.FORA || situacaoArea === SITUACOES.LIMITE
  const { bairro } = partesDoEndereco(endereco)
  // Rodada 10, item 5: US-013 pede endereço E distância. Sem geocodificação
  // de verdade, o proxy é a distância em faixas de CEP até a mais próxima
  // atendida (`dominio/areaEntrega.js`). É a mesma conta de sempre (prefixo
  // de CEP como número), então em bairro bem longe o número fica grande
  // demais para significar alguma coisa para ela: acima do limite, vira só
  // "bem longe da área atendida", sem inventar km que o domínio não sabe.
  const LONGE_DEMAIS_PARA_CONTAR = 20
  const rotuloDistancia = faixasDeDistancia == null ? null
    : faixasDeDistancia > LONGE_DEMAIS_PARA_CONTAR ? 'bem longe da área atendida'
      : `a ${faixasDeDistancia} ${faixasDeDistancia === 1 ? 'faixa de CEP' : 'faixas de CEP'} da área`

  return (
    <Bloco titulo="Endereço capturado" destaque>
      <p className={css.enderecoLiteral}>{endereco}</p>
      {foraOuLimite ? (
        <>
          <p className={css.rodapeBloco} style={{ marginTop: 0 }}>
            {situacaoArea === SITUACOES.LIMITE
              ? `No limite da área de entrega${bairro ? `, perto de ${bairro}` : ''}`
              : `Fora da área de entrega${bairro ? `, bairro ${bairro}` : ''}`}
            {rotuloDistancia ? `, ${rotuloDistancia}` : ''}. Decida caso a caso.
          </p>
          <div className={css.acoesArea}>
            <Botao largo variante="primario" onClick={() => aoDecidirArea(DECISOES_DE_EXCECAO.LIBERAR)}>
              Liberar este pedido
            </Botao>
            <Botao largo onClick={() => aoDecidirArea(DECISOES_DE_EXCECAO.ENCOMENDA_AGENDADA)}>
              Virar encomenda agendada
            </Botao>
            <Botao largo onClick={() => aoDecidirArea(DECISOES_DE_EXCECAO.RECUSAR)}>
              Recusar com cortesia
            </Botao>
          </div>
        </>
      ) : (
        <>
          <p className={css.rodapeBloco} style={{ marginTop: 0 }}>
            Confira antes de confirmar. Depois vira o endereço de entrega.
          </p>
          <Botao largo variante="primario" onClick={aoConfirmar}>
            Confirmar e cadastrar
          </Botao>
        </>
      )}
    </Bloco>
  )
}
