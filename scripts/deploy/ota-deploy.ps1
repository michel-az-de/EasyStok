[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DistributionDirectory,
    [string]$SshHost = 'hostinger',
    [switch]$Publish,
    [string]$PushCredentials = 'C:\rep\keystores\casa-da-baba-piloto\firebase-push.json',
    [string]$FirebaseConfig,
    [switch]$PushValidateOnly,
    [switch]$SkipPush
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$destination = '/opt/stacks/shared/docs/easystok-site/downloads/casa-da-baba/ota'
$publicBase = 'https://easystok.online/downloads/casa-da-baba/ota/'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$packageScript = Join-Path $root 'casa-da-baba-mobile/apk/scripts/ota-package.cjs'
$publicKey = Join-Path $root 'casa-da-baba-mobile/apk/android/app/src/main/assets/ota-public-key.der'
$output = (Resolve-Path -LiteralPath $DistributionDirectory).Path
if ($SshHost -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]*$') { throw 'Alias SSH inválido.' }
$releases = @(Get-ChildItem -LiteralPath $output -Filter 'release-*.json' -File)
if ($releases.Count -ne 1) { throw 'A distribuição deve conter exatamente uma release assinada.' }
$release = $releases[0]
$bundle = Join-Path $output 'bundle.zip'
$verification = & node $packageScript verify --release $release.FullName --bundle $bundle --public-key $publicKey
if ($LASTEXITCODE -ne 0) { throw 'Distribuição OTA inválida.' }
$payload = $verification | ConvertFrom-Json
if ($release.Name -ne "release-$($payload.sequence).json") { throw 'Nome da release diverge da sequência.' }
Write-Output "OTA conferida: conteúdo $($payload.bundleVersion), sequência $($payload.sequence), SHA256 $($payload.sha256)."
if (-not $Publish) {
    Write-Output 'Validação local concluída. Use -Publish para publicar e conferir por HTTPS.'
    return
}
if ($SkipPush -and $PushValidateOnly) { throw '-SkipPush e -PushValidateOnly são incompatíveis.' }
$pushScript = Join-Path $root 'casa-da-baba-mobile/apk/scripts/ota-push.cjs'
if (-not $SkipPush) {
    if ([string]::IsNullOrWhiteSpace($PushCredentials) -or -not (Test-Path -LiteralPath $PushCredentials -PathType Leaf)) {
        throw 'Credencial Firebase ausente. Nada foi enviado à VPS. Configure firebase-push.json ou use -SkipPush explicitamente para bootstrap/recuperação.'
    }
    if (-not $FirebaseConfig) { $FirebaseConfig = Join-Path $root 'casa-da-baba-mobile/apk/android/app/google-services.json' }
    if (-not (Test-Path -LiteralPath $FirebaseConfig -PathType Leaf)) { throw 'Configuração Firebase do APK ausente. Nada foi enviado à VPS.' }
    try {
        $preflight = & node $pushScript --credentials $PushCredentials --app-config $FirebaseConfig --release $release.FullName --public-key $publicKey --validate-only 2>&1
        if ($LASTEXITCODE -ne 0) { throw ($preflight -join ' ') }
        Write-Output ('Push validado antes da publicação: ' + ($preflight -join ' '))
    } catch {
        throw ('Push não validado; nada foi enviado à VPS. ' + $_.Exception.Message)
    }
} else {
    Write-Warning 'Publicação sem push explicitamente selecionada (-SkipPush).'
}
$stage = "$destination/.incoming-$([Guid]::NewGuid().ToString('N'))"
$remote = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ota-publish.py'))
function Invoke-OtaRemote([string]$Mode) {
    $remote | & ssh -o BatchMode=yes -o ConnectTimeout=15 $SshHost python3 - $destination $stage $Mode
    if ($LASTEXITCODE -ne 0) { throw "Falha na fase OTA '$Mode'. A fase install nunca altera latest." }
}
Invoke-OtaRemote 'stage'
foreach ($item in @(
    @{ Source = $bundle; Name = 'bundle.zip' },
    @{ Source = $release.FullName; Name = $release.Name },
    @{ Source = $publicKey; Name = 'ota-public-key.der' }
)) {
    & scp -q $item.Source "$($SshHost):$stage/$($item.Name)"
    if ($LASTEXITCODE -ne 0) { throw 'Transferência OTA falhou. latest permanece anterior.' }
}
Invoke-OtaRemote 'install'
# Um pacote só se torna latest após conferir os bytes servidos pela URL pública.
$download = Join-Path $output ('verification-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    Invoke-WebRequest -UseBasicParsing -MaximumRedirection 0 -Uri $payload.url -OutFile $download -TimeoutSec 180
    if ((Get-Item -LiteralPath $download).Length -ne $payload.sizeBytes -or (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $payload.sha256) {
        throw 'ZIP público divergente. latest não será promovido.'
    }
} finally {
    if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download -Force }
}
Invoke-OtaRemote 'activate'
$published = Invoke-RestMethod -MaximumRedirection 0 -Uri ($publicBase + 'latest.json?verify=' + [Guid]::NewGuid().ToString('N')) -TimeoutSec 30
$expected = Get-Content -LiteralPath $release.FullName -Raw | ConvertFrom-Json
if ($published.payloadBase64 -ne $expected.payloadBase64 -or $published.signatureBase64 -ne $expected.signatureBase64) {
    throw 'Manifesto público divergente. Confira a publicação antes de entregá-la.'
}
if (-not $SkipPush) {
    if (-not $FirebaseConfig) { $FirebaseConfig = Join-Path $root 'casa-da-baba-mobile/apk/android/app/google-services.json' }
    $pushMode = if ($PushValidateOnly) { '--validate-only' } else { '--send' }
    $pushScript = Join-Path $root 'casa-da-baba-mobile/apk/scripts/ota-push.cjs'
    try {
        $pushResult = & node $pushScript --credentials $PushCredentials --app-config $FirebaseConfig --release $release.FullName --public-key $publicKey $pushMode 2>&1
        if ($LASTEXITCODE -ne 0) { throw ($pushResult -join ' ') }
        Write-Output ($pushResult -join ' ')
    } catch {
        Write-Warning ("Versão publicada; push pendente. O manifesto permanece ativo, sem rollback automático. " + $_.Exception.Message)
        exit 1
    }
}
Write-Output "OTA publicada e conferida: sequência $($payload.sequence). Todas as versões anteriores foram preservadas."
