'use strict';
// Publica somente artefatos assinados; a chave privada nunca acompanha a distribuição.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const zlib = require('node:zlib');
const { spawnSync } = require('node:child_process');
const APP = 'com.casadababa.piloto';
const NATIVE_VERSION = require('../package.json').versionCode;
const BASE = 'https://easystok.online/downloads/casa-da-baba/ota/';
const MAX_ZIP = 64 * 1024 * 1024;
const MAX_FILE = 32 * 1024 * 1024;
const MAX_TOTAL = 128 * 1024 * 1024;
const DEFAULT_PRIVATE = 'C:\\rep\\keystores\\casa-da-baba-piloto\\ota-signing-private.pem';
const DEFAULT_PUBLIC = path.resolve(__dirname, '../android/app/src/main/assets/ota-public-key.der');
const fields = 'appId,bundleVersion,channel,dataSchemaVersion,nativeVersionCode,schemaVersion,sequence,sha256,sizeBytes,url';
const sha256 = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
function assert(ok, message) { if (!ok) throw new Error(message); }
function validate(payload, minimumSequence = 0) {
  assert(Number.isSafeInteger(minimumSequence) && minimumSequence >= 0, 'Sequência mínima inválida.');
  assert(payload && Object.keys(payload).sort().join(',') === fields, 'Campos do manifesto inválidos.');
  assert(payload.schemaVersion === 1 && payload.appId === APP && payload.channel === 'piloto', 'Identidade OTA inválida.');
  assert([3, 4].includes(payload.nativeVersionCode) && payload.dataSchemaVersion === 2, 'Versão nativa ou esquema incompatível.');
  assert(Number.isSafeInteger(payload.sequence) && payload.sequence > minimumSequence && payload.sequence <= 2147483647, 'Sequência inválida ou replay.');
  assert(typeof payload.bundleVersion === 'string' && /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/.test(payload.bundleVersion), 'bundleVersion inválida.');
  assert(typeof payload.sha256 === 'string' && /^[a-f0-9]{64}$/.test(payload.sha256), 'SHA256 inválido.');
  assert(Number.isSafeInteger(payload.sizeBytes) && payload.sizeBytes > 0 && payload.sizeBytes <= MAX_ZIP, 'Tamanho inválido.');
  assert(payload.url === BASE + payload.sha256 + '/bundle.zip', 'URL OTA inválida.');
  return payload;
}
function privateKeyPath(file) {
  const resolved = path.resolve(file);
  for (let parent = path.dirname(resolved); ; parent = path.dirname(parent)) {
    assert(!fs.existsSync(path.join(parent, '.git')), 'Chave privada deve ficar fora de repositórios Git.');
    if (parent === path.dirname(parent)) break;
  }
  return resolved;
}
function publicKey(file) {
  return crypto.createPublicKey({ key: fs.readFileSync(file), type: 'spki', format: 'der' });
}
function decode(value, max) {
  assert(typeof value === 'string' && value.length <= max && value.length % 4 === 0, 'Base64 inválida.');
  const bytes = Buffer.from(value, 'base64');
  assert(bytes.toString('base64') === value, 'Base64 não canônica.');
  return bytes;
}
function verify(envelope, key, minimumSequence = 0) {
  assert(envelope && Object.keys(envelope).sort().join(',') === 'payloadBase64,signatureBase64', 'Envelope inválido.');
  const payload = decode(envelope.payloadBase64, 16384);
  const signature = decode(envelope.signatureBase64, 2048);
  assert(crypto.verify('RSA-SHA256', payload, { key, padding: crypto.constants.RSA_PKCS1_PADDING }, signature), 'Assinatura OTA inválida.');
  return validate(JSON.parse(payload.toString('utf8')), minimumSequence);
}
function sign(payload, privateFile, key) {
  validate(payload);
  const bytes = Buffer.from(JSON.stringify(payload));
  const privateKey = crypto.createPrivateKey(fs.readFileSync(privateKeyPath(privateFile)));
  assert(privateKey.asymmetricKeyType === 'rsa' && privateKey.asymmetricKeyDetails.modulusLength >= 3072, 'Chave OTA precisa ser RSA de pelo menos 3072 bits.');
  const envelope = { payloadBase64: bytes.toString('base64'), signatureBase64: crypto.sign('RSA-SHA256', bytes, { key: privateKey, padding: crypto.constants.RSA_PKCS1_PADDING }).toString('base64') };
  verify(envelope, key);
  return envelope;
}
function checkBundle(file, payload) {
  const stat = fs.lstatSync(file);
  assert(stat.isFile() && !stat.isSymbolicLink() && stat.size === payload.sizeBytes && stat.size <= MAX_ZIP, 'Tamanho do pacote divergente.');
  const bytes = fs.readFileSync(file);
  assert(sha256(bytes) === payload.sha256, 'Hash do pacote divergente.');
  return bytes;
}
function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
  }
  return (crc ^ 0xffffffff) >>> 0;
}
function zipWeb(directory, version) {
  const metadata = JSON.parse(fs.readFileSync(path.join(directory, 'bundle.json'), 'utf8'));
  assert(metadata.schemaVersion === 1 && metadata.appId === APP && metadata.channel === 'piloto' && metadata.bundleVersion === version && metadata.nativeVersionCode === NATIVE_VERSION && metadata.dataSchemaVersion === 2, 'bundle.json incompatível; gere web novamente.');
  assert(fs.statSync(path.join(directory, 'index.html')).isFile(), 'index.html ausente.');
  const files = [];
  function walk(relative) {
    for (const entry of fs.readdirSync(path.join(directory, relative)).sort()) {
      const name = relative ? relative + '/' + entry : entry;
      assert(!name.includes('\\') && !name.includes('\0') && !name.split('/').some(part => part === '.' || part === '..'), 'Caminho ZIP inválido.');
      const stat = fs.lstatSync(path.join(directory, name));
      assert(!stat.isSymbolicLink(), 'Links simbólicos não são permitidos no pacote.');
      if (stat.isDirectory()) walk(name);
      else { assert(stat.isFile(), 'Entrada não regular no pacote.'); files.push({ name, size: stat.size }); }
    }
  }
  walk('');
  assert(files.length > 0 && files.length <= 4096 && files.reduce((sum, f) => sum + f.size, 0) <= MAX_TOTAL, 'Limite do pacote expandido excedido.');
  let offset = 0;
  const records = [], central = [];
  for (const file of files) {
    assert(file.size <= MAX_FILE, 'Arquivo excede 32 MiB: ' + file.name);
    const raw = fs.readFileSync(path.join(directory, file.name));
    const compressed = zlib.deflateRawSync(raw, { level: 9 });
    const name = Buffer.from(file.name, 'utf8');
    const header = Buffer.alloc(30);
    header.writeUInt32LE(0x04034b50);
    header.writeUInt16LE(20, 4); header.writeUInt16LE(0x800, 6); header.writeUInt16LE(8, 8);
    header.writeUInt16LE(33, 12); // 1980-01-01: pacote reprodutível.
    header.writeUInt32LE(crc32(raw), 14); header.writeUInt32LE(compressed.length, 18); header.writeUInt32LE(raw.length, 22);
    header.writeUInt16LE(name.length, 26);
    const directoryRecord = Buffer.alloc(46);
    directoryRecord.writeUInt32LE(0x02014b50); directoryRecord.writeUInt16LE(20, 4);
    header.copy(directoryRecord, 6, 4, 30);
    directoryRecord.writeUInt32LE(offset, 42);
    records.push(header, name, compressed); central.push(directoryRecord, name);
    offset += header.length + name.length + compressed.length;
  }
  const directoryBytes = Buffer.concat(central);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50); end.writeUInt16LE(files.length, 8); end.writeUInt16LE(files.length, 10);
  end.writeUInt32LE(directoryBytes.length, 12); end.writeUInt32LE(offset, 16);
  const zip = Buffer.concat([...records, directoryBytes, end]);
  assert(zip.length <= MAX_ZIP, 'ZIP excede 64 MiB.');
  return zip;
}
function writeImmutable(file, bytes) {
  if (fs.existsSync(file)) { assert(fs.readFileSync(file).equals(Buffer.from(bytes)), 'Artefato imutável já existe com outro conteúdo: ' + file); return; }
  fs.writeFileSync(file, bytes, { flag: 'wx' });
}
function writeRelease(output, bytes, payload, privateFile, key) {
  const envelope = sign(payload, privateFile, key);
  fs.mkdirSync(output, { recursive: true });
  // O diretório é exclusivo de uma release. Não sobrescreva uma distribuição anterior.
  const releaseName = 'release-' + payload.sequence + '.json';
  assert(fs.readdirSync(output).every(name => name === 'bundle.zip' || name === releaseName), 'Diretório de saída contém outra distribuição.');
  writeImmutable(path.join(output, 'bundle.zip'), bytes);
  writeImmutable(path.join(output, releaseName), JSON.stringify(envelope) + '\n');
  return payload;
}
function initializeKey(privateFile, publicFile) {
  privateFile = privateKeyPath(privateFile);
  assert(!fs.existsSync(privateFile) && !fs.existsSync(publicFile), 'Chave existente: não gerar nem sobrescrever. Preserve o par original.');
  const pair = crypto.generateKeyPairSync('rsa', { modulusLength: 3072, publicKeyEncoding: { type: 'spki', format: 'der' }, privateKeyEncoding: { type: 'pkcs8', format: 'pem' } });
  fs.mkdirSync(path.dirname(privateFile), { recursive: true });
  fs.writeFileSync(privateFile, pair.privateKey, { mode: 0o600, flag: 'wx' });
  if (process.platform === 'win32') {
    const user = (process.env.USERDOMAIN ? process.env.USERDOMAIN + '\\' : '') + process.env.USERNAME;
    const result = spawnSync('icacls.exe', [privateFile, '/inheritance:r', '/grant:r', user + ':(F)'], { windowsHide: true, encoding: 'utf8' });
    assert(result.status === 0, 'Falha ao restringir ACL da chave privada. Corrija a ACL antes de usá-la.');
  }
  fs.mkdirSync(path.dirname(publicFile), { recursive: true });
  fs.writeFileSync(publicFile, pair.publicKey, { flag: 'wx' });
  return { publicKeySha256: sha256(pair.publicKey) };
}
function main(argv) {
  const command = argv.shift();
  const args = {};
  while (argv.length) {
    const flag = argv.shift(), value = argv.shift();
    assert(/^--[a-z-]+$/.test(flag) && value && !value.startsWith('--') && !Object.hasOwn(args, flag.slice(2)), 'Argumentos inválidos.');
    args[flag.slice(2)] = value;
  }
  const privateFile = args['private-key'] || DEFAULT_PRIVATE, publicFile = args['public-key'] || DEFAULT_PUBLIC;
  if (command === 'key-init') return initializeKey(privateFile, publicFile);
  const key = publicKey(publicFile);
  if (command === 'verify') {
    const payload = verify(JSON.parse(fs.readFileSync(args.release, 'utf8')), key, Number(args['minimum-sequence'] || 0));
    if (args.bundle) checkBundle(args.bundle, payload);
    return payload;
  }
  const sequence = Number(args.sequence), output = path.resolve(args.output || '');
  assert(args.output && Number.isSafeInteger(sequence), 'Informe --output e --sequence.');
  let bytes, payload;
  if (command === 'build') {
    assert(args['web-dir'] && args.version, 'Informe --web-dir e --version.');
    bytes = zipWeb(path.resolve(args['web-dir']), args.version);
    const hash = sha256(bytes);
    payload = { schemaVersion: 1, appId: APP, channel: 'piloto', sequence, bundleVersion: args.version, nativeVersionCode: NATIVE_VERSION, dataSchemaVersion: 2, url: BASE + hash + '/bundle.zip', sizeBytes: bytes.length, sha256: hash };
  } else if (command === 'rollback') {
    assert(args.release && args.bundle, 'Informe --release e --bundle da versão preservada.');
    const previous = verify(JSON.parse(fs.readFileSync(args.release, 'utf8')), key);
    assert(sequence > previous.sequence, 'Rollback exige sequência nova maior que a release escolhida.');
    bytes = checkBundle(args.bundle, previous);
    payload = { ...previous, sequence };
  } else throw new Error('Comandos: key-init, build, rollback, verify.');
  return writeRelease(output, bytes, payload, privateFile, key);
}
module.exports = { APP, BASE, validate, verify, sign, checkBundle, zipWeb, writeRelease, initializeKey, main };
if (require.main === module) {
  try { console.log(JSON.stringify(main(process.argv.slice(2)))); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
