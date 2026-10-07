// Telas de ajuste dos módulos (#1447, homologação de 07/10). Antes era a casca
// do modal "Gestão" (rodada 13), com as seis abas juntas; o Felipe achou confuso
// ("isso não tem nada a ver com gestão"). Agora cada aba abre como tela do módulo
// dono (`dominio/modulos.js`), dentro da moldura do módulo, e este arquivo só
// decide qual aba montar e se ela vem com o aviso de "ainda não ligado".
// O conteúdo de cada aba continua no próprio arquivo, sem mudança.
import { useMemo } from 'react'
import { AbaProducao } from './producao/AbaProducao'
import { AbaCaixa } from './caixa/AbaCaixa'
import { AbaJanelas } from './janelas/AbaJanelas'
import { AbaFidelidade } from './fidelidade/AbaFidelidade'
import { AbaIntegracoes } from './integracoes/AbaIntegracoes'
import { AbaAtendimento } from './atendimento/AbaAtendimento'
import { TelaCanais } from './canais/TelaCanais'
import { useAtendimento } from '../../aplicacao/contextos'
import css from './gestao.module.css'

// Modo API (F06, decisão do Felipe em 30/09): as abas sem backend não somem. Abrem com a
// faixa e os controles desabilitados até a fatia de cada uma entrar. Nada é gravado: as
// ações delas também avisam (`aplicacao/api/naoLigadas.js`), e Integrações nunca guarda chave.
const AINDA_NAO_LIGADO = {
  producao: 'Produção e cardápio ainda não estão ligados ao EasyStok nesta versão (F11). Nada aqui é gravado.',
  caixa: 'O caixa ainda não está ligado ao EasyStok nesta versão (F14). Nada aqui é gravado.',
  janelas: 'Esta aba ainda não está ligada. As janelas de verdade estão em Entregas › Janelas e frete.',
  fidelidade: 'Fidelidade e cupons ainda não estão ligados ao EasyStok nesta versão (F15). Nada aqui é gravado.',
  integracoes: 'As integrações ainda não estão ligadas nesta versão (F16). Nenhuma chave é guardada no navegador.',
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
export function PainelDeAjuste({ aba }) {
  const { fonteApi } = useAtendimento()
  const painelDaAba = useMemo(() => ({
    atendimento: <AbaAtendimento />,
    producao: <AbaProducao />,
    caixa: <AbaCaixa />,
    janelas: <AbaJanelas />,
    fidelidade: <AbaFidelidade />,
    integracoes: <AbaIntegracoes />,
    canais: <TelaCanais />,
  }), [])
  const painel = painelDaAba[aba] ?? null
  if (fonteApi && AINDA_NAO_LIGADO[aba]) return <AbaNaoLigada texto={AINDA_NAO_LIGADO[aba]}>{painel}</AbaNaoLigada>
  return painel
}
