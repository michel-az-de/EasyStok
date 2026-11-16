import { useState } from 'react'
import { useAcoes, useCatalogo } from '../../../aplicacao/contextos'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { EsqueletoDeLista } from '../../../componentes/Esqueleto'
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
    <li className={`${css.cartao} ${ativo ? '' : css.desligado}`}>
      <div className={css.cabecalho}>
        <strong className={css.nomeProvedor}>{provedor.rotulo}</strong>
        <Chip
          papel="filtro" ativo={ativo} className={css.chipLigar}
          disabled={!ativo && Boolean(motivo)}
          aria-label={`${provedor.rotulo}: ${ativo ? 'ligado' : 'desligado'}`}
          onClick={() => acoes.alternarProvedorLogistica(provedor.chave)}
        >
          {ativo ? 'Ligado' : 'Desligado'}
        </Chip>
      </div>

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
            <form className={css.formularioCredencial} onSubmit={(e) => { e.preventDefault(); salvarCredencial() }}>
              <CampoTexto
                rotulo={`Credencial da ${provedor.rotulo} (simulada)`}
                tipo="password"
                autoComplete="off"
                placeholder="Cole a credencial simulada"
                value={credencial}
                onChange={(e) => setCredencial(e.target.value)}
              />
              <div className={css.botoesCredencial}>
                <Botao tipo="submit" variante="primario" icone="check" disabled={!credencial.trim()}>
                  Salvar credencial simulada
                </Botao>
                <Botao variante="texto" onClick={() => { setCredencial(''); setMostrarCampo(false) }}>
                  Cancelar
                </Botao>
              </div>
            </form>
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
        <EsqueletoDeLista linhas={3} altura="calc(var(--toque) * 3)">Carregando integrações…</EsqueletoDeLista>
      </div>
    )
  }

  return (
    <div className={css.aba}>
      <p className={css.intro}>
        Quem leva os pedidos que saem da Cozinha e de Entregas. {AVISO_SIMULADO}
      </p>
      <div role="radiogroup" aria-label="Provedor padrão do despacho">
      <ul className={css.lista}>
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
      </div>
    </div>
  )
}
