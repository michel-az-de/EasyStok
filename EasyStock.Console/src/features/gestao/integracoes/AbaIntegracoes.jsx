import { useState } from 'react'
import { useAcoes, useCatalogo } from '../../../aplicacao/contextos'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { Chip } from '../../../componentes/Chip'
import { Pilula } from '../../../componentes/Pilula'
import {
  AVISO_SIMULADO, PROVEDORES_LOGISTICA, motivoParaAtivar, temCredencial,
} from '../../../dominio/integracoes'
import css from './AbaIntegracoes.module.css'

// Frente Entregas e integrações (rodada 13, issue #46, registro 108): ligar
// Lalamove e 99 Entregas (simulado), guardar a credencial sem nunca mostrar
// de volta, e escolher o provedor padrão do despacho. "Entregador próprio"
// entra no mesmo cadastro (é o que já existia, R12/issue #17) para poder
// desligar e escolher padrão do mesmo jeito.
function CartaoProvedor({ provedor, config, padrao, acoes }) {
  const [credencial, setCredencial] = useState('')
  const [mostrarCampo, setMostrarCampo] = useState(false)
  const ativo = config?.ativo ?? false
  const salva = temCredencial({ provedores: { [provedor.chave]: config } }, provedor.chave)
  const motivo = motivoParaAtivar({ provedores: { [provedor.chave]: config } }, provedor.chave)
  const ehPadrao = padrao === provedor.chave

  const salvarCredencial = () => {
    if (!credencial.trim()) return
    acoes.salvarCredencialProvedor(provedor.chave, credencial)
    // Nunca fica em memória do componente depois de salva: some do campo e
    // não é mostrada de novo em lugar nenhum (nem aqui, nem em log).
    setCredencial('')
    setMostrarCampo(false)
  }

  return (
    <li className={css.cartao}>
      <div className={css.cabecalho}>
        <strong className={css.nomeProvedor}>{provedor.rotulo}</strong>
        <Chip
          papel="filtro" ativo={ativo} className={css.chipLigar}
          onClick={() => acoes.alternarProvedorLogistica(provedor.chave)}
        >
          {ativo ? 'Ligado' : 'Desligado'}
        </Chip>
      </div>
      {ehPadrao && <Pilula tom="ok" fina>Padrão do despacho</Pilula>}

      {provedor.exigeCredencial && (
        <div className={css.credencial}>
          <Pilula tom={salva ? 'ok' : 'neutro'} fina>
            {salva ? 'Credencial salva' : 'Sem credencial'}
          </Pilula>
          {!mostrarCampo && (
            <Botao variante="texto" icone="lapis" onClick={() => setMostrarCampo(true)}>
              {salva ? 'Trocar credencial' : 'Cadastrar credencial'}
            </Botao>
          )}
          {mostrarCampo && (
            <div className={css.formularioCredencial}>
              <CampoTexto
                rotulo={`Credencial da ${provedor.rotulo} (simulada)`}
                tipo="password"
                autoComplete="off"
                placeholder="Cole a credencial simulada"
                value={credencial}
                onChange={(e) => setCredencial(e.target.value)}
              />
              <div className={css.botoesCredencial}>
                <Botao variante="primario" icone="check" onClick={salvarCredencial} disabled={!credencial.trim()}>
                  Salvar
                </Botao>
                <Botao variante="texto" onClick={() => { setCredencial(''); setMostrarCampo(false) }}>
                  Cancelar
                </Botao>
              </div>
            </div>
          )}
        </div>
      )}

      <div className={css.rodapeCartao}>
        {motivo && !ativo && <p className={css.motivo}>{motivo}</p>}
        {/* Só ligado vira padrão: `alternarProvedor` já recusa ligar sem
            credencial (dominio/integracoes.js), então ativo implica elegível. */}
        {ativo && (
          <Chip papel="escolha" ativo={ehPadrao} onClick={() => acoes.definirProvedorPadrao(provedor.chave)}>
            Usar como padrão do despacho
          </Chip>
        )}
      </div>
    </li>
  )
}

export function AbaIntegracoes() {
  const { integracoesLogistica } = useCatalogo()
  const acoes = useAcoes()

  if (!integracoesLogistica) {
    return (
      <div className={css.aba}>
        <p role="status">Carregando integrações…</p>
      </div>
    )
  }

  return (
    <div className={css.aba}>
      <p className={css.intro}>
        Provedores de entrega ligados ao despacho (cozinha e Entregas). {AVISO_SIMULADO}
      </p>
      <ul className={css.lista} role="radiogroup" aria-label="Provedor padrão do despacho">
        {PROVEDORES_LOGISTICA.map((provedor) => (
          <CartaoProvedor
            key={provedor.chave}
            provedor={provedor}
            config={integracoesLogistica.provedores[provedor.chave]}
            padrao={integracoesLogistica.padrao}
            acoes={acoes}
          />
        ))}
      </ul>
      <p className={css.rodape}>
        Só provedores ligados podem ser usados como padrão do despacho.
      </p>
    </div>
  )
}
