using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Construye el terreno visual y físico de un escenario usando celdas conceptuales
/// de 1x1 (o el tileSize definido en el catálogo). Las celdas se combinan por tipo
/// en una sola malla para evitar crear miles de GameObjects.
/// </summary>
public sealed class ScenarioTerrainController : MonoBehaviour
{
    private readonly List<GameObject> terrainObjects = new();

    public void Initialize(string scenarioId)
    {
        ClearTerrain();
        GameContentRepository.EnsureFolders();
        TerrainDefinitionRepository.EnsureDefinitions();

        ScenarioDefinition scenario = GameContentRepository.LoadScenario(scenarioId);
        if (scenario == null)
        {
            Debug.LogError($"[ScenarioTerrainController] No se pudo cargar el escenario '{scenarioId}'.");
            return;
        }

        string defaultTerrainId = scenario.terrain != null && !string.IsNullOrWhiteSpace(scenario.terrain.defaultTerrainId)
            ? scenario.terrain.defaultTerrainId
            : "praderas_primavera";

        int width = Mathf.Max(1, Mathf.RoundToInt(scenario.worldSize?.width ?? 1f));
        int height = Mathf.Max(1, Mathf.RoundToInt(scenario.worldSize?.height ?? 1f));

        Dictionary<Vector2Int, string> overrides = new();
        if (scenario.terrain?.tiles != null)
        {
            foreach (ScenarioTerrainTilePlacement tile in scenario.terrain.tiles)
            {
                if (tile == null || string.IsNullOrWhiteSpace(tile.terrainId))
                    continue;
                overrides[new Vector2Int(tile.x, tile.z)] = tile.terrainId;
            }
        }

        Dictionary<string, List<Vector2Int>> cellsByTerrain = new(StringComparer.OrdinalIgnoreCase);
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                string terrainId = overrides.TryGetValue(new Vector2Int(x, z), out string overrideId)
                    ? overrideId
                    : defaultTerrainId;
                if (!cellsByTerrain.TryGetValue(terrainId, out List<Vector2Int> cells))
                {
                    cells = new List<Vector2Int>();
                    cellsByTerrain.Add(terrainId, cells);
                }
                cells.Add(new Vector2Int(x, z));
            }
        }

        foreach (KeyValuePair<string, List<Vector2Int>> group in cellsByTerrain)
        {
            TerrainDefinition definition = TerrainDefinitionRepository.Load(group.Key);
            if (definition == null)
                continue;
            terrainObjects.Add(CreateTerrainMesh(definition, group.Value, width, height));
        }
    }

    private GameObject CreateTerrainMesh(TerrainDefinition definition, IReadOnlyList<Vector2Int> cells, int width, int height)
    {
        float size = definition.tileSize;
        float originX = -width * size * 0.5f;
        float originZ = -height * size * 0.5f;

        Vector3[] vertices = new Vector3[cells.Count * 4];
        Vector2[] uvs = new Vector2[cells.Count * 4];
        int[] triangles = new int[cells.Count * 6];

        for (int index = 0; index < cells.Count; index++)
        {
            Vector2Int cell = cells[index];
            float x = originX + cell.x * size;
            float z = originZ + cell.y * size;
            int vertex = index * 4;
            int triangle = index * 6;

            vertices[vertex] = new Vector3(x, 0f, z);
            vertices[vertex + 1] = new Vector3(x, 0f, z + size);
            vertices[vertex + 2] = new Vector3(x + size, 0f, z + size);
            vertices[vertex + 3] = new Vector3(x + size, 0f, z);

            uvs[vertex] = Vector2.zero;
            uvs[vertex + 1] = Vector2.up;
            uvs[vertex + 2] = Vector2.one;
            uvs[vertex + 3] = Vector2.right;

            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 1;
            triangles[triangle + 2] = vertex + 2;
            triangles[triangle + 3] = vertex;
            triangles[triangle + 4] = vertex + 2;
            triangles[triangle + 5] = vertex + 3;
        }

        Mesh mesh = new()
        {
            name = $"Terrain Mesh - {definition.id}",
            vertices = vertices,
            uv = uvs,
            triangles = triangles
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject terrainObject = new($"Terrain - {definition.name}");
        terrainObject.transform.SetParent(transform, false);
        MeshFilter filter = terrainObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = terrainObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = CreateMaterial(definition);
        MeshCollider collider = terrainObject.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        terrainObject.AddComponent<TerrainTileGroupView>().Initialize(definition);
        return terrainObject;
    }

    private static Material CreateMaterial(TerrainDefinition definition)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new(shader) { name = $"Terrain Material - {definition.id}" };
        if (ColorUtility.TryParseHtmlString(definition.color, out Color color))
            material.color = color;
        return material;
    }

    private void ClearTerrain()
    {
        foreach (GameObject terrainObject in terrainObjects)
        {
            if (terrainObject != null)
                Destroy(terrainObject);
        }
        terrainObjects.Clear();
    }
}

public sealed class TerrainTileGroupView : MonoBehaviour
{
    public string TerrainId { get; private set; }
    public string Category { get; private set; }
    public string SubCategory { get; private set; }
    public bool Walkable { get; private set; }
    public float MovementCost { get; private set; }

    public void Initialize(TerrainDefinition definition)
    {
        TerrainId = definition.id;
        Category = definition.category;
        SubCategory = definition.subCategory;
        Walkable = definition.walkable;
        MovementCost = definition.movementCost;
    }
}
