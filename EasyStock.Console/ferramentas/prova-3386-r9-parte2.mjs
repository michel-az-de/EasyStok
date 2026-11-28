// #3392, #3398: item D da prova-3386-r9 num processo próprio (E a H ficam na parte3), para cada parte caber no teto de
// 180 s do rodar-provas. Usa o Playwright da r9 (PLAYWRIGHT_MODULE); a lista de itens de cada parte fica em prova-3386-r9.mjs.
process.argv.splice(2, process.argv.length, 'D')
await import('./prova-3386-r9.mjs')
