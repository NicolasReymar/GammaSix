# Fase 10 — Navegación e IA individual

## Objetivo

La Fase 10 separa la decisión estratégica de la ejecución individual. Un
controlador humano o Headless emite una intención mediante `MatchCommandBus` y
la entidad calcula una ruta, persigue su objetivo, rodea obstáculos y retoma la
orden persistente cuando termina un combate.

```text
Humano o Headless
→ MatchCommand
→ NavigationRuntimeSystem
→ NavigationPathfinder
→ EntityMovementService
```

El sistema no contiene estrategia económica, construcción ni un bot completo
de escaramuza.

## Rejilla y pathfinding

`NavigationGrid` se construye en runtime desde los límites del escenario. La
configuración es opcional:

```json
{
  "navigation": {
    "cellSize": 0.8,
    "allowDiagonal": true,
    "obstacleRefreshInterval": 0.25,
    "repathInterval": 0.2,
    "arrivalTolerance": 0.18,
    "attackMoveAcquisitionRange": 7.0,
    "individualAiInterval": 0.15
  }
}
```

`NavigationPathfinder` usa A* determinista. Evita cortar esquinas en diagonal,
busca una celda transitable cercana cuando el destino está ocupado y simplifica
los puntos intermedios cuando hay visibilidad sobre la rejilla.

## Obstáculos dinámicos

`DynamicObstacleService` rasteriza entidades sólidas inmóviles y edificios. La
firma de obstáculos se vuelve a calcular a intervalos, por lo que spawn,
despawn, muerte, reemplazo o traslado de un edificio incrementan la revisión de
la rejilla. Las rutas persistentes se recalculan al detectar una revisión nueva.

Las unidades móviles no bloquean permanentemente celdas. Se resuelven mediante
evitación local y repath cuando una entidad queda detenida.

## Órdenes persistentes

### Move

La entidad llega al destino evitando obstáculos. No abandona esta orden para
adquirir enemigos cercanos.

### Attack Move

Se activa con `A` y clic sobre terreno. Mientras avanza, una entidad agresiva
puede adquirir enemigos dentro del rango configurado, perseguirlos y atacar.
Al perder o resolver el objetivo, reanuda la ruta original.

`A` y clic sobre una entidad sigue siendo un ataque directo forzado.

### Patrol

Se activa con `R` y clic sobre terreno. La entidad recorre de forma indefinida:

```text
posición al emitir la orden ↔ destino elegido
```

Puede suspender el recorrido para combatir y después retomarlo.

### Stop y Passive

`S` elimina ruta, objetivo, seguimiento y trabajo actual. La postura `Passive`
evita adquisición automática y cancela la ruta según el comportamiento actual
del panel de órdenes.

## Persecución e interacciones

Combate, seguimiento y extracción ya no escriben una línea recta al objetivo.
Solicitan caminos temporales con propósitos distintos:

```text
Chase
Follow
ResourceInteraction
```

Los objetivos móviles provocan un repath limitado por intervalo. El combate
melee se detiene al entrar en rango y conserva el ciclo de windup/recovery.

## IA individual

`EntityAiRuntimeSystem` no elige objetivos estratégicos globales. Solo ayuda a
entidades agresivas que están ociosas, patrullando o en attack-move:

- consulta diplomacia desde la perspectiva de la entidad;
- busca un objetivo `Enemy` cercano;
- emite una orden de ataque no forzada;
- conserva la orden de navegación para retomarla después.

Los controladores Headless de la Fase 9 continúan decidiendo objetivos de mayor
alcance mediante el mismo Command Bus.

## Red

Se agregaron mensajes autoritativos:

```text
GammaSix.UnitAttackMoveCommand
GammaSix.UnitPatrolCommand
```

Los snapshots incluyen el tipo de orden, propósito del camino, índice y cantidad
de waypoints y último estado del pathfinding. Los clientes no calculan la ruta
como fuente de verdad: reciben posiciones y diagnóstico desde la autoridad.

## Diagnóstico

```text
/nav
/nav <runtime-id|filtro>
```

Muestra tamaño de celda, cantidad/revisión de obstáculos y el estado de
navegación de las entidades.

## Escenario de prueba

`Navegación e IA individual - Prueba` contiene dos equipos enemigos separados
por una barrera de edificios neutrales.

Prueba esperada:

1. Los Headless rodean la barrera para perseguir al equipo humano.
2. `A` y clic en terreno ejecuta attack-move.
3. `R` y clic en terreno inicia una patrulla.
4. `S` detiene la orden.
5. `/nav` muestra rutas y revisión de obstáculos.
6. Spawnear o retirar un edificio provoca una nueva revisión y repath.

## Alcance pendiente

Esta fase usa A* por entidad y evitación local básica. Sistemas de multitudes,
flow fields, formación avanzada, navegación por capas, puertas y costos de
terreno pueden añadirse después sin cambiar el contrato de órdenes. La
construcción de la Fase 11 reutilizará la actualización dinámica de obstáculos.
