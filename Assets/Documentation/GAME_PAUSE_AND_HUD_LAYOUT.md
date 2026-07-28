# Menú de partida y persistencia del HUD

## Menú de partida

`Escape` alterna el menú principal de partida. Desde Ajustes, `Escape` vuelve al menú anterior.

Opciones actuales:

- Volver a la partida.
- Ajustes.
- Salir de la partida.
- Espacio reservado para opciones futuras.

El menú no modifica `Time.timeScale`: en multijugador la simulación continúa y solamente se bloquea el input local.

## Categorías de ajustes

- Video: sin opciones por el momento.
- Sonido: sin opciones por el momento.
- Interfaz: guardado de posiciones del HUD.

El botón de interfaz se encuentra en su propio archivo `GameInterfaceSettings.uxml`.

En la cabecera de Ajustes existen dos caminos explícitos:

- `Volver`: regresa al menú de partida sin escribir cambios pendientes.
- `Guardar y volver`: ejecuta todos los escritores registrados en `GameSettingsPersistenceService`, fuerza la escritura en disco y después regresa al menú de partida. Si algún escritor falla, permanece en Ajustes y muestra un error.

## Guardado del HUD

Los paneles movibles se registran en `HudLayoutPersistenceService` mediante `DraggableHudPanel`.

- Al iniciar una partida, cada panel carga sus coordenadas desde `PlayerPrefs`.
- Arrastrar un panel solamente modifica su posición actual.
- `Guardar coords UI` escribe inmediatamente todas las posiciones registradas.
- `Guardar y volver` también incluye las coordenadas del HUD dentro del guardado general.
- Los paneles mantienen al menos la mitad de su superficie visible.

Claves actuales:

- `GammaSix.Hud.Gold`
- `GammaSix.Hud.SelectedEntity`
- `GammaSix.Hud.SelectedEntitiesExtended`

## Bloqueo modal

`GameUiModalService` evita que los clics del menú atraviesen hacia el mapa. Bloquea:

- selección;
- órdenes con clic derecho;
- grupos de control;
- movimiento directo;
- movimiento y rotación de cámara.

Al cerrar el menú se mantiene un frame de protección para consumir el clic que cerró o activó una opción.
