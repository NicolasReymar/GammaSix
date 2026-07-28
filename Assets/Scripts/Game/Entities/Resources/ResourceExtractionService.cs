using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Procesa órdenes autoritativas de extracción. El trabajador guarda su objetivo,
/// se desplaza usando el sistema normal de movimiento y comienza el temporizador
/// al entrar en distancia de interacción.
/// </summary>
public static class ResourceExtractionService
{
    public static bool TryAssignExtraction(
        IDictionary<int, EntityRuntimeState> entities,
        ulong senderClientId,
        ResourceInteractionCommand command,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (command == null ||
            !entities.TryGetValue(command.WorkerUnitId, out EntityRuntimeState worker) ||
            !entities.TryGetValue(command.ResourceUnitId, out EntityRuntimeState resource))
        {
            rejectionReason = "Trabajador o recurso inexistente.";
            return false;
        }

        if (worker.OwnerClientId != senderClientId)
        {
            rejectionReason = $"Cliente {senderClientId} intentó usar un trabajador ajeno ({worker.UnitId}).";
            return false;
        }

        if (worker.Worker == null || worker.Attributes == null || !worker.Attributes.Has(EntityAttributeIds.Worker))
        {
            rejectionReason = $"La entidad {worker.UnitId} no es una entidad trabajadora.";
            return false;
        }

        if (resource.Resource == null || resource.Attributes == null || !resource.Attributes.Has(EntityAttributeIds.Resource))
        {
            rejectionReason = $"La entidad {resource.UnitId} no es un recurso.";
            return false;
        }

        if (EntityInteractionRules.BlocksContextualInteraction(resource.Attributes))
        {
            rejectionReason = $"La entidad {resource.UnitId} no admite interacciones contextuales.";
            return false;
        }

        if (!CanExtract(worker.Worker, resource.Resource, out rejectionReason))
            return false;

        worker.InteractionTargetUnitId = -1;
        worker.Worker.TargetResourceUnitId = resource.UnitId;
        worker.Worker.ExtractionTimer = 0f;
        worker.Worker.IsExtracting = false;
        worker.Destination = resource.Position;
        return true;
    }

    public static void Update(IDictionary<int, EntityRuntimeState> entities, float deltaTime)
    {
        List<int> spentResources = new();

        foreach (EntityRuntimeState worker in entities.Values.ToList())
        {
            WorkerRuntimeState workerState = worker.Worker;
            if (workerState == null || workerState.TargetResourceUnitId < 0)
                continue;

            if (!entities.TryGetValue(workerState.TargetResourceUnitId, out EntityRuntimeState resource) ||
                resource.Resource == null)
            {
                ClearJob(workerState);
                worker.Destination = worker.Position;
                continue;
            }

            if (EntityInteractionRules.BlocksContextualInteraction(resource.Attributes) ||
                !CanExtract(workerState, resource.Resource, out _))
            {
                ClearJob(workerState);
                worker.Destination = worker.Position;
                continue;
            }

            float interactionDistance = CalculateInteractionDistance(worker, resource);
            Vector2 offset = new(worker.Position.x - resource.Position.x, worker.Position.z - resource.Position.z);
            if (offset.sqrMagnitude > interactionDistance * interactionDistance)
            {
                workerState.IsExtracting = false;
                workerState.ExtractionTimer = 0f;
                worker.Destination = resource.Position;
                continue;
            }

            worker.Destination = worker.Position;
            workerState.IsExtracting = true;
            workerState.ExtractionTimer += deltaTime;
            if (workerState.ExtractionTimer < workerState.ExtractionTime)
                continue;

            workerState.ExtractionTimer -= workerState.ExtractionTime;
            if (!TryExtractCycle(workerState, resource.Resource, out string extractedResource, out int extractedAmount))
            {
                spentResources.Add(resource.UnitId);
                ClearJob(workerState);
                continue;
            }

            if (string.Equals(workerState.CarriedResourceName, extractedResource, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(workerState.CarriedResourceName))
            {
                workerState.CarriedResourceName = extractedResource;
                workerState.CarriedResourceAmount += extractedAmount;
            }
            else
            {
                // No existe inventario todavía. Se conserva el último tipo extraído
                // y su cantidad para que el sistema futuro pueda migrar este estado.
                workerState.CarriedResourceName = extractedResource;
                workerState.CarriedResourceAmount = extractedAmount;
            }

            Debug.Log($"[ResourceExtractionService] Entidad {worker.UnitId} extrajo " +
                      $"{extractedAmount} de '{extractedResource}'. Transporta {workerState.CarriedResourceAmount}.");

            if (resource.Resource.IsSpent)
            {
                spentResources.Add(resource.UnitId);
                ClearJob(workerState);
            }
            else if (!workerState.RepeatExtraction)
            {
                ClearJob(workerState);
            }
        }

        foreach (int resourceId in spentResources.Distinct())
            ResolveSpentResource(entities, resourceId);
    }

    private static bool CanExtract(
        WorkerRuntimeState worker,
        ResourceRuntimeState resource,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (worker.WorkerTier < resource.ResourceTier)
        {
            rejectionReason = $"El trabajador es tier {worker.WorkerTier} y el recurso requiere tier {resource.ResourceTier}.";
            return false;
        }

        if (resource.ExtractionTools == null || resource.ExtractionTools.Length == 0)
            return true;

        bool hasRequiredTool = worker.Tools != null && worker.Tools.Any(workerTool =>
            resource.ExtractionTools.Any(required =>
                string.Equals(workerTool, required, StringComparison.OrdinalIgnoreCase)));
        if (!hasRequiredTool)
        {
            rejectionReason = "El trabajador no posee una herramienta compatible con el recurso.";
            return false;
        }

        return true;
    }

    private static float CalculateInteractionDistance(EntityRuntimeState worker, EntityRuntimeState resource)
    {
        float workerRadius = Mathf.Max(worker.BoundsSize.x, worker.BoundsSize.z) * 0.5f;
        float resourceRadius = Mathf.Max(resource.BoundsSize.x, resource.BoundsSize.z) * 0.5f;
        return workerRadius + resourceRadius +
               Mathf.Max(worker.Worker?.InteractionRange ?? 0f, resource.Resource?.InteractionRange ?? 0f);
    }

    private static bool TryExtractCycle(
        WorkerRuntimeState worker,
        ResourceRuntimeState resource,
        out string resourceId,
        out int extractedAmount)
    {
        resourceId = null;
        extractedAmount = 0;
        ResourceAmountRuntimeState stack = null;

        if (!string.IsNullOrWhiteSpace(worker.ResourceName))
        {
            stack = resource.Resources.FirstOrDefault(item =>
                string.Equals(item.ResourceId, worker.ResourceName, StringComparison.OrdinalIgnoreCase) &&
                (resource.Infinite || item.Amount > 0));
        }

        stack ??= resource.Resources.FirstOrDefault(item => resource.Infinite || item.Amount > 0);
        if (stack == null)
            return false;

        extractedAmount = resource.Infinite
            ? resource.AmountPerExtraction
            : Mathf.Min(resource.AmountPerExtraction, stack.Amount);
        if (extractedAmount <= 0)
            return false;

        resourceId = stack.ResourceId;
        if (!resource.Infinite)
            stack.Amount -= extractedAmount;
        return true;
    }

    private static void ResolveSpentResource(IDictionary<int, EntityRuntimeState> entities, int resourceId)
    {
        if (!entities.TryGetValue(resourceId, out EntityRuntimeState resource) || resource.Resource == null)
            return;

        string replacementId = resource.Resource.OnResourcesSpentEntityId;
        foreach (EntityRuntimeState worker in entities.Values)
        {
            if (worker.Worker != null && worker.Worker.TargetResourceUnitId == resourceId)
                ClearJob(worker.Worker);
        }

        if (string.IsNullOrWhiteSpace(replacementId))
        {
            entities.Remove(resourceId);
            Debug.Log($"[ResourceExtractionService] Recurso {resourceId} agotado y eliminado.");
            return;
        }

        EntityDefinition replacement = EntityDefinitionRepository.Load(replacementId);
        if (replacement == null)
        {
            entities.Remove(resourceId);
            Debug.LogWarning($"[ResourceExtractionService] No existe el reemplazo '{replacementId}'. Se eliminó el recurso.");
            return;
        }

        EntityRuntimeFactory.Reconfigure(resource, replacement);
        Debug.Log($"[ResourceExtractionService] Recurso {resourceId} reemplazado por '{replacementId}'.");
    }

    private static void ClearJob(WorkerRuntimeState worker)
    {
        worker.TargetResourceUnitId = -1;
        worker.ExtractionTimer = 0f;
        worker.IsExtracting = false;
    }
}
