# AGENTS.md

## Proyecto

ApagadoRemoto permite apagar, reiniciar, cancelar apagados, bloquear con un mensaje y cambiar el fondo de escritorio de equipos desde otro PC de la LAN.

Solucion: `ApagadoRemoto.slnx` (formato `.slnx`).

- `src/Apagado.Core`: DTOs, enums y protocolo TCP compartido. Target `net8.0`.
- `src/Apagado.Cliente`: consola que escucha comandos. Target `net8.0-windows`.
- `src/Apagado.Servidor`: WPF (interfaz en español). Target `net8.0-windows`.

## Comandos

```powershell
# Compilar todo (comando de verificacion)
dotnet build ApagadoRemoto.slnx -c Release

# Ejecutar servidor
dotnet run --project src/Apagado.Servidor -c Release

# Ejecutar cliente
dotnet run --project src/Apagado.Cliente -c Release

# Publicar cliente self-contained (salida usada por el instalador)
dotnet publish src/Apagado.Cliente/Apagado.Cliente.csproj -c Release -r win-x64 --self-contained true -o publish\Cliente

# Regenerar instalador (Inno Setup 7.1.1)
& "C:\Program Files\Inno Setup 7\ISCC.exe" /Q installer\ApagadoRemotoCliente.iss
```

No hay proyecto de tests ni linter configurado. Verificar siempre con `dotnet build` sin errores ni advertencias.

## Version y empaquetado

- Version actual del cliente: `1.3.0` (`Version`/`InformationalVersion`) y `1.3.0.0` (`FileVersion`/`AssemblyVersion`) en `src/Apagado.Cliente/Apagado.Cliente.csproj`; `AppVersion=1.3.0` en `installer/ApagadoRemotoCliente.iss`.
- El ejecutable se llama `Apagado Remoto.exe` (`AssemblyName=Apagado Remoto` en el `.csproj`); `ProductName` y descripcion tambien son `Apagado Remoto`.
- Salida publicada usada por el instalador: `publish\Cliente`. Instalador final: `installer\ApagadoRemotoClienteSetup.exe`.
- Al cambiar codigo del cliente: subir version en el `.csproj` y en el `.iss`, publicar y recompilar el instalador.

## Estado actual (05/11/2026)

- Corregido el error "Access path is denied": si el cliente no corre como SYSTEM usa `%LOCALAPPDATA%\ApagadoRemoto`; si corre como SYSTEM escribe en `%ProgramData%\ApagadoRemoto`.
- Corregido el fondo negro: antes de aplicar, `WallpaperService` copia la imagen a la carpeta local del usuario, aplica con `SystemParametersInfo` y usa `IDesktopWallpaper` como fallback. Como SYSTEM se otorga `Users:(OI)(CI)RX` sobre `%ProgramData%\ApagadoRemoto`.
- Probado localmente con imagenes BMP usando `--apply-wallpaper`: la operacion responde exito, `HKCU\Control Panel\Desktop\Wallpaper` apunta a `%LOCALAPPDATA%\ApagadoRemoto\Wallpapers\...` y el archivo existe.
- El fallo original no se pudo reproducir en esta maquina porque no hay instalacion ni tareas programadas del cliente.
- Instalador `1.1.3` regenerado y listo para reinstalar en el equipo afectado.
- Archivos clave del cambio: `WallpaperService.cs` (`PrepareLocalWallpaper`, `EnsureSharedReadAccess`, `ApplyWallpaper`), `Program.cs` (modos de aplicacion), `Apagado.Cliente.csproj` y `ApagadoRemotoCliente.iss` (version).

## Estado actual (11/11/2026)

- Renombrado el ensamblado/ejecutable del cliente a `Apagado Remoto.exe` (`AssemblyName=Apagado Remoto`); version subida a `1.2.1`. Sustituye al renombrado intermedio `APR.exe` de la 1.2.0.
- Actualizados `installer/ApagadoRemotoCliente.iss` (iconos, `UninstallDisplayIcon`, `taskkill` de `Apagado Remoto.exe`, `APR.exe` y del legado `Apagado.Cliente.exe`, `[InstallDelete]` de los ejecutables anteriores), `installer/addtask.ps1` y `installer/removetask.ps1`.
- `SessionLauncher` sigue funcionando sin cambios porque usa `Environment.ProcessPath` para relanzar el helper por sesion.

## Estado actual (12/11/2026)

- Agregado el comando `Notice` (`CommandType.Notice = 7`) para enviar avisos obligatorios a las sesiones activas. El servidor tiene una pestana "Avisos" (titulo opcional + mensaje multilinea).
- El aviso se muestra con `Services/NoticeService.cs` (`NoticeOverlayForm`): overlay a pantalla completa, topmost, mensaje desplazable y boton **Aceptar**. Bloquea `Alt+F4`, `Esc`, `Alt+Tab`, teclas Windows, `Ctrl+Esc` y `Ctrl+Shift+Esc`; no puede bloquear `Ctrl+Alt+Del`.
- El payload viaja al helper como JSON `{ Title, Message }` codificado en base64 (`--show-notice <base64>`), para conservar saltos de linea y evitar problemas de comillas.
- `CommandHeader.Title` es opcional (compatible). El cuerpo reutiliza `CommandHeader.Message`.
- Solo se muestran avisos en sesiones activas/desconectadas: si no hay ninguna, se responde error y no se guarda para el proximo inicio de sesion. No se reporta la aceptacion al servidor.
- Extraido el hook de teclado de `LockScreenService` a `Services/OverlayKeyboardGuard.cs`, reutilizado por bloqueo y aviso. Ahora el guard bloquea tambien `Esc` simple.
- Version del cliente subida a `1.3.0`.

## Pendientes

- Reinstalar `installer\ApagadoRemotoClienteSetup.exe` en el equipo con fondo negro y volver a probar `SetWallpaper`.
- Si el fondo sigue negro: confirmar que la tarea `ApagadoRemoto Cliente` corre como SYSTEM, revisar los errores de `SessionLauncher` y recopilar logs del cliente. No hay logging persistente implementado.
- Probar el aviso `Notice` en un equipo con sesion activa y como SYSTEM (sin sesion) para validar el overlay y el mensaje de error.

## Arquitectura

- La UI real esta en `src/Apagado.Servidor/Views/MainWindow.xaml` (ver `StartupUri` en `App.xaml`). No existe un `MainWindow` en la raiz del proyecto.
- El servidor usa code-behind en WPF, no MVVM. `ViewModels/MainViewModel.cs` esta vacio y sin uso.
- `Core/Protocol/FrameWriter.cs` y `FrameReader.cs` implementan el framing:
  - Cabecera: `Int32` little-endian con la longitud + JSON UTF-8 de `CommandHeader`.
  - Si `ImageLength > 0`, siguen los bytes crudos de la imagen.
  - Respuesta: `Int32` con la longitud + JSON UTF-8 de `CommandResponse`.
- `CommandType`: `Shutdown = 0`, `SetWallpaper = 1`, `Identify = 2`, `Reboot = 3`, `CancelShutdown = 4`, `Lock = 5`, `Unlock = 6`, `Notice = 7`.
- `CommandHeader.DelaySeconds` programa el apagado/reinicio; `CommandHeader.Message` contiene el aviso del bloqueo o el cuerpo del aviso; `CommandHeader.Title` es el titulo opcional de `Notice`.

## Reglas del protocolo

- Todos los comandos salvo `Identify` exigen que `CommandHeader.Token` coincida con el token del cliente.
- `Identify` responde **sin validar token** a proposito. Devuelve `MachineName` y `Port`; se usa para descubrir clientes.
- `CancelShutdown` usa `shutdown.exe /a`; responde con error si no habia apagado programado.
- `Lock`/`Unlock` requieren token. `Lock` guarda el aviso pendiente y muestra una pantalla completa en las sesiones activas; `Unlock` la cierra y elimina el pendiente.
- `Notice` requiere token. Muestra un overlay con boton **Aceptar** solo en sesiones activas/desconectadas; no persiste el aviso y no reporta la aceptacion. `Title` es opcional.
- Mantener compatibilidad: los enums se serializan como numeros y los DTOs nuevos deben ser opcionales.

## Descubrimiento de clientes

- `src/Apagado.Servidor/Services/DiscoveryScanner.cs` escanea por TCP el puerto indicado en `txtPort` (por defecto 5007).
- Escanea la `/24` de cada NIC activa con gateway IPv4 (si ninguna tiene gateway, usa todas) y agrega `127.0.0.1`.
- Usa 256 conexiones concurrentes y timeout de 250 ms por host. Deduplica por nombre de equipo + puerto, prefiriendo una IP no-loopback.
- Al pulsar "Buscar clientes", los registrados encontrados quedan como `Activo`; los que no aparecieron en el escaneo se consultan directamente con `Identify` (timeout 2 s, maximo 32 en paralelo) porque pueden usar otro puerto o subred. Los que no responden quedan como `Sin respuesta`.
- Solo se agregan endpoints nuevos (`IP:Puerto`) y siempre se guarda `targets.json` al finalizar.
- Si se agregan mas protocolos de descubrimiento, mantener el escaneo rapido (<1 s en LAN local).

## Persistencia

- Destinos: `%LOCALAPPDATA%\ApagadoRemoto\targets.json` como lista de `TargetDevice`.
- Se admite lectura del formato antiguo (`List<string>` con `IP:Puerto`) y de un `targets.json` junto al ejecutable o en el directorio de trabajo.
- Fondos del cliente: `%ProgramData%\ApagadoRemoto\Wallpapers\` (imagen compartida) y `%ProgramData%\ApagadoRemoto\wallpaper.json` (ultimo fondo y estilo).
- Si el cliente no corre como SYSTEM (por ejemplo, pruebas locales), guarda fondos y `wallpaper.json` en `%LOCALAPPDATA%\ApagadoRemoto\` y sigue leyendo el pendiente compartido de ProgramData si existe.
- Antes de aplicar el fondo, el cliente copia la imagen a `%LOCALAPPDATA%` del usuario y fuerza el refresco con `SystemParametersInfo`; evita fondos negros cuando la imagen compartida no es legible desde la sesion. Como SYSTEM, otorga `Users:(OI)(CI)RX` sobre `%ProgramData%\ApagadoRemoto`.
- Bloqueo pendiente: `%ProgramData%\ApagadoRemoto\lock.json` (`{ "Message": "..." }`). Si existe, se muestra al iniciar sesion y se elimina al desbloquear.
- Variables del cliente: `APAGADO_TOKEN` (default `clave-compartida-aqui`) y `APAGADO_PORT` (default `5007`).

## Tareas programadas del cliente

- `ApagadoRemoto Cliente`: al arrancar el equipo, cuenta `SYSTEM`, `RunLevel Highest`; mantiene el listener TCP sin requerir sesion.
- `ApagadoRemoto Fondo Escritorio`: al iniciar sesion cualquier usuario (grupo Usuarios), `RunLevel Limited`; ejecuta `Apagado Remoto.exe --apply-pending-wallpaper`, aplica el fondo y, si existe `lock.json`, muestra el aviso de bloqueo. Sin limite de tiempo y con instancias paralelas.
- El listener SYSTEM no puede cambiar el escritorio del usuario: guarda el fondo y lanza un helper en cada sesion activa con `CreateProcessAsUser` (`Services/SessionLauncher.cs`). Los usuarios sin sesion reciben el fondo al iniciar sesion.
- El bloqueo usa el mismo mecanismo: `--show-lock "<mensaje>"` y `--hide-lock` se lanzan por sesion. El desbloqueo se senaliza con el evento local `Local\ApagadoRemoto.LockScreen`. La pantalla de bloqueo (`Services/LockScreenService.cs`) bloquea Alt+F4, Alt+Tab, teclas Windows, Ctrl+Esc y Ctrl+Shift+Esc, pero no puede bloquear Ctrl+Alt+Del.
- Los avisos usan el mismo mecanismo: `--show-notice <base64-json>` se lanza por sesion; `Services/NoticeService.cs` muestra el overlay y `Services/OverlayKeyboardGuard.cs` bloquea los atajos.

## Prueba manual del fondo

- Ejecutar `publish\Cliente\Apagado Remoto.exe --apply-wallpaper "<ruta imagen>" Fill`.
- Verificar que `HKCU:\Control Panel\Desktop\Wallpaper` apunte a una ruta bajo `%LOCALAPPDATA%\ApagadoRemoto\Wallpapers\` y que el archivo exista y sea legible.
- Para probar el flujo completo del listener, iniciar el cliente con `APAGADO_PORT=5099` (o el puerto configurado), enviar `SetWallpaper` y revisar la respuesta. Este flujo no reproduce exactamente el helper instalado como SYSTEM.
- Para probar un aviso: enviar `Notice` desde el servidor o ejecutar `publish\Cliente\Apagado Remoto.exe --show-notice <base64>`, donde el base64 es `[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('{"Title":"Prueba","Message":"Hola"}'))`. Verificar el overlay, el bloqueo de atajos y el cierre con **Aceptar**.
- Restaurar el fondo original del equipo al terminar las pruebas.

## Convenciones

- Mensajes de UI, logs y errores en espanol.
- Los DTOs del protocolo viven en `Apagado.Core/Dtos`; no agregar dependencias de Windows a `Apagado.Core`.
- Preferir editar los archivos existentes y mantener el estilo de code-behind ya usado en el servidor.
- Evitar duplicar logica de framing/respuestas: reutilizar `FrameReader`/`FrameWriter`.
- No commitear `bin/`, `obj/` ni archivos `*_wpftmp.csproj`.

## Firewall

Los clientes remotos deben permitir TCP 5007 entrante. Sin esa regla el servidor no puede enviar comandos ni descubrirlos.
