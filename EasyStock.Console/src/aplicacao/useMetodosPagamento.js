import { FONTE_API } from '../infra/fonteDados'
import { contextoCadastro, useListaCadastros } from './cadastros'

const demonstracao = [
  { codigo: 'pix', nome: 'Pix' }, { codigo: 'dinheiro', nome: 'Dinheiro' },
  { codigo: 'credito', nome: 'Cartão de crédito' }, { codigo: 'debito', nome: 'Cartão de débito' },
  { codigo: 'transferencia', nome: 'Transferência' }, { codigo: 'outro', nome: 'Outro' },
]

export function useMetodosPagamento() {
  const lista = useListaCadastros('pagamentos', { busca: '', pagina: 1, filtro: '' }, contextoCadastro(), FONTE_API)
  const itens = FONTE_API ? lista.itens.filter(m => m.ativo) : demonstracao
  return { ...lista, carregando: FONTE_API && lista.carregando, erro: FONTE_API ? lista.erro : null,
    opcoes: itens.map(m => ({ valor: m.codigo, rotulo: m.nome })) }
}
