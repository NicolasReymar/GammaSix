# Participantes headless

## Alcance de esta actualización

Esta etapa incorpora la base de matchmaking para participantes controlados por el servidor:

- Cada entrada del lobby posee `ParticipantId`, `SlotId` y `SlotIndex` estables.
- Un participante puede ser `Human` o `Headless`.
- Los headless aparecen en la misma lista que los jugadores.
- Los headless no necesitan marcarse como listos.
- El host puede abrir el catálogo **Headless**, agregar perfiles compatibles y quitar los opcionales.
- Agregar o quitar un headless reinicia el estado listo de todos los humanos.
- Equipo, color y eliminación pueden bloquearse desde el escenario.
- El escenario puede declarar participantes headless obligatorios.
- El modo normal registra `base:headless.commander.normal` desde el juego base.
- El roster completo se sincroniza con los clientes.

## Estado del controlador de gameplay

Esta etapa no implementa todavía el planificador RTS del comandante normal ni el comandante declarativo de Kodo Tag. Los participantes headless ya pueden ocupar casillas, recibir equipo/color y ser propietarios diferenciados de entidades mediante un identificador sintético temporal.

La siguiente etapa debe extraer la simulación autoritativa y crear un Command Bus común para humanos y headless. Después se conectarán:

1. el comandante normal incluido en GammaSix;
2. los comandantes declarativos definidos por paquetes de escenarios;
3. las reglas de captura, rescate y oleadas.

## Configuración de escenario

```json
{
  "gameModeId": "base:game-mode.normal",
  "participantConfiguration": {
    "maximumHumanPlayers": 8,
    "maximumParticipants": 8,
    "availableHeadlessProfiles": [
      "base:headless.commander.normal"
    ],
    "requiredParticipants": []
  }
}
```

Un participante obligatorio definido por un escenario puede utilizar:

```json
{
  "slotId": "kodo.commander",
  "slotIndex": 8,
  "displayName": "Comandante Kodo",
  "controllerProfileId": "minigame.kodo-tag:headless.kodo-commander",
  "teamId": 2,
  "colorId": 1,
  "participantLocked": true,
  "teamLocked": true,
  "colorLocked": true
}
```

El perfil debe registrarse en `headlessProfiles` dentro del escenario o, posteriormente, dentro del manifiesto del paquete importado.

## Actualización de la fase 2

La propiedad de entidades ya utiliza `OwnerParticipantId` y dejó de depender de
los identificadores sintéticos como mecanismo de autorización. Los headless aún
no toman decisiones por sí solos, pero `AuthoritativeMatchRuntime` expone
`EnqueueHeadlessCommand`, por lo que el siguiente controlador podrá emitir las
mismas órdenes que un humano sin simular clics ni utilizar la UI.

La simulación también fue retirada de `NetworkEntityCoordinator`; este quedó
responsable de input, selección, vistas, mensajes y snapshots.

## Integración con paquetes de contenido

Desde la fase de paquetes, los perfiles declarados por un escenario instalado se
resuelven con namespace, por ejemplo:

```text
minigame.kodo-tag:headless.kodo-commander
```

El lobby sincroniza el packageId, la versión y el hash antes de permitir el inicio.
La ejecución del controlador Headless declarativo sigue pendiente de la fase de
controladores, pero su identidad ya queda aislada dentro del paquete.

## Runtime de controladores (Fase 9)

Los perfiles pueden declarar `runtimeControllerId` y `controllerSettings`. La
autoridad crea un controlador por participante Headless y todas sus decisiones
pasan por `MatchCommandBus`. Consulta `HEADLESS_RUNTIME_CONTROLLERS.md`.
