// #2849: a ficha do cliente abre em leitura e mostra os mesmos números e rótulos em Cadastros,
// na Busca e no Atendimento. Aqui ficam as regras puras: o quadro de números e os dados de contato.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

// O domínio importa sem extensão (Vite resolve); no Node a prova completa o ".js".
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) return proximo(especificador + '.js', contexto)
    return proximo(especificador, contexto)
  },
})
const { quadroDoCliente } = await import('../src/dominio/fichaCliente.js')
const { dadosDeContato } = await import('../src/dominio/cadastros.js')

const semEspacoDuro = (texto) => texto.replace(/ /g, ' ')

// Sem o dossiê lido não há número: nada de "0" ou "R$ 0,00" inventado.
assert.deepEqual(quadroDoCliente(null), [])

// Os rótulos são os da Ficha do Atendimento: compras válidas, total comprado e Ticket médio.
const quadro = quadroDoCliente({ pedidos: 4, totalGasto: 200 })
assert.deepEqual(quadro.map(n => n.rotulo), ['compras válidas', 'total comprado', 'Ticket médio'])
assert.equal(quadro[0].valor, '4')
assert.equal(semEspacoDuro(quadro[1].valor), 'R$ 200,00')
assert.equal(semEspacoDuro(quadro[2].valor), 'R$ 50,00')

// Uma compra fica no singular; sem compra não há soma nem média.
assert.equal(quadroDoCliente({ pedidos: 1, totalGasto: 30 })[0].rotulo, 'compra válida')
assert.deepEqual(quadroDoCliente({ pedidos: 0, totalGasto: 0 }), [{ rotulo: 'compras válidas', valor: '0' }])

// Contato: telefone, e-mail e documento aparecem sempre (vazio vira null, a tela diz "Não informado").
const vazio = dadosDeContato({ nome: 'Ana' })
assert.deepEqual(vazio.map(d => d.rotulo), ['Telefone', 'E-mail', 'CPF ou documento'])
assert.ok(vazio.every(d => d.valor === null))

// O que é opcional só entra preenchido; endereço e complemento formam uma linha só.
const cheio = dadosDeContato({ telefone: '+5511980004805', email: ' ana@casa.com ', tipoPessoa: 'juridica', documento: '12345678000190',
  nomeFantasia: 'Casa', endereco: 'Rua A, 24', apt: 'Apto 14', observacoes: 'Portão azul' })
assert.deepEqual(cheio.map(d => d.rotulo), ['Telefone', 'E-mail', 'CNPJ', 'Nome fantasia', 'Endereço', 'Observações'])
assert.equal(cheio[1].valor, 'ana@casa.com')
assert.equal(cheio[4].valor, 'Rua A, 24, Apto 14')
assert.match(cheio[0].valor, /98000-4805/)

console.log('Ficha única do cliente: quadro de números e dados de contato conferidos.')
