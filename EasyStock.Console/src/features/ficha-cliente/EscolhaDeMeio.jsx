import { Chip } from '../../componentes/Chip'
import { useCatalogo } from '../../aplicacao/contextos'
import css from './ficha.module.css'

// Os quatro chips de meio (F4, seção 4), num lugar só. Ponta (a) da
// integração: o mesmo grupo aparece no bloco Cobrança e, quando ela aperta
// "Enviar comanda" ou o "Gerar cobrança" da barra sem meio marcado, logo
// abaixo do botão, com a pergunta. Nada sai por Pix sem ela escolher.
//
// Rodada 12 (issue #13, sugestão da Thatiane): Pix e cartão em destaque, na
// primeira linha; as formas de pagar na entrega numa segunda linha, menores
// (`destaque` em `infra/catalogo.js`). `excluir` tira a forma atual quando a
// pergunta é "mudar para qual?".
export function EscolhaDeMeio({ escolhido, aoEscolher, pergunta = null, excluir = null }) {
  const { meiosDePagamento } = useCatalogo()
  const meios = meiosDePagamento.filter((meio) => meio.id !== excluir)
  const principais = meios.filter((meio) => meio.destaque)
  const naEntrega = meios.filter((meio) => !meio.destaque)
  const chip = (meio, className) => (
    <Chip
      key={meio.id}
      papel="escolha"
      ativo={escolhido === meio.id}
      className={className}
      onClick={() => aoEscolher(meio.id)}
    >
      {meio.rotulo}
    </Chip>
  )
  return (
    <>
      {pergunta && <p className={css.perguntaMeio}>{pergunta}</p>}
      <div className={css.escolhaMeio} role="radiogroup" aria-label="Meio de pagamento">
        {principais.length > 0 && (
          <div className={css.chipsMeio}>{principais.map((meio) => chip(meio, css.chipDestaque))}</div>
        )}
        {naEntrega.length > 0 && (
          <div className={`${css.chipsMeio} ${css.chipsNaEntrega}`}>
            <span className={css.rotuloNaEntrega}>Na entrega</span>
            {naEntrega.map((meio) => chip(meio, css.chipSecundario))}
          </div>
        )}
      </div>
    </>
  )
}
