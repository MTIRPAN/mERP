# Obtains a token for every development user from the local Keycloak and verifies the "tenant" claim.
# Usage: deploy/scripts/check-tokens.ps1 [-KeycloakUrl http://localhost:8080]
param([string]$KeycloakUrl = $(if ($env:KEYCLOAK_URL) { $env:KEYCLOAK_URL } else { "http://localhost:8080" }))

$ErrorActionPreference = "Stop"
$tokenUrl = "$KeycloakUrl/realms/merp/protocol/openid-connect/token"
$expectedTenant = "demo"
$users = @("admin", "ik", "yonetici", "calisan")

function ConvertFrom-JwtPayload([string]$token) {
    $payload = $token.Split(".")[1].Replace("-", "+").Replace("_", "/")
    while ($payload.Length % 4 -ne 0) { $payload += "=" }
    [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($payload)) | ConvertFrom-Json
}

$failed = $false
foreach ($user in $users) {
    try {
        $response = Invoke-RestMethod -Method Post -Uri $tokenUrl -Body @{
            grant_type = "password"; client_id = "merp-web"; scope = "openid"; username = $user; password = $user
        }
    }
    catch {
        Write-Host "FAIL ${user}: $($_.Exception.Message)"
        $failed = $true
        continue
    }

    $claims = ConvertFrom-JwtPayload $response.access_token
    if ($claims.tenant -eq $expectedTenant) {
        Write-Host "OK   ${user}: tenant=$expectedTenant"
    }
    else {
        Write-Host "FAIL ${user}: tenant claim missing or not '$expectedTenant'"
        $failed = $true
    }
}

if ($failed) { exit 1 }
