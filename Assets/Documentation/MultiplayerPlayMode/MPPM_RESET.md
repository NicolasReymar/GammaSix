# Reset de Multiplayer Play Mode en Unity 6.3

La configuración `Settings/PlayMode/cliplayer.asset` fue retirada de esta copia porque Multiplayer Play Mode 2.0.x puede lanzar el error:

`You may not pass in objects that are already persistent`

El archivo anterior se conserva solo como texto en:

`Documentation/MultiplayerPlayMode/cliplayer.asset.backup.txt`

## Pasos locales

1. Cierra completamente Unity y cualquier Virtual Player.
2. Elimina `Library/VP/` dentro de la carpeta raíz del proyecto si existe.
3. Si el error continúa, elimina la carpeta `Library/` completa. Unity la reconstruirá al abrir el proyecto.
4. Abre Unity.
5. Crea nuevamente la configuración desde el menú de Multiplayer Play Mode, evitando reutilizar el escenario persistente anterior.

Esto no modifica el networking ni la jugabilidad. Solo reinicia la configuración del editor usada para lanzar instancias adicionales.
