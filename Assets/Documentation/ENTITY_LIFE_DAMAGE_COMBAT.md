# Fase 6 — Vida, daño, estado y combate dinámico

## Objetivo

Esta fase incorpora un ciclo autoritativo de vida y combate sin codificar unidades
concretas. Las estadísticas provienen de `EntityDefinition`, las órdenes pasan por
el `MatchCommandBus` y el daño se confirma únicamente en el servidor.

La fase no implementa proyectiles, armadura, habilidades ni animaciones. Sí deja
separado el **ciclo de ataque** de la **forma de entrega del impacto**, para que una
entrega `projectile` pueda añadirse posteriormente sin reemplazar la orden de
ataque, los estados ni los tiempos de preparación y recuperación.

## Estado de una entidad

El estado se divide deliberadamente en tres capas. Una sola enumeración no podría
representar, por ejemplo, que una unidad se está moviendo mientras continúa en
combate o que está realizando una actividad mientras recibe daño.

```text
EntityLifeRuntimeState
├── Alive
├── Downed
├── Captured
└── Dead

EntityStatusRuntimeState.Activity
├── Idle
├── Moving
├── Performing
├── Attacking
├── Recovering
└── Dead

Indicadores simultáneos
├── InCombat
└── IsUnderAttack
```

`ActivityDetail` conserva un identificador extensible de la actividad concreta:

```text
idle
moving
approaching-attack-target
resource-extraction
attack-windup
attack-recovery
dead
```

Los sistemas reales siguen siendo la fuente de verdad. `EntityStatusRuntimeSystem`
deriva el estado visible desde movimiento, extracción, ataque y vida; no obliga a
que distintos sistemas escriban manualmente una única variable incompatible.

## Despawn y muerte son conceptos distintos

Un **despawn** retira una entidad del mundo por una regla, agotamiento de recurso,
reemplazo, cambio de escenario u otro sistema. No implica que la entidad haya
muerto y solo publica el flujo de `entity-despawned`.

Una **muerte** ocurre cuando una resolución fatal confirma `Death`. Primero cambia
el estado a `Dead` y publica `entity-died`. Después se aplica un resultado de muerte
configurable; ese resultado puede mantener, retirar o sustituir la entidad.

```json
"life": {
  "deathOutcome": "replace",
  "deathOutcomeDelay": 0.75,
  "deathReplacementEntityId": "unit.humanoid.corpse",
  "deathReplacementInheritsOwner": true
}
```

Resultados disponibles:

```text
remain
└── La misma entidad runtime permanece muerta en el mundo.

despawn
└── La entidad muerta se retira tras el retraso con razón DeathCleanup.

replace
└── La entidad muerta se sustituye atómicamente por otra entidad registrada.
```

`replace` pasa por `MatchEntityCatalog` y `EntityLifecycleService`. La entidad
resultante debe existir y estar registrada como dependencia de la definición que
muere. No permite crear contenido instalado arbitrario.

La sustitución conserva por defecto participante, equipo, color y posición. Con
`deathReplacementInheritsOwner: false`, el resultado se crea como neutral.

Las definiciones antiguas con `removeOnDeath` y `deathRemovalDelay` siguen siendo
compatibles:

```text
removeOnDeath = true  → despawn
removeOnDeath = false → remain
```

Los soldados y trabajadores base se convierten actualmente en
`unit.humanoid.corpse`, una entidad real del juego base. El cadáver usa una
primitiva provisional y puede recibir otra skin posteriormente sin cambiar la
lógica de muerte.

Al iniciar, el repositorio migra únicamente las definiciones base persistentes que
todavía conservan los valores legados sin modificar. Las entidades personalizadas
o las configuraciones de muerte editadas por el usuario no se sobrescriben.

El estado `Downed` no ejecuta un resultado de muerte. `Captured` permanece en
los enums por compatibilidad de datos anteriores, pero ya no es una resolución
fatal del motor. Las mecánicas que representen a un jugador fuera de combate
deben comenzar desde `entity-died` y componerse mediante reglas del escenario.

## Definición de ataque

```json
"attributes": [
  "melee"
],
"attack": {
  "delivery": "melee",
  "damageType": "physical",
  "baseDamage": 12,
  "baseAttackSpeed": 1.0,
  "attackTime": 0.35,
  "recoveryTime": 0.65,
  "attackRange": 0.55,
  "chaseTarget": true
}
```

### Campos

- `delivery`: forma de entregar el impacto. En esta fase solo `melee` está activa.
- `damageType`: categoría lógica del daño; no se confunde con la entrega. Se usa
  en eventos y reglas (`physical`, `magic`, etc.).
- `baseDamage`: daño aplicado en cada impacto válido.
- `baseAttackSpeed`: multiplicador base del ciclo. `1.0` utiliza los tiempos
  declarados; `2.0` ejecuta preparación y recuperación al doble de velocidad.
- `attackTime`: tiempo de preparación o *windup* antes del impacto.
- `recoveryTime`: tiempo posterior al impacto antes de poder comenzar otro.
- `attackRange`: distancia adicional a los radios físicos de atacante y objetivo.
- `chaseTarget`: permite acercarse automáticamente cuando el objetivo está fuera
  de alcance.

La velocidad efectiva es:

```text
velocidad efectiva = baseAttackSpeed × AttackSpeedMultiplier runtime

tiempo efectivo de preparación = attackTime / velocidad efectiva
tiempo efectivo de recuperación = recoveryTime / velocidad efectiva
```

`AttackSpeedMultiplier` queda en el estado runtime para mejoras, ralentizaciones,
auras y habilidades futuras sin modificar la definición original.

## Etiqueta melee

Una entrega `melee` requiere el atributo exacto:

```text
melee
```

Las definiciones base lo declaran explícitamente. El cargador también lo deriva
cuando encuentra `attack.delivery = melee`, evitando que una definición válida
quede inutilizable por olvidar duplicar el atributo. Las reglas de combate
comprueban el atributo antes de aceptar la orden.

La distancia de impacto se calcula con:

```text
radio del atacante + radio del objetivo + attackRange
```

Así las entidades grandes no tienen que superponer sus centros para golpearse.

## Ciclo autoritativo de ataque

```text
Orden Attack
   ↓
Validación de propietario, vida, atributos, equipo y ataque
   ↓
Approaching (cuando está fuera de alcance)
   ↓
Windup / attackTime
   ↓
Impacto melee → DamageRuntimeService
   ↓
Recovery / recoveryTime
   ↓
Revalidar objetivo y repetir
```

`Recovery` mantiene bloqueado el inicio de otro ataque, pero no inmoviliza a la
entidad. Durante ese tiempo puede recibir una orden de movimiento o aproximarse
a un nuevo objetivo; el temporizador continúa y el siguiente `Windup` solo puede
comenzar cuando la recuperación haya terminado.

El objetivo se vuelve a validar en cada ciclo. El ataque termina cuando:

- el atacante u objetivo deja de estar `Alive`;
- el objetivo desaparece;
- pasan a ser aliados;
- se emite una orden de movimiento, seguimiento o extracción;
- el delivery no posee un resolvedor disponible.

## Preparación para proyectiles

`delivery` está separado del ciclo. Actualmente el impacto registrado es
`melee`; cualquier otro valor se rechaza claramente durante importación y runtime.
Una futura entrega `projectile` podrá reemplazar únicamente la resolución del
impacto por la creación de una entidad proyectil. Se conservarán:

- el comando `Attack`;
- la persecución y validación de objetivo;
- `Windup` y `Recovery`;
- la velocidad de ataque;
- `DamageRuntimeService` al producirse la colisión;
- los eventos de daño y muerte.

No se creó un proyectil provisional ni una entidad exclusiva de depuración.

## Daño y muerte interceptable

Todo daño pasa por `DamageRuntimeService`:

```text
Aplicar daño
  ↓
entity-damaged
  ↓
¿La vida quedó en cero?
  ├── No → continuar
  └── Sí → entity-fatal-damage sincrónico
              ↓
           reglas pueden cambiar la resolución
              ↓
           Death / Prevented / Downed
```

Resoluciones disponibles:

- `Death`: vida cero, estado `Dead`, evento `entity-died` y resultado posterior según `life.deathOutcome`.
- `Prevented`: restaura vida y conserva `Alive`.
- `Downed`: detiene la entidad sin eliminarla.

La resolución fatal se evalúa sincrónicamente para que una regla pueda reemplazar
la muerte antes de que otros sistemas la confirmen.

## Eventos y reglas nuevos

Eventos:

```text
entity-damaged
entity-fatal-damage
entity-died
```

Condiciones:

```text
source-entity-has-attribute
entity-life-state-is
entity-health-at-or-below
damage-type-is
```

Acciones fatales:

```text
prevent-death
restore-health
set-fatal-resolution
```

Ejemplo de reacción posterior a una muerte:

```json
{
  "id": "remember-dead-unit",
  "eventType": "entity-died",
  "conditions": [
    {
      "type": "entity-has-attribute",
      "attribute": "mode.anchor"
    }
  ],
  "actions": [
    {
      "type": "set-participant-variable",
      "participantSelector": "event-entity-owner",
      "variableName": "mode.original-unit",
      "valueSource": "event-entity-definition"
    }
  ]
}
```

La muerte se confirma antes de ejecutar esta regla. El escenario puede usar el
contexto persistente para crear otras entidades o alterar el estado del
participante. Véase `DECLARATIVE_ACTIONS_AND_CHANNELS.md`.

## Prueba de despawn frente a muerte

Con dos soldados runtime visibles:

```text
/despawn <id>
```

retira directamente la entidad y no crea cadáver, porque no hubo una muerte.

```text
/damage <id> 999
```

confirma una muerte, publica `entity-died` y, para los humanoides base, sustituye
la unidad por `unit.humanoid.corpse` después de 0,75 segundos.

`/combat` muestra `muerte=Replace->unit.humanoid.corpse` durante el intervalo
anterior a la sustitución.

## Órdenes y red

El clic derecho sobre una entidad enemiga resuelve `ContextualEntityAction.Attack`.
Cada entidad seleccionada decide independientemente si posee ataque. La orden se
transporta como `EntityAttackCommand` y pasa por el mismo `MatchCommandBus` usado
por humanos, Headless y reglas.

Los snapshots incluyen:

- vida y estado de vida;
- actividad e indicadores de combate;
- estadísticas base de ataque;
- delivery y tipo de daño;
- objetivo y fase del ataque.

## Comandos de diagnóstico

```text
/combat [filtro]
/attack <runtime-id-atacante> <runtime-id-objetivo>
/damage <runtime-id-objetivo> <cantidad> [runtime-id-origen]
```

`/attack` utiliza el Command Bus. `/damage` utiliza directamente el servicio
autoritativo de daño. Ambos modifican el runtime únicamente desde host o partida
local.

Prueba recomendada:

```text
/runtime
/attack 1 2
/combat
/damage 2 20 1
```

También puede probarse de manera normal seleccionando una unidad propia y haciendo
clic derecho sobre una entidad enemiga.

## Archivos principales

```text
Scripts/Game/Runtime/Combat/EntityCombatRuntimeState.cs
Scripts/Game/Runtime/Combat/EntityCombatRules.cs
Scripts/Game/Runtime/Combat/CombatRuntimeSystem.cs
Scripts/Game/Runtime/Combat/DamageRuntimeService.cs
Scripts/Game/Runtime/Combat/DeathRuntimeService.cs
Scripts/Game/Runtime/Combat/EntityStatusRuntimeSystem.cs
Scripts/Game/Entities/Definitions/EntityAttackDefinition.cs
Scripts/Game/Entities/Definitions/EntityLifeDefinition.cs
```


## Integración con acciones declarativas

La Fase 7 no sustituye la muerte por un estado especializado. `entity-died`
publica una fotografía persistente y las reglas del escenario pueden combinar
control, atributos, variables, destrucción, spawn y canalizaciones. Véase
`DECLARATIVE_ACTIONS_AND_CHANNELS.md`.
