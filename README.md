# Apagado Remoto

Aplicación para administrar equipos de una red local (LAN) desde un único PC: apagar, reiniciar, cancelar apagados programados, mostrar un bloqueo de pantalla con mensaje, enviar avisos obligatorios y cambiar el fondo de escritorio de forma remota.

El sistema se compone de dos aplicaciones:

- **Servidor** (WPF, interfaz en español): envía los comandos y descubre clientes en la red.
- **Cliente** (consola sin ventana): corre en cada equipo administrado, escucha comandos por TCP y los ejecuta.

---

## Índice

- [Características](#características)
- [Requisitos](#requisitos)
- [Estructura del proyecto](#estructura-del-proyecto)
- [Compilación y ejecución](#compilación-y-ejecución)
- [Uso del servidor](#uso-del-servidor)
- [Uso del cliente](#uso-del-cliente)
- [Protocolo de comunicación](#protocolo-de-comunicación)
- [Descubrimiento de clientes](#descubrimiento-de-clientes)
- [Persistencia de datos](#persistencia-de-datos)
- [Avisos obligatorios](#avisos-obligatorios)
- [Actualización remota](#actualización-remota)
- [Tareas programadas del cliente](#tareas-programadas-del-cliente)
- [Instalador](#instalador)
- [Firewall](#firewall)
- [Versionado y publicación](#versionado-y-publicación)
- [Solución de problemas](#solución-de-problemas)
- [Limitaciones conocidas](#limitaciones-conocidas)

---

## Características

| Función | Descripción |
|---|---|
| Apagar | Apaga los equipos seleccionados, con retraso configurable en segundos. |
| Reiniciar | Reinicia los equipos seleccionados, con retraso configurable. |
| Cancelar apagado | Cancela un apagado/reinicio programado (`shutdown.exe /a`). |
| Bloquear | Muestra una pantalla completa con un mensaje en las sesiones activas. |
| Desbloquear | Cierra la pantalla de bloqueo y elimina el aviso pendiente. |
| Enviar aviso | Muestra un aviso a pantalla completa que el usuario debe aceptar para continuar. |
| Cambiar fondo | Envía una imagen y la aplica con el estilo seleccionado. |
| Actualizar cliente | Envía el instalador desde el servidor y lo aplica de forma silenciosa. |
| Descubrimiento | Escanea la red local para encontrar clientes activos. |
| Selección múltiple | Todas las acciones se aplican a uno o varios equipos. |

---

## Requisitos

- **.NET 8 SDK** para compilar.
- **Windows** para ejecutar el cliente y el servidor (`net8.0-windows`).
- El cliente publicado es **self-contained** (`win-x64`), por lo que el equipo destino no necesita .NET instalado.
- **Inno Setup 7** solo si se desea regenerar el instalador.
- Permiso de administrador para instalar el cliente (crea tareas programadas y una regla de firewall).

---

## Estructura del proyecto

```
ApagadoRemoto/
├─ ApagadoRemoto.slnx              # Solución (formato .slnx)
├─ src/
│  ├─ Apagado.Core/                # net8.0 — DTOs, enums y protocolo TCP
│  │  ├─ Dtos/
│  │  │  ├─ CommandHeader.cs
│  │  │  └─ CommandResponse.cs
│  │  ├─ Enums/
│  │  │  ├─ CommandType.cs
│  │  │  └─ WallpaperStyle.cs
│  │  └─ Protocol/
│  │     ├─ FrameReader.cs
│  │     └─ FrameWriter.cs
│  ├─ Apagado.Cliente/             # net8.0-windows — escucha y ejecuta comandos
│  │  ├─ Program.cs                # Punto de entrada y modos de ejecución
│  │  └─ Services/
│  │     ├─ TcpListenerService.cs  # Listener TCP y despacho de comandos
│  │     ├─ ShutdownService.cs     # shutdown.exe
│  │     ├─ WallpaperService.cs    # Aplicación y persistencia de fondos
│  │     ├─ LockScreenService.cs   # Pantalla de bloqueo
│  │     ├─ NoticeService.cs       # Avisos obligatorios con botón Aceptar
│  │     ├─ OverlayKeyboardGuard.cs# Bloqueo de atajos en pantallas completas
│  │     ├─ SessionLauncher.cs     # Lanza procesos en sesiones de usuario
│  │     └─ UpdateService.cs       # Descarga, valida y aplica actualizaciones
│  └─ Apagado.Servidor/            # net8.0-windows — UI WPF
│     ├─ App.xaml(.cs)
│     ├─ Views/MainWindow.xaml(.cs)
│     ├─ Models/TargetDevice.cs
│     ├─ Services/
│     │  ├─ NetworkSender.cs       # Envío de comandos por TCP
│     │  └─ DiscoveryScanner.cs    # Escaneo de la red
│     └─ ViewModels/MainViewModel.cs
├─ installer/                      # Inno Setup y scripts de tareas/firewall
│  ├─ ApagadoRemotoCliente.iss
│  ├─ addtask.ps1
│  ├─ installupdate.ps1          # Instalación silenciosa enviada por Update
│  ├─ removetask.ps1
│  ├─ addfirewall.ps1
│  └─ removefirewall.ps1
└─ publish/Cliente/                # Salida usada por el instalador
```

Las dependencias entre proyectos son:

```
Apagado.Servidor ──► Apagado.Core
Apagado.Cliente  ──► Apagado.Core
```

`Apagado.Core` no debe depender de APIs de Windows: es el contrato compartido.

---

## Compilación y ejecución

```powershell
# Compilar toda la solución (verificación)
dotnet build ApagadoRemoto.slnx -c Release

# Ejecutar el servidor
dotnet run --project src/Apagado.Servidor -c Release

# Ejecutar el cliente
dotnet run --project src/Apagado.Cliente -c Release

# Publicar el cliente self-contained (salida para el instalador)
dotnet publish src/Apagado.Cliente/Apagado.Cliente.csproj -c Release -r win-x64 --self-contained true -o publish\Cliente

# Regenerar el instalador (Inno Setup 7.1.1)
& "C:\Program Files\Inno Setup 7\ISCC.exe" /Q installer\ApagadoRemotoCliente.iss
```

No hay proyecto de tests ni linter configurado. Verificar siempre con `dotnet build` sin errores ni advertencias.

---

## Uso del servidor

1. Añadir destinos con el botón **Añadir** (formato `IP:Puerto`) o con **Buscar clientes**.
2. Seleccionar uno o varios clientes en la lista (`Ctrl`/`Shift`).
3. Usar las pestañas:
   - **Encendido**: retraso en segundos, apagar, reiniciar, cancelar.
   - **Bloqueo**: mensaje y botones bloquear/desbloquear.
   - **Avisos**: título, mensaje y botón enviar aviso.
   - **Fondo de escritorio**: seleccionar imagen, estilo y aplicar.
   - **Actualización**: seleccionar el instalador del cliente, verificar versiones y actualizar.
   - **Conexión**: token compartido y puerto por defecto.
4. Revisar el resultado en **Historial / Log** y en las columnas **Versión** y **Último estado**.

El token y el puerto deben coincidir con la configuración del cliente. Por defecto: `clave-compartida-aqui` / `5007`.

---

## Uso del cliente

Sin argumentos, inicia el listener TCP:

```powershell
dotnet run --project src/Apagado.Cliente -c Release
```

Modos de aplicación disponibles:

| Argumento | Función |
|---|---|
| _(ninguno)_ | Inicia el listener TCP en el puerto configurado. |
| `--apply-pending-wallpaper` | Aplica el último fondo pendiente y, si existe, muestra el aviso de bloqueo pendiente. |
| `--apply-wallpaper "<ruta>" <estilo>` | Aplica una imagen concreta con el estilo indicado. |
| `--show-lock "<mensaje>"` | Muestra la pantalla de bloqueo con el mensaje. |
| `--show-notice <base64-json>` | Muestra un aviso obligatorio; el payload es JSON `{ "Title", "Message" }` en base64. |
| `--hide-lock` | Cierra la pantalla de bloqueo (señaliza el evento local). |

Ejemplo de prueba manual del fondo:

```powershell
publish\Cliente\Apagado Remoto.exe --apply-wallpaper "C:\ruta\imagen.jpg" Fill
```

### Variables de entorno del cliente

| Variable | Valor por defecto | Descripción |
|---|---|---|
| `APAGADO_TOKEN` | `clave-compartida-aqui` | Token que deben incluir los comandos (excepto `Identify`). |
| `APAGADO_PORT` | `5007` | Puerto TCP donde escucha el cliente. |

> En producción, defina un token propio y manténgalo igual en el servidor y en todos los clientes instalados.

---

## Protocolo de comunicación

El protocolo es binario con cabeceras JSON sobre TCP.

### Framing

**Petición** (cliente TCP → cliente de escritorio):

```
[Int32 little-endian: longitud cabecera]  (4 bytes)
[JSON UTF-8 de CommandHeader]             (N bytes)
[bytes crudos de la imagen]               (solo si ImageLength > 0)
```

**Respuesta** (cliente → servidor):

```
[Int32 little-endian: longitud]  (4 bytes)
[JSON UTF-8 de CommandResponse]  (N bytes)
```

La lógica de lectura/escritura está centralizada en `FrameReader` y `FrameWriter`; no debe duplicarse.

### `CommandHeader` (petición)

```jsonc
{
  "Type": 0,                    // CommandType
  "Token": "clave-compartida-aqui",
  "RequestId": "guid",
  "FileName": "imagen.jpg",     // opcional (SetWallpaper)
  "ContentType": "image/jpeg",  // opcional
  "ImageLength": 123456,        // tamaño de los bytes de imagen que siguen
  "Style": 4,                   // WallpaperStyle
  "DelaySeconds": 60,           // apagado/reinicio
  "Message": "texto",           // aviso de bloqueo o cuerpo del aviso
  "Title": "texto",             // opcional (Notice)
  "PackageVersion": "1.4.0",    // opcional (Update)
  "PackageSha256": "HEX",       // opcional (Update)
  "AllowDowngrade": false       // opcional (Update)
}
```

### `CommandResponse` (respuesta)

```jsonc
{
  "RequestId": "guid",
  "Success": true,
  "Message": "Fondo de escritorio aplicado correctamente.",
  "MachineName": "PC-AULA-01",  // solo en Identify
  "Port": 5007,                 // solo en Identify
  "Version": "1.4.0"            // opcional
}
```

### Tipos de comando (`CommandType`)

| Valor | Nombre | Requiere token | Descripción |
|---:|---|---|---|
| `0` | `Shutdown` | Sí | Apaga con retraso (`DelaySeconds`). |
| `1` | `SetWallpaper` | Sí | Recibe imagen y estilo y cambia el fondo. |
| `2` | `Identify` | **No** | Devuelve `MachineName` y `Port` para descubrimiento. |
| `3` | `Reboot` | Sí | Reinicia con retraso (`DelaySeconds`). |
| `4` | `CancelShutdown` | Sí | Cancela un apagado/reinicio programado. |
| `5` | `Lock` | Sí | Muestra pantalla de bloqueo con `Message`. |
| `6` | `Unlock` | Sí | Cierra la pantalla de bloqueo. |
| `7` | `Notice` | Sí | Muestra un aviso obligatorio con `Title` y `Message`; requiere pulsar Aceptar. |
| `8` | `Update` | Sí | Recibe el instalador del cliente, valida `PackageSha256` y lo instala en silencio. |

### Estilos de fondo (`WallpaperStyle`)

| Valor | Nombre |
|---:|---|
| `0` | Center |
| `1` | Tile |
| `2` | Stretch |
| `3` | Fit |
| `4` | Fill |
| `5` | Span |

### Reglas del protocolo

- Todos los comandos **excepto `Identify`** exigen que `Token` coincida con el token del cliente; si no, se responde `Token inválido`.
- `Identify` responde **sin validar token** a propósito.
- `DelaySeconds` se limita a `0..604800` (7 días) en el cliente.
- `CancelShutdown` usa `shutdown.exe /a` y responde con error si no había apagado programado.
- `Notice` solo se muestra en sesiones activas: si no hay ninguna, responde error y no se guarda para el próximo inicio de sesión.
- `Update` valida token, extensión `.exe`, tamaño máximo (256 MB), SHA-256 y que `PackageVersion` sea mayor que la versión instalada, salvo `AllowDowngrade`.
- Mantener compatibilidad: los enums se serializan como números y los DTOs nuevos deben ser opcionales.

---

## Descubrimiento de clientes

`DiscoveryScanner` (servidor) localiza clientes activos:

1. Escanea la subred `/24` de cada NIC activa con gateway IPv4. Si ninguna tiene gateway, usa todas las NIC utilizables.
2. Agrega siempre `127.0.0.1`.
3. Abre conexiones TCP concurrentes (256 en paralelo) con timeout de 250 ms por host y envía `Identify`.
4. Deduplica por `nombre de equipo + puerto`, prefiriendo una IP no-loopback.
5. Los destinos ya registrados que no aparecen en el escaneo se sondean directamente con `Identify` (timeout 2 s, máximo 32 en paralelo), porque pueden usar otro puerto o subred.
6. Solo se agregan endpoints (`IP:Puerto`) nuevos y se guarda `targets.json` al finalizar.

---

## Persistencia de datos

### Servidor

- Destinos: `%LOCALAPPDATA%\ApagadoRemoto\targets.json` (lista de `TargetDevice`; incluye nombre, IP, puerto, último estado y fecha).
- Compatibilidad: se admite el formato antiguo (`List<string>` con `IP:Puerto`) y un `targets.json` junto al ejecutable o en el directorio de trabajo.

### Cliente

- Como **SYSTEM** (instalación normal):
  - Imágenes: `%ProgramData%\ApagadoRemoto\Wallpapers\`
  - Último fondo: `%ProgramData%\ApagadoRemoto\wallpaper.json`
  - Bloqueo pendiente: `%ProgramData%\ApagadoRemoto\lock.json`
- Sin ser SYSTEM (pruebas locales):
  - Imágenes y `wallpaper.json` en `%LOCALAPPDATA%\ApagadoRemoto\`
  - Sigue leyendo el bloqueo pendiente compartido de `%ProgramData%` si existe.

### Fondo de escritorio: por qué se copia la imagen

Como SYSTEM el cliente no puede cambiar el escritorio de una sesión de usuario directamente. El flujo es:

1. SYSTEM guarda la imagen en `%ProgramData%\ApagadoRemoto\Wallpapers\` y concede `Users:(OI)(CI)RX`.
2. Guarda la solicitud en `wallpaper.json`.
3. Lanza un helper en cada sesión activa con `CreateProcessAsUser` (`--apply-wallpaper`).
4. El helper copia la imagen a `%LOCALAPPDATA%\ApagadoRemoto\Wallpapers\` y aplica con `SystemParametersInfo`; si falla, usa `IDesktopWallpaper` como respaldo.
5. Los usuarios sin sesión reciben el fondo al iniciar sesión.

Se conservan los últimos 10 fondos y se eliminan los más antiguos.

---

## Avisos obligatorios

El comando `Notice` envía un aviso con título y mensaje a los equipos seleccionados:

1. El cliente **no guarda** el aviso: solo se muestra en las sesiones activas o desconectadas.
2. Como SYSTEM, lanza un helper `--show-notice <base64-json>` en cada sesión con `SessionLauncher`.
3. El helper muestra `NoticeOverlayForm`: overlay a pantalla completa, siempre encima, con título, mensaje desplazable y botón **Aceptar**.
4. Mientras está visible bloquea `Alt+F4`, `Esc`, `Alt+Tab`, teclas Windows, `Ctrl+Esc` y `Ctrl+Shift+Esc`; **no puede bloquear `Ctrl+Alt+Del`**.
5. Al pulsar **Aceptar** (o `Enter`) el aviso se cierra. La aceptación **no se reporta** al servidor.
6. Si no hay sesiones activas, el servidor recibe `Success = false` y el aviso no se guarda para el próximo inicio de sesión.
7. Si se envían varios avisos a la misma sesión, se muestran en secuencia mediante un mutex local.

---

## Actualización remota

El servidor puede distribuir el instalador del cliente a los equipos seleccionados:

1. En la pestaña **Actualización**, seleccionar `ApagadoRemotoClienteSetup.exe`. El servidor lee la versión del archivo y calcula su SHA-256.
2. Opcionalmente pulsar **Verificar versiones** para consultar `Identify` y ver la versión instalada en cada equipo.
3. Pulsar **Actualizar clientes**. El instalador se envía como payload binario con un timeout de 10 minutos por equipo.
4. El cliente valida token, extensión, tamaño máximo (256 MB), hash y versión; guarda el paquete en `%ProgramData%\ApagadoRemoto\Updates\` (`%LOCALAPPDATA%\ApagadoRemoto\Updates\` en pruebas) y responde al servidor.
5. Como SYSTEM, lanza `installupdate.ps1`: espera 5 s para que la respuesta llegue al servidor, ejecuta el setup con `/VERYSILENT /NORESTART /SUPPRESSMSGBOXES` y reinicia la tarea `ApagadoRemoto Cliente`. El resultado final se escribe en `update-result.json`.

Requisitos y limitaciones:

- La primera versión con soporte `Update` es la **1.4.0**; debe instalarse manualmente una vez porque los clientes 1.3.0 no conocen el comando.
- El paquete se valida con SHA-256, pero no se comprueba una firma digital; el token compartido sigue siendo la única autenticación del canal.
- El envío es secuencial por equipo y el instalador cierra temporalmente los overlays de bloqueo o avisos activos.

## Tareas programadas del cliente

El instalador crea dos tareas:

| Tarea | Disparador | Cuenta / nivel | Acción |
|---|---|---|---|
| `ApagadoRemoto Cliente` | Al arrancar el equipo | `SYSTEM`, `RunLevel Highest` | Inicia el listener TCP sin requerir sesión. |
| `ApagadoRemoto Fondo Escritorio` | Al iniciar sesión cualquier usuario | Grupo Usuarios, `RunLevel Limited` | Ejecuta `--apply-pending-wallpaper`; sin límite de tiempo e instancias paralelas. |

- El listener SYSTEM no puede cambiar el escritorio; usa `SessionLauncher` para lanzar helpers en las sesiones activas (`WTSQueryUserToken` + `CreateProcessAsUser`).
- `addtask.ps1` inicia el listener al registrar o actualizar las tareas; `installupdate.ps1` lo reinicia al terminar una actualización remota.
- El bloqueo usa el mismo mecanismo: `--show-lock "<mensaje>"` y `--hide-lock` por sesión.
- Los avisos obligatorios usan el mismo mecanismo: `--show-notice <base64-json>` por sesión.
- El desbloqueo se señaliza con el evento local `Local\ApagadoRemoto.LockScreen`.
- La pantalla de bloqueo bloquea `Alt+F4`, `Alt+Tab`, teclas Windows, `Ctrl+Esc` y `Ctrl+Shift+Esc`; **no puede bloquear `Ctrl+Alt+Del`**.

---

## Instalador

Archivo: `installer/ApagadoRemotoCliente.iss` (Inno Setup).

- Instala en `{commonpf}\ApagadoRemoto\Cliente`.
- Copia `publish\Cliente\*` y los scripts de `installer\`.
- Copia `installupdate.ps1`, usado por las actualizaciones enviadas desde el servidor.
- Ejecuta `addfirewall.ps1` y `addtask.ps1` al instalar.
- Al desinstalar ejecuta `removetask.ps1` y `removefirewall.ps1`.
- En actualizaciones detiene tareas y procesos (`Apagado Remoto.exe`, `APR.exe`, `Apagado.Cliente.exe`) antes de reemplazar archivos.
- Salida: `installer\ApagadoRemotoClienteSetup.exe`.
- Log de instalación de tareas: `%ProgramData%\ApagadoRemoto\task-install.log`.

---

## Firewall

Los clientes deben permitir **TCP 5007** entrante (o el puerto configurado). Sin esa regla el servidor no puede enviar comandos ni descubrir el equipo.

El instalador agrega la regla `ApagadoRemoto - Cliente TCP 5007` para los perfiles Domain, Private y Public.

---

## Versionado y publicación

- Versión actual del cliente: **1.4.0**
  - `Version` / `InformationalVersion`: `1.4.0`
  - `FileVersion` / `AssemblyVersion`: `1.4.0.0`
  - En `src/Apagado.Cliente/Apagado.Cliente.csproj`
- `AppVersion=1.4.0` en `installer/ApagadoRemotoCliente.iss`.
- El ejecutable se llama **`Apagado Remoto.exe`** (`AssemblyName=Apagado Remoto`).
- Al cambiar código del cliente:
  1. Subir la versión en el `.csproj` y en el `.iss`.
  2. Publicar en `publish\Cliente`.
  3. Recompilar el instalador.

---

## Solución de problemas

| Síntoma | Qué revisar |
|---|---|
| El servidor no encuentra clientes | Firewall TCP 5007, que el cliente esté corriendo y que estén en la misma subred. |
| `Token inválido` | `APAGADO_TOKEN` del cliente debe coincidir con el token del servidor. |
| El fondo queda negro | Confirmar que la tarea `ApagadoRemoto Cliente` corre como SYSTEM y revisar los errores de `SessionLauncher`; no hay logging persistente. |
| `Access path is denied` | El cliente debe escribir en `%ProgramData%\ApagadoRemoto` solo como SYSTEM; en pruebas locales usa `%LOCALAPPDATA%`. |
| El bloqueo no aparece | Debe haber una sesión activa o el aviso se mostrará al iniciar sesión (revisar `lock.json`). |
| El aviso obligatorio no aparece | Debe haber una sesión activa; el aviso no se guarda para el próximo inicio de sesión. Revisar el log del servidor. |
| La actualización remota falla | El cliente debe ser 1.4.0 o superior, el token debe coincidir y el SHA-256 del paquete debe ser correcto. Revisar `%ProgramData%\ApagadoRemoto\Updates\install.log` y `update-result.json`. |
| `Ctrl+Alt+Del` cierra el bloqueo o el aviso | Limitación de Windows: una aplicación no puede bloquear esa combinación. |

---

## Limitaciones conocidas

- No hay logging persistente en el cliente (solo salida por consola).
- El bloqueo y los avisos son disuasorios: no pueden impedir `Ctrl+Alt+Del`.
- Los avisos obligatorios solo se muestran en sesiones activas o desconectadas; no se guardan para usuarios sin sesión.
- El descubrimiento asume redes `/24`; en subredes mayores se recorta a la `/24` de la IP local.
- No hay autenticación más allá del token compartido ni cifrado del canal TCP. Usar en redes de confianza.
- No hay tests automatizados.
- El paquete de actualización se envía en un único frame: consume memoria en el servidor y en el cliente y se transmite secuencialmente equipo por equipo.
- SHA-256 detecta corrupción, pero no sustituye una firma digital del instalador.
