'use strict';
// #3114: único lugar do conteúdo web empacotado com endereço de produção. A homologação não tem padrão,
// exige origem HTTPS de teste e fica sem canal de atualização, que é o da produção.
const OFICIAL = Object.freeze({
  apiBaseUrl: 'https://api.easystok.online',
  updateManifestUrl: 'https://easystok.online/downloads/casa-da-baba/latest.json',
  updatePageUrl: 'https://easystok.online/downloads/casa-da-baba/'
});
const PRODUCAO = /(^|\.)(easystok\.online|casadababa\.com)$/;

function origemDeTeste(valor) {
  let url = null;
  try { url = new URL(valor); } catch { /* recusada abaixo */ }
  if (!url || url.protocol !== 'https:' || url.username || url.password || url.port || url.search || url.hash || url.pathname !== '/')
    throw new Error('A homologação precisa de CDB_API_BASE_URL com origem HTTPS de teste, sem caminho, porta ou usuário.');
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

module.exports = { configWeb };
