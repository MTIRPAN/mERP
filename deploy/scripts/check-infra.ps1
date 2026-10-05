# Waits until every docker-compose service with a healthcheck is healthy, then probes the Aspire Dashboard
# (its chiseled image cannot run a healthcheck). Usage: deploy/scripts/check-infra.ps1 [-TimeoutSeconds 180]
param([int]$TimeoutSeconds = 180)

$ErrorActionPreference = "Stop"
$services = @("postgres", "valkey", "rabbitmq", "keycloak", "mailpit", "gotenberg")
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)

foreach ($service in $services) {
    while ($true) {
        $container = docker compose ps -q $service
        $status = if ($container) { docker inspect -f "{{.State.Health.Status}}" $container } else { "missing" }
        if ($status -eq "healthy") { Write-Host "OK   $service"; break }
        if ((Get-Date) -gt $deadline) { Write-Host "FAIL ${service}: $status"; exit 1 }
        Start-Sleep -Seconds 2
    }
}

$port = if ($env:ASPIRE_UI_PORT) { $env:ASPIRE_UI_PORT } else { "18888" }
while ($true) {
    try { Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:$port/" | Out-Null; break }
    catch {
        if ((Get-Date) -gt $deadline) { Write-Host "FAIL aspire-dashboard"; exit 1 }
        Start-Sleep -Seconds 2
    }
}
Write-Host "OK   aspire-dashboard"
