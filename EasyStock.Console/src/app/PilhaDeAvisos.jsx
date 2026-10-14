import { Toast } from '../componentes/Toast'
import { Botao } from '../componentes/Botao'
import { useAcoes, useAtendimento } from '../aplicacao/contextos'
import { TIPOS_DE_AVISO } from '../dominio/aviso'
import { hashDoModulo } from '../dominio/modulos'
import css from './pilhaDeAvisos.module.css'

// #1807: os avisos do Balcão (contato novo, passou para você, mensagem nova, pagamento e etapa
// do pedido) aparecem em qualquer tela do console, não só no Balcão. Clicar leva à conversa.
const DESENHO = {
  [TIPOS_DE_AVISO.CONTATO_NOVO]: { icone: 'user-plus', tom: 'info' },
  [TIPOS_DE_AVISO.PASSOU_PARA_VOCE]: { icone: 'hand', tom: 'alerta' },
  [TIPOS_DE_AVISO.MENSAGEM_NOVA]: { icone: 'conversa', tom: 'marca' },
  [TIPOS_DE_AVISO.PAGAMENTO_CONFIRMADO]: { icone: 'dollar-sign', tom: 'sucesso' },
  [TIPOS_DE_AVISO.ETAPA_DO_PEDIDO]: { icone: 'package', tom: 'info' },
}

export function PilhaDeAvisos() {
  const { avisosNaTela = [], dispensarAviso, permissaoNotificacao } = useAtendimento()
  const { selecionar, pedirNotificacaoDoNavegador } = useAcoes()
  const itens = avisosNaTela.map((aviso) => ({ ...aviso, ...DESENHO[aviso.tipo], abrivel: Boolean(aviso.conversaId) }))
  const abrir = (aviso) => {
    selecionar(aviso.conversaId)
    const balcao = hashDoModulo('atendimento', 'balcao')
    if (window.location.hash !== balcao) window.location.hash = balcao
    dispensarAviso(aviso.id)
  }
  // Permissão do navegador pedida no primeiro aviso, sempre por um clique dela (navegador
  // nenhum concede sem gesto). Depois de respondida, o botão some.
  const rodape = permissaoNotificacao === 'default' && pedirNotificacaoDoNavegador
    ? <Botao variante="secundario" className={css.permissao} onClick={pedirNotificacaoDoNavegador}>Avisar também com o console minimizado</Botao>
    : null
  return <Toast itens={itens} aoFechar={dispensarAviso} aoAbrir={abrir} rotulo="Avisos do atendimento" rodape={rodape} />
}
