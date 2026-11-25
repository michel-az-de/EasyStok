// #3175: o motivo do turno chega da API como código curto; a tela mostra em português. Domínio puro.
import assert from 'node:assert/strict'
import { motivoDoTurno } from '../src/dominio/motivoDoTurno.js'

assert.equal(motivoDoTurno(null), null, 'turno que respondeu não tem motivo')
assert.equal(motivoDoTurno(''), null)
assert.equal(motivoDoTurno('portao:limite'), 'limite de respostas na última hora')
assert.equal(motivoDoTurno('portao:cota'), 'cota do mês esgotada')
assert.equal(motivoDoTurno('portao:bloqueado'), 'cliente bloqueado')
assert.equal(motivoDoTurno('guarda:valor'), 'resposta com valor que nenhuma consulta confirmou')
assert.equal(motivoDoTurno('escalada_modelo'), 'o Assistente pediu a equipe')
assert.equal(motivoDoTurno('falha_llm'), 'falha no modelo de IA')

// Os códigos que a API grava hoje (MotivoDoTurno.cs e PortoesDoAssistente.cs) têm todos texto próprio.
const conhecidos = ['portao:chave', 'portao:modelo', 'portao:canal', 'portao:equipe', 'portao:bloqueado', 'portao:pausado', 'portao:limite', 'portao:cota',
  'guarda:valor', 'guarda:outro', 'guarda:sem_resposta', 'guarda:texto_de_ferramenta', 'guarda:promete_encaminhar', 'guarda:situacao_pedido',
  'guarda:pagamento_devolucao', 'guarda:frete_desconto', 'guarda:frete_combinado', 'guarda:link_sem_origem', 'guarda:ferramenta_falhou',
  'iteracoes', 'menu', 'falha_llm', 'teto_saida', 'prazo_turno', 'recusa_modelo', 'escalada_modelo', 'interrompido']
for (const codigo of conhecidos) assert.notEqual(motivoDoTurno(codigo), codigo, codigo + ' sem texto')
assert.equal(new Set(conhecidos.map(motivoDoTurno)).size, conhecidos.length, 'cada código tem o seu texto')

// Código novo que a tela ainda não conhece aparece como veio: melhor o código que esconder o motivo.
assert.equal(motivoDoTurno('portao:novo'), 'portao:novo')
console.log('#3175 domínio: motivo do turno em português, todos os códigos conhecidos, desconhecido aparece como veio.')
