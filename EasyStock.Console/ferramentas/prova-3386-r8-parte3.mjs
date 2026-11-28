// #3398: item D da prova-3386-r8 num processo próprio (C fica na parte2), para cada parte caber no teto de 180 s do
// rodar-provas. Usa o Playwright da r8 (PLAYWRIGHT_MODULE); a lista de itens de cada parte fica em prova-3386-r8.mjs.
process.argv.splice(2, process.argv.length, 'D')
await import('./prova-3386-r8.mjs')
