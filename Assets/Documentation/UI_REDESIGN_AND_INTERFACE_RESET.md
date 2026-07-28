# Rediseño unificado de interfaz

Esta versión aplica un mismo lenguaje visual al menú principal, un jugador, multijugador, lobby, ajustes, menú de partida, HUD y Creador de Entidades.

## Principios

- Paleta oscura verde con acento esmeralda.
- Encabezados, tarjetas, botones, campos y estados compartidos.
- Resolución de referencia unificada en `1920 x 1024` con coincidencia equilibrada entre ancho y alto.
- No se modificaron reglas de partida, selección, movimiento, red, escenarios ni gameplay.

## Ajustes de interfaz

La categoría **Interfaz** está disponible:

- Desde el menú principal: `Ajustes > Interfaz`.
- Dentro de una partida: `ESC > Ajustes > Interfaz`.

Dentro de una partida se puede:

- Guardar la posición actual de los paneles del HUD.
- Restablecer inmediatamente los paneles a su distribución predeterminada.

Fuera de una partida, **Restablecer interfaz** elimina la distribución persistida para que la siguiente partida cargue las posiciones predeterminadas.

El archivo persistente continúa ubicado en:

`Application.persistentDataPath/Settings/hud-layout.json`

## Creador de Entidades

- El panel de biblioteca conserva su modo retráctil.
- Los vectores de escala, tamaño visual y colisión usan filas compactas X/Y/Z.
- El panel central evita desplazamiento horizontal.
- La vista previa derecha es desplazable y el JSON queda contenido dentro de su panel.
- Se eliminó el selector USS no compatible `:first-child` que generaba advertencias en Unity.
