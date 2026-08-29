# SonyBraviaControl

Mando de escritorio WPF para controlar una Sony Bravia desde Windows sin depender de una consola.

El primer MVP usa ADB por red para navegación, volumen, canales, reproducción, lanzamiento de apps y escritura desde el teclado del PC. Para encender una TV que ya no responde por ADB incluye Wake-on-LAN mediante la MAC configurada.

## Estado actual

- WPF sobre .NET 8.
- MVVM sin lógica de control en el code-behind de la ventana.
- Detección automática de `adb.exe` en PATH y en descargas habituales de Android Platform Tools.
- Conexión ADB configurable (`IP:puerto`).
- Cruceta, OK, Home, Atrás, Menú y selector de fuente.
- Volumen, mute y canales.
- Controles multimedia.
- Accesos directos a Projectivy, Netflix, YouTube, Prime Video, Disney+ y Movistar+.
- Entrada de texto desde el PC.
- Wake-on-LAN y `KEYCODE_WAKEUP` para el encendido.
- Persistencia de IP, puerto, MAC y ruta de ADB en `%LocalAppData%\SonyBraviaControl\settings.json`.

## Arranque

1. Abrir `SonyBraviaControl.sln` en Visual Studio.
2. Tener activada la depuración ADB en la Bravia.
3. Haber autorizado previamente el PC con `adb connect IP_DE_LA_TV:5555`.
4. Ejecutar el proyecto.
5. Revisar IP y puerto en **Conexión y ajustes**. Para Wake-on-LAN, añadir también la MAC de la TV.

La configuración inicial usa `192.168.1.2:5555`, pero puede cambiarse desde la propia interfaz.

## Encendido

Si ADB sigue disponible durante el reposo, **Encender** envía `KEYCODE_WAKEUP`. Si ADB ha desaparecido, la aplicación envía un Magic Packet Wake-on-LAN y reintenta la conexión durante unos segundos.

Para que Wake-on-LAN funcione, la Bravia y el router deben permitir el arranque/control por red durante el reposo.

## Siguiente fase

- Control IP nativo de Sony (IRCC-IP / JSON-RPC) para reducir la dependencia de ADB.
- Detección automática de la TV en la red.
- Botones HDMI directos cuando se confirme el identificador de entrada del modelo.
- Estado de encendido/entrada/volumen más fiable.
- Icono y pulido visual final.
- Empaquetado MSIX o instalador autocontenido.

## Referencia técnica

Los keycodes ADB utilizados corresponden al mismo enfoque del proyecto público `supermarsx/sony-bravia-adb-scripts`, que sirvió como referencia para comprobar los comandos del mando. Esta aplicación no ejecuta sus scripts; implementa el control desde C#.
