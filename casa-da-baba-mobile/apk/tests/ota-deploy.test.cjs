'use strict';
// Exercita o script PowerShell real com SSH/HTTPS/FCM simulados. Não acessa a VPS.
const { test, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');
const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'cdb-deploy-test-'));
const deploy = path.resolve(__dirname, '../../../scripts/deploy/ota-deploy.ps1');
const quote = value => "'" + value.replaceAll("'", "''") + "'";
after(() => {
  const resolved = fs.realpathSync(temporary);
  assert.ok(resolved.startsWith(fs.realpathSync(os.tmpdir()) + path.sep) && path.basename(resolved).startsWith('cdb-deploy-test-'));
  fs.rmSync(resolved, { recursive: true, force: true });
});
function scenario(name) {
  const directory = fs.mkdtempSync(path.join(temporary, name + '-'));
  const bytes = Buffer.from('pacote de teste local');
  const hash = crypto.createHash('sha256').update(bytes).digest('hex');
  fs.writeFileSync(path.join(directory, 'bundle.zip'), bytes);
  fs.writeFileSync(path.join(directory, 'release-1.json'), JSON.stringify({ payloadBase64: 'fixture', signatureBase64: 'fixture' }));
  fs.writeFileSync(path.join(directory, 'service.json'), '{}');
  fs.writeFileSync(path.join(directory, 'google-services.json'), '{}');
  const wrapper = path.join(directory, 'invoke.ps1'), events = path.join(directory, 'events.json');
  const script = [
    // No Windows a saída redirecionada usa a página OEM; o assert compara acentos em UTF-8.
    '[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)',
    '$ErrorActionPreference = "Stop"',
    '$global:scenario = ' + quote(name),
    '$global:directory = ' + quote(directory),
    '$global:events = [Collections.Generic.List[string]]::new()',
    '$global:pushCalls = 0',
    '$global:payload = ' + quote(JSON.stringify({ nativeVersionCode: name === 'legacy-channel' ? 4 : 5, sequence: 1, bundleVersion: '0.3.1', sizeBytes: bytes.length, sha256: hash, url: 'https://easystok.online/downloads/casa-da-baba/ota/' + hash + '/bundle.zip' })) + ' | ConvertFrom-Json',
    'function node {',
    '  $global:LASTEXITCODE = 0',
    '  if ([string]$args[0] -like "*ota-package.cjs") { $global:events.Add("verify"); $global:payload | ConvertTo-Json -Compress; return }',
    '  $global:pushCalls++',
    '  $mode = if ($args -contains "--send") { "send" } else { "validate" }',
    '  $global:events.Add("push:" + $mode)',
    '  if ($global:scenario -eq "preflight-failure" -or ($global:scenario -eq "push-failure" -and $global:pushCalls -gt 1)) { $global:LASTEXITCODE = 1; Write-Output "falha FCM simulada"; return }',
    '  Write-Output \'{"status":"validated"}\'',
    '}',
    'function ssh { $global:events.Add("ssh:" + $args[-1]); $global:LASTEXITCODE = 0 }',
    'function scp { $global:events.Add("scp"); $global:LASTEXITCODE = 0 }',
    'function Invoke-WebRequest { param([switch]$UseBasicParsing, [int]$MaximumRedirection, [string]$Uri, [string]$OutFile, [int]$TimeoutSec)',
    '  $global:events.Add("download"); if ($global:scenario -eq "download-failure") { throw "download simulado falhou" }; Copy-Item -LiteralPath (Join-Path $global:directory "bundle.zip") -Destination $OutFile',
    '}',
    'function Invoke-RestMethod { param([int]$MaximumRedirection, [string]$Uri, [int]$TimeoutSec)',
    '  $expected = if ($global:payload.nativeVersionCode -ge 5) { "/ota/native-5/latest.json" } else { "/ota/latest.json" }; if (-not $Uri.Contains($expected)) { throw "Canal de manifesto incorreto" }; $global:events.Add("latest"); Get-Content -LiteralPath (Join-Path $global:directory "release-1.json") -Raw | ConvertFrom-Json',
    '}',
    '$options = @{ DistributionDirectory = $global:directory; Publish = $true; PushCredentials = (Join-Path $global:directory "service.json"); FirebaseConfig = (Join-Path $global:directory "google-services.json") }',
    'if ($global:scenario -eq "missing" -or $global:scenario -eq "skip") { $options.PushCredentials = Join-Path $global:directory "missing.json" }',
    'if ($global:scenario -eq "skip") { $options.SkipPush = $true }',
    'if ($global:scenario -eq "validate-only") { $options.PushValidateOnly = $true }',
    '$global:code = 0',
    'try { & ' + quote(deploy) + ' @options; if ($LASTEXITCODE -ne 0) { $global:code = $LASTEXITCODE } }',
    'catch { Write-Output $_.Exception.Message; $global:code = 1 }',
    'finally { [IO.File]::WriteAllText(' + quote(events) + ', (ConvertTo-Json -InputObject @($global:events.ToArray()) -Compress)) }',
    'exit $global:code'
  ].join('\n');
  fs.writeFileSync(wrapper, script);
  const result = spawnSync(process.env.POWERSHELL || 'pwsh', ['-NoProfile', '-NonInteractive', '-File', wrapper], { encoding: 'utf8' });
  assert.ok(!result.error, result.error?.message);
  return { ...result, events: fs.existsSync(events) ? JSON.parse(fs.readFileSync(events)) : [] };
}
test('credencial ausente e preflight FCM falho param antes de staging', () => {
  for (const name of ['missing', 'preflight-failure']) {
    const result = scenario(name);
    assert.notEqual(result.status, 0, result.stdout);
    assert.ok(!result.events.some(event => event.startsWith('ssh:') || event === 'scp'), JSON.stringify(result.events));
    assert.match(result.stdout, /Nada foi enviado|nada foi enviado/);
  }
});
test('SkipPush explícito publica sem FCM; PushValidateOnly nunca envia notificação', () => {
  const skip = scenario('skip');
  assert.equal(skip.status, 0, skip.stdout + skip.stderr);
  assert.ok(skip.events.includes('ssh:activate') && skip.events.includes('latest'));
  assert.equal(skip.events.filter(event => event.startsWith('push:')).length, 0);
  const validation = scenario('validate-only');
  assert.equal(validation.status, 0, validation.stdout + validation.stderr);
  assert.deepEqual(validation.events.filter(event => event.startsWith('push:')), ['push:validate', 'push:validate']);
});
test('falha de push após latest retorna erro explícito sem executar rollback', () => {
  const result = scenario('push-failure');
  assert.notEqual(result.status, 0);
  assert.deepEqual(result.events.filter(event => event.startsWith('ssh:')), ['ssh:stage', 'ssh:install', 'ssh:activate']);
  assert.ok(result.events.indexOf('latest') < result.events.indexOf('push:send'));
  assert.match(result.stdout, /Versão publicada; push pendente/);
  assert.match(result.stdout, /sem rollback automático/);
});
test('#2066 falha antes de ativar descarta o staging e mantém latest', () => {
  const result = scenario('download-failure');
  assert.notEqual(result.status, 0);
  assert.deepEqual(result.events.filter(event => event.startsWith('ssh:')), ['ssh:stage', 'ssh:install', 'ssh:discard']);
  assert.match(result.stdout, /download simulado falhou/);
});
test('#2066 publicação ativada não chama descarte, o activate já remove o staging', () => {
  const result = scenario('skip');
  assert.equal(result.status, 0, result.stdout + result.stderr);
  assert.equal(result.events.includes('ssh:discard'), false);
});

test('publicação legada ainda confere o manifesto global', () => {
 const result=scenario('legacy-channel'); assert.equal(result.status,0,result.stdout); assert.ok(result.events.includes('latest'));
});
