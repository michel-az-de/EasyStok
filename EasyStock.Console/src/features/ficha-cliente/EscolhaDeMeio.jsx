import { Chip } from '../../componentes/Chip'
import { CampoReais } from '../../componentes/CampoReais'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { usePixDireto } from '../../aplicacao/usePixDireto'
import { meiosDaEscolha } from '../../dominio/cobranca'
import { lerMoeda, moeda } from '../../dominio/formato'
import { avisoDeSinalAntesDeGerar } from '../../dominio/resumoPedido'
import css from './ficha.module.css'

// Os chips de meio (F4, seção 4), num lugar só. Ponta (a) da integração: o
// mesmo grupo aparece no bloco Cobrança e, quando ela aperta "Enviar comanda"
// ou o "Gerar cobrança" da barra sem meio marcado, logo abaixo do botão, com a
// pergunta. Nada sai sem ela escolher.
//
// Decisão de 22/10: um botão só para o link, "Link de pagamento (Pix ou
// cartão)", na primeira linha; as formas de pagar na entrega numa segunda
// linha, menores (`destaque` em `infra/catalogo.js`). `excluir` tira a forma
// atual quando a pergunta é "mudar para qual?".
//
// J2 (#1990): "Dinheiro na entrega" (`pedeTroco` no catálogo) abre o "Troco para" logo abaixo. O
// valor mora no pedido (`definirTroco`) e vai nas observações quando a Ficha gera o pedido (barra
// "Gerar cobrança e enviar" ou bloco Cobrança). O pedido que nasce pela seleção do link (comanda
// compartilhada) não leva o troco: o meio ali é o que o cliente escolheu. Pedido já criado no
// EasyStok não regrava as observações: a tela diz o que ficou gravado ou pede para combinar pela conversa.
export function EscolhaDeMeio({ escolhido, aoEscolher, pergunta = null, excluir = null, pedido = null, geraAoEscolher = false, conversaId = null }) {
  const { meiosDePagamento } = useCatalogo()
  const { fonteApi, agora, selecionada } = useAtendimento()
  const { definirTroco } = useAcoes()
  const pixDireto = usePixDireto()
  // #2921: a janela "Novo pedido" monta numa conversa que não é a selecionada e passa o `conversaId` dela.
  const alvo = conversaId ?? selecionada?.id ?? null
  const pedidoAtual = pedido ?? selecionada?.pedido ?? null
  // #2233: Pix na chave da loja só com a API dizendo que está disponível e o pedido já criado.
  const meios = meiosDaEscolha(meiosDePagamento, { pixDireto, pedidoCriado: Boolean(pedidoAtual?.pedidoId) })
    .filter((meio) => meio.id !== excluir)
  const principais = meios.filter((meio) => meio.destaque)
  const naEntrega = meios.filter((meio) => !meio.destaque)
  const escolhidoNaEntrega = naEntrega.find((meio) => meio.id === escolhido)
  const sinalNaEntrega = escolhidoNaEntrega && escolhidoNaEntrega.id !== 'vale-refeicao'
  const aviso = (sinalNaEntrega || (geraAoEscolher && !escolhido)) && naEntrega.some(meio => meio.id === 'maquininha')
    ? avisoDeSinalAntesDeGerar(pedido, { fonteApi, agora, meio: escolhido }) : null
  const pedeTroco = Boolean(escolhidoNaEntrega?.pedeTroco) && Boolean(pedidoAtual)
  const trocoGravavel = pedeTroco && !pedidoAtual.pedidoId && Boolean(alvo)
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
        {trocoGravavel && (
          <CampoReais
            rotulo="Troco para"
            dica="Quanto o cliente vai dar em dinheiro. Vazio é sem troco. Vai para a Cozinha e para o entregador."
            placeholder="Sem troco"
            centavos={pedidoAtual.trocoPara > 0 ? Math.round(pedidoAtual.trocoPara * 100) : null}
            ler={lerMoeda}
            aoMudar={(centavos) => definirTroco(alvo, centavos ? centavos / 100 : null)}
          />
        )}
        {pedeTroco && !trocoGravavel && (
          <p className={css.corpoBloco}>
            {pedidoAtual.trocoPara > 0
              ? `Troco para ${moeda(pedidoAtual.trocoPara)}, gravado no pedido.`
              : 'O pedido já foi criado: combine o troco com o cliente pela conversa.'}
          </p>
        )}
        {aviso && <p className={css.corpoBloco}>{escolhidoNaEntrega?.rotulo ?? 'Maquininha na entrega'}: {aviso}</p>}
      </div>
    </>
  )
}
