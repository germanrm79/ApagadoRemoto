$ErrorActionPreference = 'Stop'

$systemTaskName = 'ApagadoRemoto Cliente'
$wallpaperTaskName = 'ApagadoRemoto Fondo Escritorio'
$exe = Join-Path $PSScriptRoot 'Apagado Remoto.exe'
$workDir = $PSScriptRoot
$logDir = Join-Path $env:ProgramData 'ApagadoRemoto'
$logFile = Join-Path $logDir 'task-install.log'

function Write-Log([string]$message) {
    try {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
        Add-Content -LiteralPath $logFile -Value ("[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $message)
    }
    catch { }
}

try {
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "No se encontro el ejecutable del cliente en: $exe"
    }

    # Carpeta compartida: SYSTEM escribe el fondo, los usuarios solo lo leen.
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    & icacls.exe $logDir /grant '*S-1-5-32-545:(OI)(CI)RX' /T | Out-Null

    # Listener en segundo plano: arranca con el equipo, sin requerir sesion, como SYSTEM.
    $systemAction = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $workDir
    $startupTrigger = New-ScheduledTaskTrigger -AtStartup
    $systemPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $systemSettings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Minutes 1)

    Register-ScheduledTask -TaskName $systemTaskName -Action $systemAction -Trigger $startupTrigger `
        -Principal $systemPrincipal -Settings $systemSettings `
        -Description 'Inicia el cliente de ApagadoRemoto al arrancar el equipo, sin requerir sesion y con privilegios de administrador.' `
        -Force | Out-Null
    Write-Log "Tarea '$systemTaskName' registrada."

    # Aplica el ultimo fondo a cada usuario cuando inicia sesion (incluidos usuarios nuevos).
    $wallpaperAction = New-ScheduledTaskAction -Execute $exe -Argument '--apply-pending-wallpaper' -WorkingDirectory $workDir
    $logonTrigger = New-ScheduledTaskTrigger -AtLogOn
    $usersPrincipal = New-ScheduledTaskPrincipal -GroupId 'S-1-5-32-545' -RunLevel Limited
    # Sin limite de tiempo: si hay un bloqueo pendiente, el aviso debe permanecer
    # abierto hasta que el administrador desbloquee el equipo.
    $wallpaperSettings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -MultipleInstances Parallel `
        -ExecutionTimeLimit ([TimeSpan]::Zero)

    Register-ScheduledTask -TaskName $wallpaperTaskName -Action $wallpaperAction -Trigger $logonTrigger `
        -Principal $usersPrincipal -Settings $wallpaperSettings `
        -Description 'Aplica el fondo de escritorio configurado por ApagadoRemoto al iniciar sesion cualquier usuario.' `
        -Force | Out-Null
    Write-Log "Tarea '$wallpaperTaskName' registrada."

    try {
        Start-ScheduledTask -TaskName $systemTaskName -ErrorAction Stop
        Write-Log "Tarea '$systemTaskName' iniciada."
    }
    catch {
        Write-Log "No se pudo iniciar '$systemTaskName': $($_.Exception.Message)"
    }
}
catch {
    Write-Log "ERROR: $($_.Exception.Message)"
    throw
}
