import { Botao } from './Botao'
import { Chip } from './Chip'
import css from './EscolhaDeEntregador.module.css'

// Issue #46 (registro 108): só aparece quando a Thati ligou algum provedor
// além de "Entregador próprio" na aba Gestão · Entregas e integrações (senão
// o botão único de sempre continua, ver `features/entregas/PainelViagem.jsx`
// — sem redesenhar quem nunca ligou integração nenhuma).
//
// Mesmo CSS de `EscolhaDeEntregador.jsx` (a escolha de quem leva, R12): é a
// mesma forma de "chips + rodapé com confirmar/cancelar", sem inventar
// estilo novo. Componente não importa `dominio` nem `infra`
// (`ferramentas/verificar-camadas.mjs`): preço e prazo já chegam formatados,
// e quem decide o que cada clique faz é a feature, por `aoEscolherProvedor`/
// `aoConfirmar`.
export function EscolhaDeProvedorEntrega({
  provedores = [], provedorEscolhido, cotacaoTexto, cotando = false, aviso, aoEscolherProvedor, aoConfirmar, aoFechar,
}) {
  const precisaCotar = provedorEscolhido && provedorEscolhido !== 'proprio'
  return (
    <div className={css.escolha}>
      <p className={css.pergunta}>Quem vai buscar este pedido?</p>
      <div className={css.opcoes} role="radiogroup" aria-label="Provedor de entrega">
        {provedores.map((p) => (
          <Chip
            key={p.chave} papel="escolha" ativo={provedorEscolhido === p.chave}
            onClick={() => aoEscolherProvedor(p.chave)}
          >
            {p.rotulo}
          </Chip>
        ))}
      </div>
      {precisaCotar && (
        <p className={css.aviso} aria-live="polite">
          {cotando ? 'Cotando…' : cotacaoTexto}
        </p>
      )}
      <div className={css.rodape}>
        <Botao
          variante="primario"
          disabled={!provedorEscolhido || (precisaCotar && (cotando || !cotacaoTexto))}
          onClick={aoConfirmar}
        >
          {provedorEscolhido === 'proprio' ? 'Buscar entregador' : 'Chamar'}
        </Botao>
        <Botao variante="texto" onClick={aoFechar}>Cancelar</Botao>
      </div>
      {aviso && <p className={css.aviso}>{aviso}</p>}
    </div>
  )
}
