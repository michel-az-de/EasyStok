import { useState } from 'react'
import { useChavePixLoja } from '../../../aplicacao/useChavePixLoja'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import css from './AbaIntegracoes.module.css'

// #2233: Pix direto pela chave da loja, sem Mercado Pago. O cliente paga pelo copia e cola e a
// equipe confere no banco antes do "Marcar pago": nada confirma sozinho. A chave só se escreve; o
// que volta é o tipo e o final. Cobrar assim ainda depende da flag `pagamento.pix_direto` do tenant.
const TIPOS = [
  { valor: 'cpf', rotulo: 'CPF' }, { valor: 'cnpj', rotulo: 'CNPJ' }, { valor: 'email', rotulo: 'E-mail' },
  { valor: 'telefone', rotulo: 'Telefone' }, { valor: 'aleatoria', rotulo: 'Chave aleatória' },
]
const VAZIO = { tipo: 'cnpj', chave: '', nomeRecebedor: '', cidade: '' }

function pilula(pix) {
  if (!pix?.configurada) return { tom: 'neutro', texto: 'Sem chave' }
  return pix.pixDiretoLigado ? { tom: 'ok', texto: 'Ligado' } : { tom: 'aviso', texto: 'Chave salva, Pix direto desligado' }
}

export function CartaoPixDireto() {
  const { pix, erro, ocupado, salvar, remover } = useChavePixLoja()
  const [editando, setEditando] = useState(false)
  const [valores, setValores] = useState(VAZIO)
  const preenchido = valores.chave.trim() && valores.nomeRecebedor.trim() && valores.cidade.trim()
  const fechar = () => { setValores(VAZIO); setEditando(false) }
  const campo = (nome) => (e) => setValores((v) => ({ ...v, [nome]: e.target.value }))
  const situacao = pilula(pix)

  return (
    <li className={css.cartao}>
      <div className={css.cabecalho}>
        <strong className={css.nomeProvedor}>Pix na chave da loja</strong>
        <Pilula tom={situacao.tom} fina>{situacao.texto}</Pilula>
      </div>
      <div className={css.credencial}>
        <p className={css.motivo}>
          Sem Mercado Pago: o cliente recebe o copia e cola da chave da loja e a equipe confere no banco antes de marcar pago.
        </p>
        {pix?.configurada && (
          <p className={css.motivo}>Chave {pix.chaveMascarada} · {pix.nomeRecebedor} · {pix.cidade}</p>
        )}
        {erro && <p className={css.motivo} role="alert">{erro}</p>}

        {pix && !editando && (
          <Botao variante="texto" onClick={() => setEditando(true)}>
            {pix.configurada ? 'Trocar a chave Pix' : 'Cadastrar a chave Pix'}
          </Botao>
        )}
        {editando && (
          <div className={css.formularioCredencial}>
            <CampoSelecao rotulo="Tipo da chave" opcoes={TIPOS} value={valores.tipo} onChange={campo('tipo')} />
            <CampoTexto rotulo="Chave Pix" autoComplete="off" value={valores.chave} onChange={campo('chave')} />
            <CampoTexto rotulo="Nome de quem recebe" dica="Até 25 letras, sai sem acento no Pix." maxLength={25}
              value={valores.nomeRecebedor} onChange={campo('nomeRecebedor')} />
            <CampoTexto rotulo="Cidade" dica="Até 15 letras." maxLength={15} value={valores.cidade} onChange={campo('cidade')} />
            <div className={css.botoesCredencial}>
              <Botao variante="primario" disabled={!preenchido || ocupado}
                onClick={async () => { if (await salvar(valores)) fechar() }}>Salvar</Botao>
              <Botao variante="texto" onClick={fechar}>Cancelar</Botao>
            </div>
          </div>
        )}
      </div>
      {pix?.configurada && (
        <div className={css.rodapeCartao}>
          <Botao variante="texto" onClick={remover} disabled={ocupado}>Remover a chave Pix</Botao>
        </div>
      )}
    </li>
  )
}
