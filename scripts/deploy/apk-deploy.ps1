[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ApkPath,
    [string]$MetadataPath,
    [string[]]$ReleaseNotes = @(),
    [string]$BuildToolsDirectory,
    [string]$JavaHome,
    [string]$SshHost = 'hostinger',
    [string]$OutputDirectory,
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$destination = '/opt/stacks/shared/docs/easystok-site/downloads/casa-da-baba'
$publicBase = 'https://easystok.online/downloads/casa-da-baba/'
$expectedPackage = 'com.casadababa.piloto'
if ($SshHost -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_.-]*$') { throw 'Alias SSH inválido.' }
$apk = (Resolve-Path -LiteralPath $ApkPath).Path
if (-not $MetadataPath) { $MetadataPath = "$apk.json" }
$metadata = Get-Content -LiteralPath $MetadataPath -Raw | ConvertFrom-Json
foreach ($field in @('packageId', 'versionName', 'versionCode', 'sha256', 'signingCertificateSha256', 'buildSha', 'apiBaseUrl', 'sourceDirty')) {
    if ($null -eq $metadata.PSObject.Properties[$field] -or [string]::IsNullOrWhiteSpace([string]$metadata.$field)) { throw "Metadado obrigatório ausente: $field" }
}
if ($metadata.packageId -ne $expectedPackage) { throw "Este distribuidor aceita somente $expectedPackage." }
if ([string]$metadata.versionCode -notmatch '^[1-9][0-9]{0,9}$' -or [long]$metadata.versionCode -gt 2100000000) { throw 'versionCode inválido.' }
if ([string]$metadata.versionName -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][A-Za-z0-9.-]+)?$') { throw 'versionName inválido.' }
if ([string]$metadata.buildSha -notmatch '^[a-fA-F0-9]{40}$') { throw 'buildSha deve identificar o commit completo.' }
if ($metadata.sourceDirty -isnot [bool]) { throw 'sourceDirty deve ser booleano nos metadados do build.' }
$sourceRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$currentSha = & git -C $sourceRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $currentSha -ne $metadata.buildSha) { throw 'O buildSha do APK não corresponde ao commit desta cópia do código.' }
$sourceChanges = & git -C $sourceRoot status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'Não foi possível conferir o estado do código-fonte.' }
if (-not $PrepareOnly -and ($metadata.sourceDirty -or $sourceChanges)) { throw 'Publicação bloqueada: faça commit do código e gere outro APK a partir da árvore limpa.' }
if ($PrepareOnly -and ($metadata.sourceDirty -or $sourceChanges)) { Write-Output 'Ensaio local com alterações não commitadas; este artefato não pode ser publicado.' }
if ($metadata.apiBaseUrl -ne 'https://api.easystok.online') { throw 'O APK deve apontar para a API pública atual do EasyStok.' }
$sha = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sha -ne ([string]$metadata.sha256).ToLowerInvariant()) { throw 'O SHA-256 do APK diverge dos metadados do build.' }
$certificate = ([string]$metadata.signingCertificateSha256).Replace(':', '').ToLowerInvariant()
if ($certificate -notmatch '^[a-f0-9]{64}$') { throw 'Certificado de assinatura inválido nos metadados.' }

if (-not $BuildToolsDirectory) {
    $sdkRoots = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA 'Android\Sdk'), 'C:\Program Files (x86)\Android\android-sdk') | Where-Object { $_ }
    foreach ($sdkRoot in $sdkRoots) {
        $buildToolsRoot = Join-Path $sdkRoot 'build-tools'
        if (Test-Path -LiteralPath $buildToolsRoot) {
            $candidate = Get-ChildItem -LiteralPath $buildToolsRoot -Directory | Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
            if ($candidate) { $BuildToolsDirectory = $candidate.FullName; break }
        }
    }
}
if (-not $BuildToolsDirectory) { throw 'Informe -BuildToolsDirectory para verificar assinatura e versão do APK.' }
$signer = Join-Path $BuildToolsDirectory 'apksigner.bat'
$aapt = Join-Path $BuildToolsDirectory 'aapt.exe'
if (-not (Test-Path -LiteralPath $signer) -or -not (Test-Path -LiteralPath $aapt)) { throw 'apksigner.bat e aapt.exe são obrigatórios no Android Build Tools.' }
$previousJavaHome = $env:JAVA_HOME
try {
    if ($JavaHome) { $env:JAVA_HOME = $JavaHome }
    elseif (-not $env:JAVA_HOME -and (Test-Path -LiteralPath 'C:\Program Files\Android\openjdk\jdk-21.0.8')) { $env:JAVA_HOME = 'C:\Program Files\Android\openjdk\jdk-21.0.8' }
    $verification = & $signer verify --print-certs $apk 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'apksigner rejeitou a assinatura do APK.' }
    $signedCertificate = [regex]::Match(($verification -join "`n"), 'Signer #1 certificate SHA-256 digest:\s*([a-fA-F0-9:]+)').Groups[1].Value.Replace(':', '').ToLowerInvariant()
    if ($signedCertificate -ne $certificate) { throw 'A assinatura real do APK diverge dos metadados.' }
    $badging = & $aapt dump badging $apk 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível ler a identidade do APK.' }
    $identity = [regex]::Match(($badging -join "`n"), "package: name='([^']+)' versionCode='([^']+)' versionName='([^']+)'")
    if (-not $identity.Success -or $identity.Groups[1].Value -ne $expectedPackage -or $identity.Groups[2].Value -ne [string]$metadata.versionCode -or $identity.Groups[3].Value -ne $metadata.versionName) { throw 'A identidade ou versão interna do APK diverge dos metadados.' }
} finally { $env:JAVA_HOME = $previousJavaHome }

$fileName = "casa-da-baba-$($metadata.versionCode)-$($sha.Substring(0, 12)).apk"
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot "../../.build/apk-distribution-$($metadata.versionCode)-$($sha.Substring(0, 12))" }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$manifest = [ordered]@{
    schemaVersion = 1
    appId = $expectedPackage
    version = [string]$metadata.versionName
    versionCode = [long]$metadata.versionCode
    sha256 = $sha
    signingCertificateSha256 = $certificate
    buildSha = [string]$metadata.buildSha
    apiBaseUrl = [string]$metadata.apiBaseUrl
    sizeBytes = (Get-Item -LiteralPath $apk).Length
    fileName = $fileName
    url = "$publicBase$fileName"
    publishedAt = [DateTime]::UtcNow.ToString('o')
    notes = @($ReleaseNotes)
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $output 'latest.json'), ($manifest | ConvertTo-Json -Depth 5), $utf8)
Copy-Item -LiteralPath $apk -Destination (Join-Path $output $fileName) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'apk-site/index.html') -Destination (Join-Path $output 'index.html') -Force
Write-Output "APK conferido: $($manifest.version) ($($manifest.versionCode)); pacote e assinatura válidos."
Write-Output "Distribuição preparada: $output"
if ($PrepareOnly) { return }

# O manifesto só muda depois de conferir o APK baixado pelo HTTPS público.
$stage = "$destination/.incoming-$([Guid]::NewGuid().ToString('N'))"
$remote = @'
import fcntl, hashlib, json, os, re, sys
from pathlib import Path
root, staging, mode = map(str, sys.argv[1:4])
root, staging = Path(root), Path(staging)
assert str(root) == '/opt/stacks/shared/docs/easystok-site/downloads/casa-da-baba'
assert staging.parent == root and re.fullmatch(r'\.incoming-[a-f0-9]{32}', staging.name)
if mode == 'stage':
    root.mkdir(parents=True, exist_ok=True)
    staging.mkdir()
    sys.exit(0)

def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1048576), b''): value.update(block)
    return value.hexdigest()

with (root / '.publish.lock').open('a') as lock:
    fcntl.flock(lock, fcntl.LOCK_EX)
    release = json.loads((staging / 'latest.json').read_text())
    code = release['versionCode']
    assert release['schemaVersion'] == 1
    assert isinstance(code, int) and 0 < code <= 2100000000
    assert release['appId'] == 'com.casadababa.piloto'
    assert re.fullmatch(r'[a-f0-9]{64}', release['sha256'])
    assert re.fullmatch(r'[a-f0-9]{64}', release['signingCertificateSha256'])
    filename = f"casa-da-baba-{code}-{release['sha256'][:12]}.apk"
    assert release['fileName'] == filename
    assert release['url'] == 'https://easystok.online/downloads/casa-da-baba/' + filename
    immutable = root / f'release-{code}.json'
    for previous_path in (root / 'latest.json', immutable):
        if not previous_path.exists(): continue
        previous = json.loads(previous_path.read_text())
        if previous['appId'] != release['appId'] or previous['signingCertificateSha256'] != release['signingCertificateSha256']:
            raise SystemExit('Recusado: pacote ou certificado diferente do publicado.')
        if previous['versionCode'] > code: raise SystemExit('Recusado: downgrade do manifesto latest.')
        if previous['versionCode'] == code and (previous['sha256'] != release['sha256'] or previous['version'] != release['version']):
            raise SystemExit('Recusado: versionCode existente com outro conteúdo.')
    target = root / filename
    if mode == 'apk':
        source = staging / filename
        if digest(source) != release['sha256'] or source.stat().st_size != release['sizeBytes']:
            raise SystemExit('Recusado: arquivo transferido divergente.')
        if target.exists():
            if digest(target) != release['sha256']: raise SystemExit('Recusado: arquivo imutável já existe com outro conteúdo.')
        else:
            os.link(source, target)
        if not immutable.exists():
            with immutable.open('x') as stream: json.dump(release, stream, ensure_ascii=False, indent=2)
        print('APK e registro disponíveis; latest ainda não alterado.')
    elif mode == 'activate':
        if not target.exists() or digest(target) != release['sha256']: raise SystemExit('APK publicado ausente ou divergente.')
        # Repetir a publicação preserva o registro e a data originais.
        release = json.loads(immutable.read_text())
        (staging / 'latest.json').write_text(json.dumps(release, ensure_ascii=False, indent=2), encoding='utf-8')
        os.replace(staging / 'index.html', root / 'index.html')
        os.replace(staging / 'latest.json', root / 'latest.json')
        for path in staging.iterdir():
            if path.is_file(): path.unlink()
        staging.rmdir()
        print('Manifesto latest ativado.')
    else:
        raise SystemExit('Modo desconhecido.')
'@
function Invoke-ApkRemote([string]$Mode) {
    $remote | & ssh -o BatchMode=yes -o ConnectTimeout=15 $SshHost python3 - $destination $stage $Mode
    if ($LASTEXITCODE -ne 0) { throw "Falha na fase remota '$Mode'; latest não foi promovido por esta fase." }
}
Invoke-ApkRemote 'stage'
foreach ($name in @($fileName, 'latest.json', 'index.html')) {
    & scp -q (Join-Path $output $name) "${SshHost}:$stage/$name"
    if ($LASTEXITCODE -ne 0) { throw "Falha ao transferir $name; latest permanece anterior." }
}
Invoke-ApkRemote 'apk'
$publicCopy = Join-Path $output 'verification-download.apk'
try {
    Invoke-WebRequest -UseBasicParsing -Uri ($manifest.url + '?verify=' + [Guid]::NewGuid().ToString('N')) -OutFile $publicCopy -TimeoutSec 180
    if ((Get-FileHash -LiteralPath $publicCopy -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sha) { throw 'O APK servido por HTTPS diverge do build. latest não será promovido.' }
} finally {
    if (Test-Path -LiteralPath $publicCopy) { Remove-Item -LiteralPath $publicCopy -Force }
}
Invoke-ApkRemote 'activate'
$published = Invoke-RestMethod -Uri ($publicBase + 'latest.json?verify=' + [Guid]::NewGuid().ToString('N')) -TimeoutSec 30
if ($published.versionCode -ne $manifest.versionCode -or $published.sha256 -ne $sha -or $published.url -ne $manifest.url) { throw 'A versão pública diverge da esperada. Conferir a publicação antes de entregar o link.' }
$page = Invoke-WebRequest -UseBasicParsing -Uri ($publicBase + '?verify=' + [Guid]::NewGuid().ToString('N')) -TimeoutSec 30
if ($page.StatusCode -ne 200 -or $page.Content -notmatch 'Baixar app para Android') { throw 'A página pública de instalação não passou na verificação.' }
Write-Output "Publicado e conferido por HTTPS: $publicBase"
Write-Output "APK: $($manifest.url)"
Write-Output "SHA-256: $sha"
