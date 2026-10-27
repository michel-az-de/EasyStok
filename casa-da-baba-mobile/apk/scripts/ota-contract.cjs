'use strict';
// Contrato único entre o APK, o conteúdo empacotado e a publicação OTA.
// A versão nativa sai do package.json, que também gera o versionCode do APK.
const { versionCode } = require('../package.json');

const NATIVE_VERSION_CODE = versionCode;
const DATA_SCHEMA = 2; // Igual a UpdatePolicy.DATA_SCHEMA; um teste confere os dois.
if (!Number.isSafeInteger(NATIVE_VERSION_CODE) || NATIVE_VERSION_CODE < 1) throw new Error('versionCode inválido no package.json.');

module.exports = { APP: 'com.casadababa.piloto', CHANNEL: 'piloto', NATIVE_VERSION_CODE, DATA_SCHEMA };
