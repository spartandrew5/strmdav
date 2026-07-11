param(
    [string]$ContainerName = "strmdav-test"
)

$ErrorActionPreference = "Stop"

try {
    docker rm -f $ContainerName | Out-Null
    Write-Host "Stopped $ContainerName"
} catch {
    Write-Host "$ContainerName is not running"
}