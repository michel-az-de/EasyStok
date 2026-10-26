[CmdletBinding()]
param([string]$JavaHome=$env:JAVA_HOME, [string]$JsonJar='')
$ErrorActionPreference='Stop'
if (-not $JavaHome) {
    $JavaHome=Get-ChildItem -LiteralPath "$env:ProgramFiles\Android\openjdk" -Directory |
        Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $JsonJar) {
    $jsonCache=Join-Path $env:USERPROFILE '.gradle\caches\modules-2\files-2.1\org.json\json'
    $JsonJar=Get-ChildItem -LiteralPath $jsonCache -Recurse -Filter '*.jar' |
        Sort-Object FullName | Select-Object -First 1 -ExpandProperty FullName
}
if (-not (Test-Path -LiteralPath $JsonJar -PathType Leaf)) { throw 'Informe -JsonJar com o org.json já usado pelo ambiente Gradle. Não é dependência do APK.' }
$apkRoot=Split-Path -Parent $PSScriptRoot
$output=Join-Path $apkRoot 'android/app/build/ota-host-test'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sources=@(Get-ChildItem (Join-Path $PSScriptRoot 'java') -Recurse -Filter '*.java' | Select-Object -ExpandProperty FullName)
$sourceRoot=Join-Path $apkRoot 'android/app/src/main/java/com/casadababa/app/updater'
$sources+=Join-Path $sourceRoot 'UpdatePolicy.java'
$sources+=Join-Path $sourceRoot 'OtaManager.java'
& (Join-Path $JavaHome 'bin/javac.exe') -encoding UTF-8 -cp $JsonJar -d $output $sources
if ($LASTEXITCODE -ne 0) { throw 'Compilação do harness nativo falhou.' }
& (Join-Path $JavaHome 'bin/java.exe') -cp "$output;$JsonJar" com.casadababa.app.updater.UpdaterTest
if ($LASTEXITCODE -ne 0) { throw 'Teste do atualizador nativo falhou.' }
