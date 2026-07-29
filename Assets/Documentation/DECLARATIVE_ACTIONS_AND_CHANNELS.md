# Fase 7 revisada — Acciones declarativas, variables y canalizaciones

## Objetivo

GammaSix no contiene un sistema principal de captura o rescate. La Fase 7
incorpora operaciones generales que un escenario guardado puede combinar para
crear esas mecánicas u otras completamente diferentes.

El motor aporta:

- atributos y variables runtime de participantes;
- habilitación o bloqueo explícito de control;
- modificación de atributos de entidades;
- destrucción o despawn de entidades de un participante;
- spawn desde un ID fijo o desde una variable del participante;
- posiciones derivadas de eventos o entidades de área;
- canalizaciones generales;
- fotografías persistentes del contexto de eventos.

El escenario decide el significado de cada atributo, condición y secuencia.

## Muerte normal como origen de una regla

La muerte no se sustituye por captura. Una unidad muere normalmente y publica
`entity-died`. La regla puede consultar los atributos que tenía al morir incluso
si después se convierte en cadáver o se despawnea.

```text
unidad marcada por el mapa
→ muerte normal
→ entity-died
→ reglas guardadas del escenario
→ cambiar estado/control del participante
→ destruir entidades del propietario
→ generar un edificio prop
```

`RuntimeEventContext` conserva una fotografía con:

- ID runtime;
- definición;
- instancia de escenario;
- propietario, equipo y color;
- posición;
- vida;
- estado de vida;
- atributos.

Por eso `event-entity-owner`, `event-entity-position`,
`event-entity-definition` y las condiciones de atributos siguen funcionando
aunque la entidad original ya no esté disponible.

## Participantes

Cada participante posee:

```text
LifeState
ControlEnabled
Attributes
Variables
Resources
```

`ControlEnabled` es independiente del estado. La composición de ejemplo usa
`ControlEnabled = false` y un atributo propio, sin exigir un estado especial.
El motor no interpreta nombres como `kodo-tag.captured`.

Acciones disponibles:

```text
set-participant-state
set-participant-control-enabled
add-participant-attribute
remove-participant-attribute
set-participant-variable
```

Ejemplo:

```json
{
  "type": "set-participant-control-enabled",
  "participantSelector": "event-entity-owner",
  "controlEnabled": false
}
```

## Destrucción frente a despawn

```text
destroy-participant-entities
```

aplica muerte forzada a las entidades afectadas, publica `entity-died` y
ejecuta su propio `deathOutcome`: permanecer, despawnear o convertirse en
otra definición. Los interceptores de daño fatal no anulan esta acción; la
exclusión se controla mediante `preserveAttribute`.

```text
despawn-participant-entities
```

las retira directamente sin muerte.

La preservación se configura con un único atributo indicado por la regla:

```json
{
  "type": "destroy-participant-entities",
  "participantSelector": "event-entity-owner",
  "preserveAttribute": "capture-test.preserve-on-owner-destroy"
}
```

Si una entidad posee ese atributo, se conserva. Si no lo posee, se destruye.
El motor no conoce el significado del texto.

## Variables

Una regla puede recordar la definición de la entidad que murió:

```json
{
  "type": "set-participant-variable",
  "participantSelector": "event-entity-owner",
  "variableName": "capture-test.original-unit-definition",
  "valueSource": "event-entity-definition"
}
```

Después puede crear una nueva entidad desde esa variable:

```json
{
  "type": "spawn-entity",
  "participantSelector": "event-participant",
  "entityIdVariable": "capture-test.original-unit-definition",
  "areaAttribute": "trigger.rescue-exit"
}
```

No se revive la entidad muerta. Se crea una entidad nueva con otro ID runtime.

También existen variables generales de reglas mediante `set-rule-variable` y la
condición `rule-variable-is`.

## Canalizaciones

`RuntimeChannelSystem` solamente conoce:

- entidad que canaliza;
- entidad de área;
- participante objetivo opcional;
- duración;
- estado o atributo requerido opcional;
- continuidad espacial.

No sabe si la actividad representa rescate, construcción, reparación, apertura
de puertas o captura de un objetivo.

```json
{
  "type": "start-channel",
  "channelId": "capture-test.return-player",
  "duration": 3.0,
  "participantSelector": "event-area-owner",
  "requiredParticipantAttribute": "capture-test.captured"
}
```

La canalización se cancela si:

- la entidad fuente desaparece o deja de poder actuar;
- la entidad abandona el área;
- desaparece el área;
- cambia el estado o atributo requerido del participante objetivo.

Eventos:

```text
channel-started
channel-completed
channel-cancelled
```

## Escenario técnico

`scenario_capture_rescue_test` se mantiene como nombre de archivo por
compatibilidad, pero su comportamiento ya no utiliza acciones especializadas.
La prueba hace lo siguiente mediante datos:

1. una unidad con `capture-test.anchor` muere normalmente;
2. se guarda su definición en una variable del participante;
3. el mapa deshabilita su control y agrega `capture-test.captured`;
4. se destruyen sus entidades sin el atributo de preservación;
5. se crea `building.captured-player-prop` en el área de prisión;
6. un aliado canaliza dentro del área del edificio;
7. el mapa despawnea el prop, restaura control y crea una unidad nueva.

El edificio prop es una entidad normal del equipo. Posee:

```text
capture-test.captured-player-prop
capture-test.preserve-on-owner-destroy
```

Estos atributos pertenecen a los datos del escenario de prueba, no a una regla
interna de GammaSix.

## Acciones principales de la fase

```text
set-participant-state
set-participant-control-enabled
add-participant-attribute
remove-participant-attribute
set-participant-variable
set-rule-variable
add-entity-attribute
remove-entity-attribute
set-entity-health
set-entity-life-state
move-entity-to-area
destroy-participant-entities
despawn-participant-entities
spawn-entity
start-channel
cancel-channel
```

## Condiciones principales

```text
entity-has-attribute
entity-definition-is
participant-state-is
participant-control-is
participant-has-attribute
participant-lacks-attribute
participant-variable-is
rule-variable-is
channel-id-is
```

## Compatibilidad

Al iniciar, `GameContentRepository` reemplaza únicamente la copia persistente
del escenario técnico antiguo cuando detecta acciones obsoletas como
`capture-participant` o `rescue-participant`. No sobrescribe escenarios
personalizados.
