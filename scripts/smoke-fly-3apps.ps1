#!/usr/bin/env pwsh
# Smoke HTTP estrutural dos 3 fly apps EasyStok pos-deploy.
# Uso: pwsh scripts/smoke-fly-3apps.ps1
# Sem auth (so endpoints publicos / health / login pages).

$ErrorActionPreference = 'Continue'
$results = @()

function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Url,
        [int[]]$ExpectedStatus = @(200),
        [int]$TimeoutSec = 10
    )
    $start = Get-Date
    try {
        $resp = Invoke-WebRequest -Uri $Url -TimeoutSec $TimeoutSec -SkipHttpErrorCheck -MaximumRedirection 0 -ErrorAction Stop
        $elapsed = ((Get-Date) - $start).TotalMilliseconds
        $ok = $ExpectedStatus -contains $resp.StatusCode
        return [pscustomobject]@{
            Name = $Name
            Url = $Url
            Status = $resp.StatusCode
            ElapsedMs = [math]::Round($elapsed, 0)
            Expected = ($ExpectedStatus -join ',')
            OK = $ok
            Body = if ($resp.Content.Length -lt 200) { $resp.Content } else { $resp.Content.Substring(0, 200) + '...' }
        }
    } catch {
        $elapsed = ((Get-Date) - $start).TotalMilliseconds
        return [pscustomobject]@{
            Name = $Name
            Url = $Url
            Status = 'ERROR'
            ElapsedMs = [math]::Round($elapsed, 0)
            Expected = ($ExpectedStatus -join ',')
            OK = $false
            Body = $_.Exception.Message
        }
    }
}

Write-Output "=== Smoke HTTP — 3 fly apps EasyStok ==="
Write-Output ""

$results += Test-Endpoint 'api-health-live' 'https://easystok.fly.dev/health/live' @(200)
$results += Test-Endpoint 'api-health' 'https://easystok.fly.dev/health' @(200)
$results += Test-Endpoint 'api-swagger' 'https://easystok.fly.dev/swagger/index.html' @(200, 401, 404)
$results += Test-Endpoint 'api-root' 'https://easystok.fly.dev/' @(200, 302, 404)

$results += Test-Endpoint 'admin-root' 'https://easystok-admin.fly.dev/' @(200, 302)
$results += Test-Endpoint 'admin-login' 'https://easystok-admin.fly.dev/Login' @(200, 302)

$results += Test-Endpoint 'web-root' 'https://easystok-web.fly.dev/' @(200, 302)
$results += Test-Endpoint 'web-login' 'https://easystok-web.fly.dev/Login' @(200, 302)
$results += Test-Endpoint 'web-health' 'https://easystok-web.fly.dev/health' @(200, 404)

$results | Format-Table -AutoSize Name, Status, ElapsedMs, Expected, OK

$failed = $results | Where-Object { -not $_.OK }
if ($failed.Count -gt 0) {
    Write-Output ""
    Write-Output "=== FALHAS ($($failed.Count)) ==="
    $failed | Format-Table -AutoSize Name, Url, Status, Body
    exit 1
}

Write-Output ""
Write-Output "=== OK: todos os smoke tests passaram ($($results.Count)/$($results.Count)) ==="
exit 0
