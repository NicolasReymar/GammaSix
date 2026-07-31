# HUD de órdenes de unidad

Módulo UI Toolkit separado para órdenes autoritativas de la selección local.

## Archivos

- `UnitOrdersHud.uxml`
- `UnitOrdersHud.uss`
- `UnitOrdersHudController.cs`
- `EntityUnitOrderRuntimeService.cs`

El panel reutiliza `DraggableHudPanel` y `GameHudShared.uss`, por lo que conserva posición, arrastre y borde amarillo durante la edición del HUD.

## Órdenes disponibles

### Atacar [A]

Activa un modo de objetivo. El siguiente clic izquierdo sobre una entidad envía una orden `Attack` con `ForceTarget = true` para las unidades seleccionadas que posean ataque.

La orden explícita puede apuntar a entidades neutrales o aliadas. No puede atacar a la propia entidad, áreas, entidades sin vida o entidades que bloqueen la interacción.

El clic contextual derecho continúa atacando solamente enemigos.

El ataque hacia un punto del terreno no se implementa todavía, porque el ataque-movimiento y la patrulla dependen del sistema de navegación.

### Detener [S]

Cancela movimiento, seguimiento, trabajo y objetivo de ataque. Si la entidad se encuentra en recuperación, el temporizador no se reinicia ni se elimina.

### Pasivo/Agresivo [P]

Cambia la postura autoritativa de combate. Pasivo cancela el objetivo actual y queda preparado para que la futura adquisición automática de objetivos no actúe sobre esa entidad. Las órdenes manuales explícitas siguen permitidas.

### Patrulla

El botón está visible como parte del contrato del panel, pero permanece deshabilitado hasta la fase de navegación e IA individual.
