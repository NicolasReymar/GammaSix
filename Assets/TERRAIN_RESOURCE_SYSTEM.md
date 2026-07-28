# GammaSix — terreno, recursos y trabajadores

## Terrenos

Los terrenos se registran en:

```text
StreamingAssets/GameContent/Terrains
```

La definición inicial es `praderas_primavera.json`. Cada terreno declara:

- `id`
- `name`
- `category`
- `subCategory` opcional
- `tileSize`
- `color`
- `walkable`
- `movementCost`
- `attributes`

El escenario selecciona el terreno mediante:

```json
"terrain": {
  "defaultTerrainId": "praderas_primavera",
  "tiles": []
}
```

Cada celda es conceptualmente un cuadrado de 1×1. Para rendimiento, las celdas del
mismo tipo se combinan en una malla, en vez de crear un GameObject por celda.
`tiles` queda preparado para reemplazar celdas concretas por otro terreno.

## Entidad recurso

El atributo de recurso es:

```text
entity.resource
```

La configuración vive en la definición de la entidad, no en el escenario:

```json
"resource": {
  "infinite": false,
  "onResourcesSpentEntityId": null,
  "resources": [
    { "resourceId": "wood", "amount": 100 }
  ],
  "resourceTier": 1,
  "extractionTools": [],
  "interactionRange": 0.75,
  "amountPerExtraction": 5
}
```

- `infinite`: no reduce las cantidades al extraer.
- `onResourcesSpentEntityId`: reemplaza la entidad agotada por otra definición.
  Si es `null` o vacío, la entidad se elimina.
- `resources`: tipos y cantidades almacenadas.
- `resourceTier`: tier mínimo del trabajador.
- `extractionTools`: herramientas permitidas. Si está vacío, se ignora.
- `interactionRange`: margen de interacción adicional a los radios físicos.
- `amountPerExtraction`: cantidad obtenida por ciclo.

El árbol inicial está registrado como `environment.tree.spring` y carga el modelo:

```text
Resources/EntityVisuals/TreeOne.fbx
```

## Entidad trabajadora

El atributo de trabajador es:

```text
unit.worker
```

Su configuración:

```json
"worker": {
  "extractionTime": 2.0,
  "repeatExtraction": true,
  "resourceName": "wood",
  "workerTier": 1,
  "tools": [],
  "interactionRange": 0.75
}
```

El trabajador de prueba se registra como `unit.humanoid.worker`.

## Orden de extracción

1. Seleccionar uno o más trabajadores propios.
2. Hacer clic derecho sobre una entidad con `entity.resource`.
3. El servidor valida propietario, tier y herramientas.
4. El trabajador guarda `TargetResourceUnitId` y usa `Destination` para acercarse.
5. Al entrar en distancia, se detiene y comienza `ExtractionTimer`.
6. Al completar `extractionTime`, extrae `amountPerExtraction`.
7. Si `repeatExtraction` es `true`, reinicia el ciclo. Si es `false`, termina.
8. Una orden de movimiento normal cancela la tarea de extracción.

Mientras no exista inventario, el estado runtime del trabajador conserva:

- `CarriedResourceName`
- `CarriedResourceAmount`

El snapshot replica el recurso restante y el estado del trabajador para que la UI
pueda mostrarlo.

## Escenario de prueba

`scenario_3v3_resources` incluye:

- terreno `praderas_primavera`;
- tres entidades por equipo;
- un trabajador del equipo 1;
- dos árboles neutrales con madera;
- edificio de mercenarios y aura neutral;
- 500 de oro para cada equipo.

## Normalización de modelos importados

Las entidades con `visual: "prefab"` se instancian dentro de una raíz de gameplay independiente.
El modelo se ajusta automáticamente a `visualSize` usando los bounds de sus renderers, por lo que
los FBX pueden venir exportados en metros, centímetros u otras unidades sin quedar invisibles.

```json
"visualSize": { "x": 2.4, "y": 4.0, "z": 2.4 },
"collisionSize": { "x": 1.25, "y": 3.2, "z": 1.25 }
```

- `visualSize`: tamaño máximo visible en unidades del mundo.
- `collisionSize`: volumen autoritativo y collider de la raíz.
- Si una definición antigua no incluye `visualSize`, se utiliza `collisionSize` como tamaño objetivo.
- La base visual se alinea automáticamente con el suelo de la entidad.
