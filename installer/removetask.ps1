$ErrorActionPreference = 'Continue'

$taskNames = @('ApagadoRemoto Cliente', 'ApagadoRemoto Fondo Escritorio')

foreach ($name in $taskNames) {
    Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
}

Get-Process -Name 'Apagado Remoto', 'APR', 'Apagado.Cliente' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

foreach ($name in $taskNames) {
    try {
        Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
    }
    catch {
        & schtasks.exe /Delete /TN $name /F 2>&1 | Out-Null
    }
}
