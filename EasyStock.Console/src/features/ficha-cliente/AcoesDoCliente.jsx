import { useEffect, useState } from 'react'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { ROTULO_CONFERIR_IDENTIDADE, telefoneDaConversa } from '../../dominio/cadastroDoLead'
import { CorrecaoVinculoCliente } from './CorrecaoVinculoCliente'
import { copiarTexto, linkDoMapa } from './apresentacaoDaFicha'
import css from './ficha.module.css'

// #3265 (R1): as ações do cartão numa fileira só, logo abaixo da identidade (telefone e endereço seguem como texto).
// Cada uma tem ícone e rótulo curto visível; o nome acessível e a dica dizem a ação inteira. As cópias têm um retorno
// só. O lugar logo abaixo desta fileira fica para a distância (#3232) e a mídia (#3233).
export function AcoesDoCliente({ conversa, conferir }) {
  const { acoes } = useAcessoModulos()
  const [retorno, setRetorno] = useState(null) // 'copiado' | 'falhou'
  useEffect(() => {
    if (!retorno) return undefined
    const id = setTimeout(() => setRetorno(null), 2400)
    return () => clearTimeout(id)
  }, [retorno])
  const telefone = telefoneDaConversa(conversa)
  const endereco = conversa.cliente.endereco
  const podeConferir = conferir && acoes.corrigirVinculoClienteConversa
  if (!telefone && !endereco && !podeConferir) return null
  const copiar = async (texto) => setRetorno(await copiarTexto(texto) ? 'copiado' : 'falhou')
  const nomeTelefone = `Copiar telefone ${telefone}`
  return (
    <div className={css.acoesDoCliente} data-acoes-do-cliente>
      <div className={css.fileiraAcoesCliente} role="group" aria-label="Ações do cliente">
        {telefone && <Botao variante="texto" icone="copy" className={css.acaoCliente} aria-label={nomeTelefone} title={nomeTelefone}
          onClick={() => copiar(telefone)}>Telefone</Botao>}
        {endereco && <>
          <Botao variante="texto" icone="copy" className={css.acaoCliente} aria-label="Copiar endereço" title="Copiar endereço"
            onClick={() => copiar(endereco)}>Endereço</Botao>
          <a className={css.acaoCliente} href={linkDoMapa(endereco)} target="_blank" rel="noopener noreferrer" aria-label="Abrir no mapa" title="Abrir no mapa">
            <Icone nome="external-link" tamanho={20} />Mapa
          </a>
        </>}
        {podeConferir && <CorrecaoVinculoCliente key={conversa.id} conversa={conversa} rotulo={ROTULO_CONFERIR_IDENTIDADE} rotuloCurto="Identidade" className={css.acaoCliente} />}
      </div>
      <output className={css.retornoCopia} aria-live="polite">
        {retorno === 'copiado' ? 'Copiado' : retorno === 'falhou' ? 'Não foi possível copiar' : ''}
      </output>
    </div>
  )
}
