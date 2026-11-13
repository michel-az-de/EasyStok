import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoTexto } from '../../componentes/Campo'
import { Chip } from '../../componentes/Chip'
import { Icone } from '../../componentes/Icone'
import { Selo } from '../../componentes/Selo'
import { useRelogio } from '../../hooks/useRelogio'
import {
  VEICULOS_CORRIDA, cotacaoVencida, distanciaDaCotacao, relogioDaCotacao, validadeDaCotacao,
} from '../../dominio/entregasApi'
import { moedaDosCentavos } from '../../dominio/formato'
import css from './corridas.module.css'

const ICONE_DO_VEICULO = { Moto: 'moto', Carro: 'carro' }

// Cotar e chamar (#2162, redesenho #2168): a cotação mostra o preço grande, a distância e quanto tempo ela
// ainda vale. Vencida, o preço fica riscado e o botão vira "Cotar de novo". A chamada usa a cotação relida no
// servidor: o preço nunca sai daqui.
export function ChamadaLalamove({ travado, cotar, chamar, destino }) {
  const [veiculo, setVeiculo] = useState('Moto')
  const [cotacao, setCotacao] = useState(null)
  const [observacao, setObservacao] = useState('')
  const cotarAgora = () => cotar(veiculo).then((c) => { if (c && c !== true) setCotacao(c) })
  const chamarAgora = () => chamar({ cotacaoId: cotacao.cotacaoId, veiculo, observacao: observacao.trim() || null })
    .then((ok) => { if (ok) setCotacao(null) })

  return (
    <section className={css.painel} aria-label="Chamar pela Lalamove">
      <div className={css.cabeca}>
        <span className={css.marcador} aria-hidden="true"><Icone nome={ICONE_DO_VEICULO[veiculo]} tamanho={20} /></span>
        <div className={css.titulo}>
          <strong>Chamar pela Lalamove</strong>
          {destino && (
            <span className={css.trajeto}><Icone nome="map-pin" tamanho={16} /><span>Da cozinha até {destino}</span></span>
          )}
        </div>
      </div>
      {cotacao
        // Cotação nova (inclusive a de "Cotar de novo") monta de novo: o foco volta para "Chamar".
        ? <Cotacao key={`${cotacao.cotacaoId}:${cotacao.expiraEm}`} cotacao={cotacao} veiculo={veiculo} observacao={observacao} travado={travado}
            aoMudarObservacao={setObservacao} aoChamar={chamarAgora} aoCotarDeNovo={cotarAgora} aoDesistir={() => setCotacao(null)} />
        : (
          <div className={css.linha}>
            <div className={css.veiculos} role="radiogroup" aria-label="Veículo">
              {VEICULOS_CORRIDA.map((v) => (
                <Chip key={v.valor} papel="escolha" ativo={veiculo === v.valor} icone={ICONE_DO_VEICULO[v.valor]} disabled={travado}
                  onClick={() => setVeiculo(v.valor)}>{v.rotulo}</Chip>
              ))}
            </div>
            <Botao variante="primario" disabled={travado} onClick={cotarAgora}>{travado ? 'Cotando…' : 'Ver preço'}</Botao>
          </div>
        )}
    </section>
  )
}

function Cotacao({ cotacao, veiculo, observacao, travado, aoMudarObservacao, aoChamar, aoCotarDeNovo, aoDesistir }) {
  const agora = useRelogio(0, 1000, { real: true })
  const chamarRef = useRef(null)
  // A cotação acabou de aparecer no lugar do "Ver preço": o foco vai para o próximo passo.
  useEffect(() => { chamarRef.current?.focus() }, [])
  const segundos = validadeDaCotacao(cotacao, agora)
  const vencida = cotacaoVencida(cotacao, agora)
  const preco = moedaDosCentavos(cotacao.valorCentavos)
  const detalhes = [distanciaDaCotacao(cotacao), veiculo === 'Carro' ? 'de carro' : 'de moto'].filter(Boolean).join(' · ')

  return (
    <section className={css.painelInterno} aria-label="Cotação da Lalamove">
      <div className={css.cotacao}>
        <div>
          <p className={`${css.preco} ${vencida ? css.precoVencido : ''}`}>{preco}</p>
          <p className={css.detalhes}>{detalhes}</p>
        </div>
        {vencida
          ? <Selo tom="alerta" icone="relogio">Cotação vencida</Selo>
          : (
            <span className={`${css.validade} ${segundos < 60 ? css.validadeCurta : ''}`} aria-live="off">
              <Icone nome="relogio" tamanho={16} />Vale por {relogioDaCotacao(segundos)}
            </span>
          )}
      </div>
      {!vencida && (
        <CampoTexto rotulo="Recado para o motorista (opcional)" maxLength={120} value={observacao} disabled={travado}
          placeholder="Ex.: portão azul, interfone 42" onChange={(e) => aoMudarObservacao(e.target.value)} />
      )}
      <div className={css.linha}>
        {vencida
          ? <Botao variante="primario" icone="refresh-cw" disabled={travado} onClick={aoCotarDeNovo}>Cotar de novo</Botao>
          : <Botao ref={chamarRef} variante="primario" icone={ICONE_DO_VEICULO[veiculo]} disabled={travado} onClick={aoChamar}>
              {travado ? 'Chamando…' : `Chamar por ${preco}`}
            </Botao>}
        <Botao variante="texto" disabled={travado} onClick={aoDesistir}>Cancelar</Botao>
      </div>
      {vencida && <p className={css.apoio}>A Lalamove segura o preço por 5 minutos. Cote de novo para ver o preço de agora.</p>}
    </section>
  )
}
