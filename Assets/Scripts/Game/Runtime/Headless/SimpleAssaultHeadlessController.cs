using System;

/// <summary>
/// Controlador técnico mínimo de la Fase 9. Asigna a cada unidad agresiva el
/// objetivo hostil más cercano y emite Attack por el Command Bus. No construye,
/// no administra economía y no sustituye al comandante normal de escaramuza.
/// </summary>
public sealed class SimpleAssaultHeadlessController : IHeadlessController
{
    public string ControllerId => HeadlessControllerRegistry.SimpleAssaultControllerId;

    public void Initialize(HeadlessControllerInitializationContext context)
    {
        if (context?.State != null)
            context.State.LastDecision = "Controlador de asalto simple inicializado.";
    }

    public void Tick(HeadlessControllerUpdateContext context)
    {
        if (context == null || context.Perception == null)
            return;

        int maximumOrders = Math.Max(1, context.Settings?.maxOrdersPerUpdate ?? 4);
        int issuedThisTick = 0;
        int evaluated = 0;

        foreach (EntityRuntimeState source in context.Perception.ControlledEntities)
        {
            evaluated++;
            if (issuedThisTick >= maximumOrders)
                break;
            if (source?.Attack == null || source.Attack.Stance == EntityCombatStance.Passive)
                continue;

            if (source.Attack.TargetEntityId > 0 &&
                context.Perception.TryGetEntity(source.Attack.TargetEntityId, out EntityRuntimeState currentTarget) &&
                context.Perception.IsValidHostileTarget(source, currentTarget))
            {
                continue;
            }

            EntityRuntimeState target = context.Perception.FindNearestHostile(source);
            if (target == null)
                continue;

            long sequence = context.EnqueueCommand(
                MatchCommandType.Attack,
                new EntityAttackCommand
                {
                    SourceUnitId = source.UnitId,
                    TargetUnitId = target.UnitId,
                    ForceTarget = false
                });

            if (sequence <= 0)
                continue;

            issuedThisTick++;
            context.State.LastSourceEntityId = source.UnitId;
            context.State.LastTargetEntityId = target.UnitId;
            context.State.LastDecision = $"Ataque encolado: {source.UnitId} → {target.UnitId}.";
        }

        context.State.DecisionsEvaluated += evaluated;
        if (issuedThisTick == 0 && evaluated > 0)
            context.State.LastDecision = "Sin nuevas órdenes: objetivos vigentes o ningún hostil disponible.";
        else if (evaluated == 0)
            context.State.LastDecision = "No hay entidades propias aptas para combatir.";
    }
}
