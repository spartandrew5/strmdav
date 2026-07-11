param(
    [string]$ImageTag = "strmdav:test",
    [string]$ContainerName = "strmdav-test",
    [int]$Port = 3001,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"

docker volume create strmdav-test-config | Out-Null
docker volume create strmdav-test-mnt | Out-Null

try {
    docker rm -f $ContainerName | Out-Null
} catch {
}

docker build -t $ImageTag .

docker run -d `
    --name $ContainerName `
    -p "${Port}:3001" `
    -e PUID=1000 `
    -e PGID=1000 `
    -v strmdav-test-config:/config `
    -v strmdav-test-mnt:/mnt `
    $ImageTag | Out-Null

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:$Port/health" -TimeoutSec 5
        if ($response.StatusCode -eq 200) {
            Write-Host "Server is running on http://localhost:$Port"
            exit 0
        }
    } catch {
        Start-Sleep -Seconds 2
    }
}

Write-Error "Timed out waiting for the local StrmDAV container to become healthy."
exit 1