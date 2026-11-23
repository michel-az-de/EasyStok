'use strict';
// #3114: único lugar do conteúdo web empacotado com endereço de produção. A homologação não tem padrão,
// exige origem HTTPS de teste e fica sem canal de atualização, que é o da produção.
// Também é a regra que o Gradle e o empacotador de OTA aplicam: `node scripts/ambiente.cjs conferir`.
const fs = require('node:fs');
const OFICIAL = Object.freeze({
  apiBaseUrl: 'https://api.easystok.online',
  updateManifestUrl: 'https://easystok.online/downloads/casa-da-baba/latest.json',
  updatePageUrl: 'https://easystok.online/downloads/casa-da-baba/'
});
// Mesma lista de caracteres do Ambiente.java: recusa o ponto final do nome absoluto, que resolve para a produção.
const HOST = /^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$/;
const PRODUCAO = /(^|\.)(easystok\.online|casadababa\.com)$/;

function origemDeTeste(valor, nome = 'CDB_API_BASE_URL') {
  let url = null;
  try { url = new URL(valor); } catch { /* recusada abaixo */ }
  if (!url || url.protocol !== 'https:' || !HOST.test(url.hostname) || url.username || url.password || url.port || url.search || url.hash || url.pathname !== '/')
    throw new Error(`A homologação precisa de ${nome} com origem HTTPS de teste, sem caminho, porta ou usuário.`);
  if (PRODUCAO.test(url.hostname)) throw new Error('A homologação não pode falar com a produção: ' + url.origin);
  return url.origin;
}

function configWeb({ ambiente = 'oficial', apiBaseUrl } = {}) {
  if (ambiente === 'homologacao') return { ambiente, apiBaseUrl: origemDeTeste(apiBaseUrl), updateManifestUrl: '', updatePageUrl: '' };
  if (ambiente !== 'oficial') throw new Error('Ambiente do APK desconhecido: ' + ambiente);
  const api = (apiBaseUrl || OFICIAL.apiBaseUrl).replace(/\/+$/, '');
  if (new URL(api).protocol !== 'https:') throw new Error('A API do piloto precisa de HTTPS.');
  return { ambiente, apiBaseUrl: api, updateManifestUrl: OFICIAL.updateManifestUrl, updatePageUrl: OFICIAL.updatePageUrl };
}

function lerConfig(arquivo) {
  if (!fs.existsSync(arquivo)) throw new Error('Conteúdo web sem config.js: rode scripts/copy-web.cjs (e cap sync) antes.');
  return JSON.parse(fs.readFileSync(arquivo, 'utf8').replace(/^window\.CDB_CONFIG = /, '').replace(/;\s*$/, ''));
}

// Build do APK: o -P de homologação é origem de teste canônica e o conteúdo web empacotado é desta variante.
function conferirBuild({ ambiente, configJs, api, app }) {
  if (ambiente === 'homologacao') {
    for (const [nome, valor] of [['-PcdbHomologacaoApi', api], ['-PcdbHomologacaoApp', app]])
      if (origemDeTeste(valor, nome) !== valor) throw new Error(`${nome} precisa ser a origem exata, sem barra final e em minúsculas: ${valor}`);
  } else if (ambiente !== 'oficial') throw new Error('Ambiente do APK desconhecido: ' + ambiente);
  const config = lerConfig(configJs);
  if (config.ambiente !== ambiente || (ambiente === 'homologacao' && config.apiBaseUrl !== api))
    throw new Error(`Conteúdo web empacotado para ${config.ambiente} (${config.apiBaseUrl}). Rode copy-web com CDB_AMBIENTE=${ambiente}` + (ambiente === 'homologacao' ? ` e CDB_API_BASE_URL=${api}.` : '.'));
}

// OTA vai para os aparelhos da operação: só conteúdo oficial, na API da produção.
function exigirConteudoOficial(configJs) {
  const config = lerConfig(configJs);
  if (config.ambiente !== 'oficial' || config.apiBaseUrl !== OFICIAL.apiBaseUrl)
    throw new Error(`OTA só leva conteúdo oficial na API ${OFICIAL.apiBaseUrl}; este é ${config.ambiente} (${config.apiBaseUrl}). Rode copy-web com CDB_AMBIENTE=oficial.`);
}

if (require.main === module) {
  const [comando, ambiente, configJs, api, app] = process.argv.slice(2);
  try {
    if (comando !== 'conferir') throw new Error('Uso: node scripts/ambiente.cjs conferir <oficial|homologacao> <config.js> [api] [app]');
    conferirBuild({ ambiente, configJs, api, app });
  } catch (error) {
    console.error(error.message);
    process.exit(1);
  }
}

module.exports = { configWeb, conferirBuild, exigirConteudoOficial };
