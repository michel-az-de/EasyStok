import { useState } from 'react'
import { Bloco } from '../../componentes/Bloco'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { CampoMascarado } from '../../componentes/CampoMascarado'
import { Pilula } from '../../componentes/Pilula'
import { useAcoes } from '../../aplicacao/contextos'
import {
  dataHora, lerMoeda, mascaraMoeda, moeda, moedaAltaDemais,
} from '../../dominio/formato'
import { ESTADOS_OCORRENCIA } from '../../dominio/ocorrencia'
import css from './ficha.module.css'

const SITUACAO_DA_OCORRENCIA = {
  [ESTADOS_OCORRENCIA.ABERTA]: { rotulo: 'Aberta', tom: 'perigo' },
  [ESTADOS_OCORRENCIA.EM_APURACAO]: { rotulo: 'Em apuração', tom: 'aviso' },
  [ESTADOS_OCORRENCIA.ENCERRADA_COM_ESTORNO]: { rotulo: 'Encerrada com estorno', tom: 'ok' },
  [ESTADOS_OCORRENCIA.ENCERRADA_SEM_ESTORNO]: { rotulo: 'Encerrada sem estorno', tom: 'neutro' },
}

// Bloco da ocorrência (UC-06, RN-34 a RN-38): o que o cliente disse, com o
// histórico completo, e as ações que a dona tem daqui. A abertura é
// automática (US-049), então este bloco só aparece quando ela já existe;
// nenhum botão aqui cria ocorrência, só avança ou fecha a que já está aberta.
//
// "Confirmar reembolso" (rodada 10, item P1.5/"o que falta construir" 4,
// UC-06 passos 5-6, RN-36) é atalho para o MESMO `marcarEstorno` que a barra
// do pedido (MenuDoPedido/BarraProximoPasso) já expõe: antes só existia por
// lá, e a banca (achado P1.5) precisava abrir mais uma camada para achar o
// reembolso a partir da própria ocorrência. `marcarEstorno` já fecha a
// ocorrência com estorno e grava a nota no cadastro (reducer.js,
// MARCAR_ESTORNO); este bloco só chama a mesma ação.
export function BlocoOcorrencia({
  ocorrencia, conversaId, nomeCliente, pedido,
}) {
  const {
    apurarOcorrencia, encerrarOcorrenciaSemEstorno, bloquearCliente, marcarEstorno,
  } = useAcoes()
  const [confirmando, setConfirmando] = useState(null) // 'sem-estorno' | 'bloquear' | 'reembolso'
  const [preferencia, setPreferencia] = useState('')
  const [motivoReembolso, setMotivoReembolso] = useState('')
  const [centavosReembolso, setCentavosReembolso] = useState(0)

  const situacao = SITUACAO_DA_OCORRENCIA[ocorrencia.estado]
  const aberta = ocorrencia.estado === ESTADOS_OCORRENCIA.ABERTA
  const emApuracao = ocorrencia.estado === ESTADOS_OCORRENCIA.EM_APURACAO
  const relatoOriginal = ocorrencia.historico[0]?.texto ?? ''

  // Mesma trava de BarraProximoPasso: sem cobrança paga não há o que
  // devolver (evita confirmar "devolveu R$ 0,00").
  const valorPago = pedido?.cobranca?.valorPago ?? pedido?.cobranca?.valor ?? 0
  const podeReembolsar = Boolean(pedido?.cobranca?.pagaEm)

  const fechar = () => { setConfirmando(null); setPreferencia('') }
  const iniciarReembolso = () => {
    setMotivoReembolso('')
    setCentavosReembolso(Math.round(valorPago * 100))
    setConfirmando('reembolso')
  }

  return (
    <Bloco titulo="Ocorrência">
      <p className={css.corpoBloco}>
        <Pilula tom={situacao.tom}>{situacao.rotulo}</Pilula>
      </p>

      <ul className={css.notas}>
        {ocorrencia.historico.map((entrada, indice) => (
          <li className={css.nota} key={indice}>
            <span className={css.quem}>
              {/* Rodada 10, item 4: entrada 'sistema' (ocorrência de entrega
                  atrasada, aberta sozinha) nunca vira fala da dona. */}
              {entrada.autor === 'cliente' ? nomeCliente : entrada.autor === 'sistema' ? 'Sistema' : 'Thatiane'}
              {' · '}{dataHora(entrada.em)}
            </span>
            {entrada.texto}
          </li>
        ))}
      </ul>

      {ocorrencia.estorno && (
        <p className={css.corpoBloco}>
          Estornado {moeda(ocorrencia.estorno.valor)}. Motivo: {ocorrencia.estorno.motivo}
        </p>
      )}
      {ocorrencia.notaInterna && (
        <p className={css.corpoBloco}>Nota interna: {ocorrencia.notaInterna.texto}</p>
      )}

      {aberta && confirmando === null && (
        <Botao largo variante="primario" onClick={() => apurarOcorrencia(conversaId)}>
          Apurar
        </Botao>
      )}

      {emApuracao && confirmando === null && (
        <div className={css.botoesPrimarios}>
          {podeReembolsar && (
            <Botao largo variante="primario" onClick={iniciarReembolso}>
              Confirmar reembolso
            </Botao>
          )}
          <Botao largo variante="secundario" onClick={() => setConfirmando('sem-estorno')}>
            Encerrar sem estorno
          </Botao>
          <Botao largo variante="texto" onClick={() => setConfirmando('bloquear')}>
            Bloquear por má-fé reincidente
          </Botao>
        </div>
      )}

      {confirmando === 'reembolso' && (
        <div className={css.confirmarCorrecao}>
          <p className={css.corpoBloco}>
            Devolver para {nomeCliente}. Pago: {moeda(valorPago)}.
          </p>
          <CampoMascarado
            tipo="moeda"
            rotulo="Valor a devolver"
            valor={mascaraMoeda(centavosReembolso)}
            erro={moedaAltaDemais(centavosReembolso)
              ? 'Valor alto demais'
              : centavosReembolso > Math.round(valorPago * 100) ? 'Maior que o pago' : null}
            dica="Pode ser menor que o pago, para um estorno parcial."
            aoMudarDigitos={(digitos) => setCentavosReembolso(Number(digitos || '0'))}
            aoColarTexto={(texto) => setCentavosReembolso(lerMoeda(texto))}
          />
          <CampoArea
            rotulo="Motivo do reembolso"
            dica="Obrigatório. Fecha a ocorrência com estorno e fica gravado no cadastro do cliente (UC-06)."
            rows={2}
            value={motivoReembolso}
            onChange={(e) => setMotivoReembolso(e.target.value)}
          />
          <p className={css.confirmarAcoes}>
            <Botao variante="texto" onClick={fechar}>Manter</Botao>
            <Botao
              variante="primario"
              disabled={motivoReembolso.trim().length < 3
                || centavosReembolso <= 0 || moedaAltaDemais(centavosReembolso)
                || centavosReembolso > Math.round(valorPago * 100)}
              onClick={() => {
                marcarEstorno(conversaId, motivoReembolso.trim(), centavosReembolso / 100)
                fechar()
              }}
            >
              Confirmar reembolso
            </Botao>
          </p>
        </div>
      )}

      {confirmando === 'sem-estorno' && (
        <div className={css.confirmarCorrecao}>
          <CampoArea
            rotulo="Preferência do cliente"
            dica="Não era defeito do produto. Vira nota interna, sem mexer no dinheiro."
            rows={2}
            value={preferencia}
            onChange={(e) => setPreferencia(e.target.value)}
          />
          <p className={css.confirmarAcoes}>
            <Botao variante="texto" onClick={fechar}>Manter</Botao>
            <Botao
              variante="primario"
              disabled={preferencia.trim().length < 3}
              onClick={() => { encerrarOcorrenciaSemEstorno(conversaId, preferencia.trim()); fechar() }}
            >
              Encerrar sem estorno
            </Botao>
          </p>
        </div>
      )}

      {confirmando === 'bloquear' && (
        <div className={css.confirmarCorrecao}>
          <p className={css.corpoBloco}>
            Bloquear {nomeCliente} por má-fé reincidente? Vale em todos os canais.
          </p>
          <p className={css.confirmarAcoes}>
            <Botao variante="texto" onClick={fechar}>Manter</Botao>
            <Botao
              variante="primario"
              onClick={() => {
                bloquearCliente(nomeCliente, `Reclamação indevida reincidente. ${relatoOriginal}`.trim())
                fechar()
              }}
            >
              Bloquear
            </Botao>
          </p>
        </div>
      )}
    </Bloco>
  )
}
