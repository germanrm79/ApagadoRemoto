param(
    [Parameter(Mandatory = $true)][string]$Package,
    [Parameter(Mandatory = $true)][string]$Log,
    [Parameter(Mandatory = $true)][string]$Result
)

$ErrorActionPreference = 'Stop'

function Write-UpdateResult {
    param([string]$Status, [string]$Message, [int]$Code)

    $payload = [ordered]@{
        Status      = $Status
        Message     = $Message
        ExitCode    = $Code
        FinishedUtc = (Get-Date).ToUniversalTime().ToString('o')
    }

    $directory = Split-Path -Parent $Result
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $temp = "$Result.$([guid]::NewGuid().ToString('N')).tmp"
    $payload | ConvertTo-Json -Compress | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $Result -Force
}

Start-Sleep -Seconds 5
$exitCode = -1

try {
    if (-not (Test-Path -LiteralPath $Package)) {
        throw "No se encontro el paquete de actualizacion."
    }

    & $Package /VERYSILENT /NORESTART /SUPPRESSMSGBOXES "/LOG=$Log"
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        throw "El instalador termino con codigo $exitCode."
    }

    Write-UpdateResult -Status 'Aplicado' -Message 'Actualizacion aplicada correctamente.' -Code $exitCode
}
catch {
    Write-UpdateResult -Status 'Error' -Message $_.Exception.Message -Code $exitCode
}
finally {
    try {
        Start-ScheduledTask -TaskName 'ApagadoRemoto Cliente' -ErrorAction Stop
    }
    catch {
    }
}
