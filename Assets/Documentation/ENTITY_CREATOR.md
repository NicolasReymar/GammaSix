# Creador de entidades

El creador se abre desde **Menú principal > Creador de entidades**.

## Alcance actual

- Lista y filtra las definiciones existentes.
- Crea, duplica, edita y elimina entidades.
- Maneja las categorías `unit`, `building` y `environment`.
- Clasifica unidades como humanoide, bestia, máquina, no muerto, elemental o tipo personalizado.
- Edita vida, velocidad, solidez, visual, prefab, escala, tamaño visual y colisión.
- Administra atributos conocidos y personalizados.
- Configura los bloques especializados `resource` y `worker`.
- Muestra una vista previa del JSON antes de guardar.

## Ubicación de guardado

Las definiciones se guardan en:

`Application.persistentDataPath/GameContent/Entities`

Es la misma ruta utilizada por `EntityDefinitionRepository` al cargar las entidades del juego. Los JSON de `StreamingAssets` se copian allí la primera vez que se inicializa el repositorio. Mientras se ejecuta dentro del Editor de Unity, guardar o eliminar también sincroniza el JSON correspondiente en `StreamingAssets/GameContent/Entities`, para que quede dentro del proyecto y pueda versionarse.

## Consideraciones

- El ID solo admite letras, números, puntos, guiones y guiones bajos.
- Cambiar el ID de una entidad renombra su archivo persistente.
- El botón **Probar entidad** está reservado para una futura escena sandbox.
- En una build instalada, los cambios se conservan solamente en `persistentDataPath`; dentro del Editor también quedan sincronizados con `StreamingAssets`.

## Panel lateral de entidades

El listado lateral puede contraerse con el botón `‹` y restaurarse con `›`. Al estar abierto usa un ancho mayor y muestra el buscador y el filtro de categoría en filas completas. El formulario central ocupa automáticamente el espacio liberado al contraerlo.
