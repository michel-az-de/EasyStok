// Telas de ajuste dos módulos (#1447, homologação de 07/10). Antes era a casca
// do modal "Gestão" (rodada 13), com as seis abas juntas; o Felipe achou confuso
// ("isso não tem nada a ver com gestão"). Agora cada aba abre como tela do módulo
// dono (`dominio/modulos.js`), dentro da moldura do módulo, e este arquivo só
// decide qual aba montar e se ela vem com o aviso de "ainda não ligado".
// O conteúdo de cada aba continua no próprio arquivo, sem mudança.
import { useMemo } from 'react'
import { AbaProducao } from './producao/AbaProducao'
import { AbaCaixa } from './caixa/AbaCaixa'
import { AbaCaixaApi } from './caixa/AbaCaixaApi'
import { AbaJanelas } from './janelas/AbaJanelas'
import { AbaFidelidade } from './fidelidade/AbaFidelidade'
import { AbaIntegracoes } from './integracoes/AbaIntegracoes'
import { AbaAtendimento } from './atendimento/AbaAtendimento'
import { AbaRespostas } from './respostas/AbaRespostas'
import { TelaCanais } from './canais/TelaCanais'
import { AbaCardapioApi } from './cardapio/AbaCardapioApi'
import { AbaCategoriasApi } from './cardapio/AbaCategoriasApi'
import { useAtendimento } from '../../aplicacao/contextos'
import css from './gestao.module.css'

// Modo API (F06, decisão do Felipe em 30/09): as abas sem backend não somem. Abrem com a
// faixa e os controles desabilitados até a fatia de cada uma entrar. Nada é gravado: as
// ações delas também avisam (`aplicacao/api/naoLigadas.js`), e Integrações nunca guarda chave.
const AINDA_NAO_LIGADO = {
  producao: 'Produção e cardápio ainda não funcionam por esta tela. Nada aqui é gravado.',
  fidelidade: 'Fidelidade e cupons ainda não funcionam por esta tela. Nada aqui é gravado.',
  integracoes: 'As integrações ainda não funcionam por esta tela. Nenhuma chave é guardada no navegador.',
}

function AbaNaoLigada({ texto, children }) {
  return (
    <>
      <p className={css.naoLigado} role="note">{texto}</p>
      <fieldset disabled className={css.desligado}>{children}</fieldset>
    </>
  )
}

// Uma tela por aba: o objeto só decide qual elemento entra na árvore.
// `janelasApi` (#1440): no modo API o App entrega aqui o cadastro de janelas, zonas e bloqueios
// da S45 (o mesmo de Entregas › Janelas e frete), no lugar da aba de demonstração.
export function PainelDeAjuste({ aba, janelasApi = null }) {
  const { fonteApi } = useAtendimento()
  const painelDaAba = useMemo(() => ({
    atendimento: <AbaAtendimento />,
    // #1441: respostas prontas e automáticas, fora do caminho do atendimento.
    respostas: <AbaRespostas />,
    producao: <AbaProducao />,
    // #1443: no modo API o caixa é o do EasyStok; a demonstração segue com a massa local.
    caixa: fonteApi ? <AbaCaixaApi /> : <AbaCaixa />,
    janelas: fonteApi && janelasApi ? janelasApi : <AbaJanelas />,
    fidelidade: <AbaFidelidade />,
    integracoes: <AbaIntegracoes />,
    canais: <TelaCanais />,
    // M1.1 (#1481): gestão do cardápio (só modo API).
    cardapio: <AbaCardapioApi />,
    // M1.3 (#1483): categorias do cardápio (só modo API).
    categorias: <AbaCategoriasApi />,
  }), [fonteApi, janelasApi])
  const painel = painelDaAba[aba] ?? null
  if (fonteApi && AINDA_NAO_LIGADO[aba]) return <AbaNaoLigada texto={AINDA_NAO_LIGADO[aba]}>{painel}</AbaNaoLigada>
  return painel
}
