[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ApkPath,
    [Parameter(Mandatory)][string]$BuildToolsDirectory
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$dexdump = Join-Path $BuildToolsDirectory 'dexdump.exe'
if (-not (Test-Path -LiteralPath $dexdump -PathType Leaf)) { throw 'dexdump do Android SDK ausente.' }
$required = @{
    'Lcom/google/firebase/messaging/FirebaseMessaging;' = $false
    'Landroidx/datastore/preferences/PreferenceDataStoreDelegateKt;' = $false
    'Landroidx/datastore/migrations/SharedPreferencesMigration;' = $false
}
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('cdb-dex-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null
$files = @()
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ApkPath).Path)
try {
    foreach ($entry in $zip.Entries | Where-Object FullName -Match '^classes[0-9]*\.dex$') {
        $file = Join-Path $temporary $entry.FullName
        $files += $file
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $file)
        # Procura definições de classes, não referências que também existem no APK quebrado.
        & $dexdump $file | ForEach-Object {
            if ($_ -match "^\s*Class descriptor\s*:\s*'([^']+)'" -and $required.ContainsKey($Matches[1])) {
                $required[$Matches[1]] = $true
            }
        }
        if ($LASTEXITCODE -ne 0) { throw 'DEX do APK inválido.' }
    }
    $missing = @($required.Keys | Where-Object { -not $required[$_] })
    if ($missing.Count) { throw ('Runtime de push incompleto no APK: ' + ($missing -join ', ')) }
    Write-Output 'Runtime de push conferido no APK: Firebase Messaging e DataStore presentes.'
} finally {
    $zip.Dispose()
    foreach ($file in $files) { Remove-Item -LiteralPath $file -Force }
    Remove-Item -LiteralPath $temporary
}
