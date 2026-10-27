[CmdletBinding()]
param([string]$NodePath='', [string]$JavaHome=$env:JAVA_HOME, [string]$AndroidSdk=$env:ANDROID_HOME, [string]$ApiBaseUrl='https://api.easystok.online', [string]$SigningDirectory=$env:CDB_SIGNING_DIR, [switch]$InitializeSigning, [switch]$AllowDirty, [string]$FirebaseConfigPath='C:\rep\keystores\casa-da-baba-piloto\google-services.json')
$ErrorActionPreference='Stop'
$apkRoot=$PSScriptRoot
$utf8=[Text.UTF8Encoding]::new($false)
$sourceDirty = [bool](git -C $apkRoot status --porcelain)
if ($sourceDirty -and -not $AllowDirty) { throw 'Worktree suja. Faça commit ou use -AllowDirty apenas para validação local.' }
if (-not $SigningDirectory) { $SigningDirectory='C:\rep\keystores\casa-da-baba-piloto' }
if (-not (Test-Path -LiteralPath $FirebaseConfigPath -PathType Leaf)) { throw 'Configuração Firebase ausente. Informe -FirebaseConfigPath com o arquivo oficial do projeto casa-da-baba-app.' }
try { $firebaseConfig=Get-Content -LiteralPath $FirebaseConfigPath -Raw | ConvertFrom-Json } catch { throw 'Configuração Firebase JSON inválida.' }
$firebaseClient=$firebaseConfig.client | Where-Object { $_.client_info.android_client_info.package_name -eq 'com.casadababa.piloto' } | Select-Object -First 1
if ($firebaseConfig.project_info.project_id -ne 'casa-da-baba-app' -or -not $firebaseClient) { throw 'A configuração Firebase deve pertencer ao projeto casa-da-baba-app e ao pacote com.casadababa.piloto.' }
$firebaseApiKey=($firebaseClient.api_key | Select-Object -First 1).current_key
if ([string]::IsNullOrWhiteSpace($firebaseConfig.project_info.project_number) -or [string]::IsNullOrWhiteSpace($firebaseClient.client_info.mobilesdk_app_id) -or $firebaseApiKey -notmatch '^AIza[0-9A-Za-z_-]{35}$') { throw 'Configuração Firebase incompleta ou com valores de exemplo. Use o google-services.json oficial.' }
$firebaseConfigSha256=(Get-FileHash -LiteralPath $FirebaseConfigPath -Algorithm SHA256).Hash.ToLowerInvariant()
$firebaseDestination=Join-Path $apkRoot 'android\app\google-services.json'
if ([IO.Path]::GetFullPath($FirebaseConfigPath) -ne [IO.Path]::GetFullPath($firebaseDestination)) { Copy-Item -LiteralPath $FirebaseConfigPath -Destination $firebaseDestination -Force }
if ((Get-FileHash -LiteralPath $firebaseDestination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $firebaseConfigSha256) { throw 'A cópia da configuração Firebase divergiu da origem.' }

if (-not $NodePath) { $command=Get-Command node -ErrorAction SilentlyContinue; if ($command) { $NodePath=$command.Source } }
if (-not (Test-Path -LiteralPath $NodePath -PathType Leaf)) { throw 'Informe -NodePath para node.exe.' }
if (-not $JavaHome) {
    $jdk=Get-ChildItem -LiteralPath "$env:ProgramFiles\Android\openjdk" -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
    if ($jdk) { $JavaHome=$jdk.FullName }
}
if (-not $AndroidSdk) { $AndroidSdk=Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Android\android-sdk' }
$nodeDir=Split-Path -Parent $NodePath
$npmCli=Join-Path $nodeDir 'node_modules\npm\bin\npm-cli.js'
$keytool=Join-Path $JavaHome 'bin\keytool.exe'
$apksigner=Join-Path $AndroidSdk 'build-tools\36.0.0\apksigner.bat'
foreach ($required in @($npmCli,$keytool,$apksigner)) { if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Ferramenta ausente: $required" } }
$previous=@{}
foreach ($key in @('PATH','JAVA_HOME','ANDROID_HOME','CDB_API_BASE_URL','CDB_KEYSTORE','CDB_SIGNING_PASSWORD')) { $previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process') }
Push-Location $apkRoot
try {
    $env:JAVA_HOME=$JavaHome
    $env:ANDROID_HOME=$AndroidSdk
    $env:PATH="$nodeDir;$JavaHome\bin;$env:PATH"
    $env:CDB_API_BASE_URL=$ApiBaseUrl
    $signingDir=[IO.Path]::GetFullPath($SigningDirectory)
    $credentialsPath=Join-Path $signingDir 'credentials.json'
    $env:CDB_KEYSTORE=Join-Path $signingDir 'piloto-release.p12'
    if ((Test-Path -LiteralPath $credentialsPath) -ne (Test-Path -LiteralPath $env:CDB_KEYSTORE)) { throw 'Assinatura incompleta. Recupere o par de arquivos de .signing; não gere outra chave.' }
    if (-not (Test-Path -LiteralPath $credentialsPath)) {
        if (-not $InitializeSigning -or (Test-Path -LiteralPath (Join-Path $apkRoot 'releases'))) { throw 'Assinatura ausente. Recupere a chave existente; use -InitializeSigning somente na primeira criação, antes de qualquer release.' }
        [IO.Directory]::CreateDirectory($signingDir) | Out-Null
        $bytes=New-Object byte[] 32
        $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
        $env:CDB_SIGNING_PASSWORD=[Convert]::ToBase64String($bytes)
        & $keytool -genkeypair -keystore $env:CDB_KEYSTORE -storetype PKCS12 -alias piloto -storepass:env CDB_SIGNING_PASSWORD -keypass:env CDB_SIGNING_PASSWORD -keyalg RSA -keysize 3072 -validity 10000 -dname 'CN=Casa da Baba Piloto, O=Casa da Baba, C=BR' 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao criar a assinatura privada do piloto.' }
        [IO.File]::WriteAllText($credentialsPath,(@{password=$env:CDB_SIGNING_PASSWORD}|ConvertTo-Json),$utf8)
        $acl=Get-Acl -LiteralPath $signingDir
        $acl.SetAccessRuleProtection($true,$false)
        $identity=[Security.Principal.WindowsIdentity]::GetCurrent().Name
        $rule=[Security.AccessControl.FileSystemAccessRule]::new($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
        $acl.AddAccessRule($rule)
        Set-Acl -LiteralPath $signingDir -AclObject $acl
    } else { $env:CDB_SIGNING_PASSWORD=(Get-Content -LiteralPath $credentialsPath -Raw|ConvertFrom-Json).password }
    & $NodePath $npmCli ci --no-audit --no-fund 2>&1 | Select-Object -Last 8
    if ($LASTEXITCODE -ne 0) { throw 'npm ci falhou.' }
    & $NodePath (Join-Path $apkRoot 'scripts\copy-web.cjs')
    if ($LASTEXITCODE -ne 0) { throw 'Empacotamento da PWA falhou.' }
    & $NodePath (Join-Path $apkRoot 'node_modules\@capacitor\cli\bin\capacitor') sync android 2>&1 | Select-Object -Last 10
    if ($LASTEXITCODE -ne 0) { throw 'Capacitor sync falhou.' }
    [IO.File]::WriteAllText((Join-Path $apkRoot 'android\local.properties'),('sdk.dir='+$AndroidSdk.Replace('\','/')+[Environment]::NewLine),$utf8)
    Push-Location (Join-Path $apkRoot 'android')
    try {
        & .\gradlew.bat assembleRelease --no-daemon --console=plain 2>&1 | Select-Object -Last 35
        if ($LASTEXITCODE -ne 0) { throw 'Gradle assembleRelease falhou.' }
    } finally { Pop-Location }
    $builtApk=Join-Path $apkRoot 'android\app\build\outputs\apk\release\app-release.apk'
    $verification=& $apksigner verify --print-certs $builtApk 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'A verificação da assinatura do APK falhou.' }
    $certificateLine=$verification|Where-Object {$_ -match '^Signer #1 certificate SHA-256 digest: '}|Select-Object -First 1
    if (-not $certificateLine) { throw 'Fingerprint SHA-256 da assinatura ausente.' }
    $certificateSha=($certificateLine -replace '^Signer #1 certificate SHA-256 digest: ','').Trim().ToLowerInvariant()
    if ($certificateSha -notmatch '^[a-f0-9]{64}$') { throw 'Fingerprint de assinatura inválido.' }
    $package=Get-Content -LiteralPath (Join-Path $apkRoot 'package.json') -Raw|ConvertFrom-Json
    $releaseDir=Join-Path $apkRoot 'releases'
    [IO.Directory]::CreateDirectory($releaseDir)|Out-Null
    $outputApk=Join-Path $releaseDir "casa-da-baba-piloto-$($package.version).apk"
    Copy-Item -LiteralPath $builtApk -Destination $outputApk -Force
    $metadata=[ordered]@{
        packageId='com.casadababa.piloto'
        versionName=$package.version
        versionCode=[int]$package.versionCode
        sha256=(Get-FileHash -LiteralPath $outputApk -Algorithm SHA256).Hash.ToLowerInvariant()
        signingCertificateSha256=$certificateSha
        buildSha=(git rev-parse HEAD).Trim()
        sourceDirty=$sourceDirty
        builtAt=[DateTime]::UtcNow.ToString('o')
        apiBaseUrl=$ApiBaseUrl
        firebaseProjectId=$firebaseConfig.project_info.project_id
        firebaseConfigSha256=$firebaseConfigSha256
    }
    [IO.File]::WriteAllText("$outputApk.json",($metadata|ConvertTo-Json),$utf8)
    Write-Output "APK assinado e verificado: $outputApk"
    Write-Output "SHA-256: $($metadata.sha256)"
    Write-Output "Certificado SHA-256: $certificateSha"
} finally {
    Pop-Location
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key,$previous[$key],'Process') }
}
