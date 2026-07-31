using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Canal común de texto de partida. Transporta chat y solicitudes de comandos,
/// pero toda mutación de gameplay se resuelve únicamente en la autoridad.
/// </summary>
public sealed class MatchTextChannelController : MonoBehaviour
{
    private const int MaxSubmittedCharacters = 512;
    private const int MaxDisplayCharacters = 1400;
    private const int MaxPayloadBytes = 16 * 1024;
    private const int MaxListResults = 14;

    public static MatchTextChannelController Instance { get; private set; }

    public event Action<MatchTextDisplayPayload> TextDisplayed;

    private bool handlersRegistered;
    private AuthoritativeMatchRuntime subscribedRuntime;

    private NetworkManager Manager => NetworkRuntimeBootstrap.Instance?.NetworkManager;
    private bool NetworkIsActive => Manager != null && Manager.IsListening;
    private MatchRuntimeController RuntimeController => MatchRuntimeController.Instance;
    private AuthoritativeMatchRuntime Runtime => RuntimeController?.Runtime;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        TryRegisterHandlers();
        TrySubscribeRuntimeMessages();
    }

    private void Update()
    {
        TryRegisterHandlers();
        TrySubscribeRuntimeMessages();
    }

    private void OnDestroy()
    {
        UnregisterHandlers();
        UnsubscribeRuntimeMessages();
        if (Instance == this)
            Instance = null;
    }

    public void SubmitLocalText(string rawText)
    {
        string text = NormalizeSubmittedText(rawText);
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!NetworkIsActive)
        {
            int participantId = RuntimeController?.LocalParticipantId ?? 1;
            ProcessSubmittedText(participantId, 0UL, text, true);
            return;
        }

        if (Manager.IsServer)
        {
            NetworkPlayerInfo local = NetworkSessionManager.Instance?.GetLocalPlayer();
            int participantId = local?.ParticipantId ?? RuntimeController?.LocalParticipantId ?? -1;
            ProcessSubmittedText(participantId, Manager.LocalClientId, text, true);
            return;
        }

        SendPayloadToServer(
            MatchTextNetworkMessageNames.Submit,
            new MatchTextSubmitPayload { Text = text });
    }

    private void HandleSubmitMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (Manager == null || !Manager.IsServer)
            return;

        MatchTextSubmitPayload payload = ReadPayload<MatchTextSubmitPayload>(reader);
        if (payload == null)
            return;

        NetworkPlayerInfo participant = NetworkSessionManager.Instance?.Players
            .FirstOrDefault(item => item != null && item.IsHuman && item.ClientId == senderClientId);
        if (participant == null)
        {
            SendDisplayToClient(senderClientId, SystemMessage("No se encontró tu participante de partida.", true));
            return;
        }

        ProcessSubmittedText(
            participant.ParticipantId,
            senderClientId,
            NormalizeSubmittedText(payload.Text),
            false);
    }

    private void HandleDisplayMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (Manager != null && Manager.IsServer)
            return;

        MatchTextDisplayPayload payload = ReadPayload<MatchTextDisplayPayload>(reader);
        if (payload != null)
            RaiseDisplay(payload);
    }

    private void ProcessSubmittedText(
        int participantId,
        ulong senderClientId,
        string text,
        bool isLocalAuthoritySubmission)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        MatchTextCommandParseResult parsed = MatchTextCommandParser.Parse(text);
        if (!parsed.IsCommand)
        {
            BroadcastChat(participantId, text);
            return;
        }

        if (!parsed.Success)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission, SystemMessage(parsed.Error, true));
            return;
        }

        ExecuteCommand(participantId, senderClientId, isLocalAuthoritySubmission, parsed);
    }

    private void ExecuteCommand(
        int participantId,
        ulong senderClientId,
        bool isLocalAuthoritySubmission,
        MatchTextCommandParseResult command)
    {
        if (Runtime == null || !Runtime.IsInitialized)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage("El runtime de la partida aún no está disponible.", true));
            return;
        }

        if (command.CommandType == MatchTextCommandType.Help)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission, SystemMessage(
                "Comandos: /entities [filtro] · /spawn <id> <x> <y> <z> [owner <P>|slot <id>|team <E>] · " +
                "/change_owner <runtime-id> <participant P|slot id> · /change_team <runtime-id> <E> · " +
                "/despawn <runtime-id|last> (retira sin matar) · /runtime [filtro] · /state · /areas · /combat [filtro] · /channels · " +
                "/waves [filtro] · /wave <start|pause|resume|stop|advance> <id> · /headless [filtro] · /nav [filtro] · /path_visualization <on|off> · /diplomacy · /diplomacy_stance <origen> <objetivo> <ally|neutral|enemy> · " +
                "/attack <atacante> <objetivo> · /damage <objetivo> <cantidad> [origen]. " +
                "Spawn solo acepta entidades habilitadas por el escenario.",
                false));
            return;
        }

        if (command.CommandType == MatchTextCommandType.EntityDefinitions)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage(BuildEntityDefinitionList(command.Filter), false));
            return;
        }

        if (command.CommandType == MatchTextCommandType.RuntimeEntities)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage(BuildRuntimeEntityList(command.Filter), false));
            return;
        }

        if (command.CommandType == MatchTextCommandType.MatchState)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage(BuildMatchStateSummary(), false));
            return;
        }

        if (command.CommandType == MatchTextCommandType.Areas)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage(BuildAreaSummary(), false));
            return;
        }

        if (command.CommandType == MatchTextCommandType.Combat)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage(BuildCombatSummary(command.Filter), false));
            return;
        }

        if (command.CommandType == MatchTextCommandType.Channels)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage(BuildChannelSummary(), false));
            return;
        }

        if (command.CommandType == MatchTextCommandType.Waves)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage(BuildWaveSummary(command.Filter), false));
            return;
        }

        if (command.CommandType == MatchTextCommandType.HeadlessControllers)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage(BuildHeadlessControllerSummary(command.Filter), false));
            return;
        }

        if (command.CommandType == MatchTextCommandType.Navigation)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage(BuildNavigationSummary(command.Filter), false));
            return;
        }

        if (command.CommandType == MatchTextCommandType.Diplomacy)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage(BuildDiplomacySummary(), false));
            return;
        }

        if (!CanMutateRuntime(senderClientId, isLocalAuthoritySubmission))
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage("Solo el host o una partida local pueden ejecutar comandos que modifican el runtime.", true));
            return;
        }

        if (command.CommandType == MatchTextCommandType.PathVisualization)
        {
            ExecutePathVisualization(senderClientId, isLocalAuthoritySubmission, command);
            return;
        }

        if (command.CommandType == MatchTextCommandType.DiplomacyStance)
        {
            ExecuteDiplomacyStance(senderClientId, isLocalAuthoritySubmission, command);
            return;
        }

        if (command.CommandType == MatchTextCommandType.Spawn)
        {
            ExecuteSpawn(participantId, senderClientId, isLocalAuthoritySubmission, command);
            return;
        }

        if (command.CommandType == MatchTextCommandType.ChangeOwner)
        {
            ExecuteChangeOwner(senderClientId, isLocalAuthoritySubmission, command);
            return;
        }

        if (command.CommandType == MatchTextCommandType.ChangeTeam)
        {
            ExecuteChangeTeam(senderClientId, isLocalAuthoritySubmission, command);
            return;
        }

        if (command.CommandType == MatchTextCommandType.Despawn)
        {
            ExecuteDespawn(senderClientId, isLocalAuthoritySubmission, command);
            return;
        }

        if (command.CommandType == MatchTextCommandType.Attack)
        {
            ExecuteAttack(senderClientId, isLocalAuthoritySubmission, command);
            return;
        }

        if (command.CommandType == MatchTextCommandType.Damage)
        {
            ExecuteDamage(senderClientId, isLocalAuthoritySubmission, command);
            return;
        }

        if (command.CommandType == MatchTextCommandType.WaveControl)
        {
            ExecuteWaveControl(senderClientId, isLocalAuthoritySubmission, command);
            return;
        }

        ReplyToSender(senderClientId, isLocalAuthoritySubmission,
            SystemMessage("El comando todavía no tiene una ejecución registrada.", true));
    }

    private void ExecutePathVisualization(
        ulong senderClientId,
        bool isLocalAuthoritySubmission,
        MatchTextCommandParseResult command)
    {
        NavigationRuntimeSystem navigation = Runtime.Navigation;
        if (navigation == null)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage("El sistema de navegación todavía no está disponible.", true));
            return;
        }

        navigation.SetPathVisualizationEnabled(command.ToggleEnabled);
        ReplyToSender(senderClientId, isLocalAuthoritySubmission,
            SystemMessage(
                command.ToggleEnabled
                    ? "Visualización de rutas activada para todas las unidades con un camino vigente."
                    : "Visualización de rutas desactivada.",
                false));
    }

    private void ExecuteDiplomacyStance(
        ulong senderClientId,
        bool isLocalAuthoritySubmission,
        MatchTextCommandParseResult command)
    {
        if (Runtime.Diplomacy == null ||
            !DiplomacyRuntimeService.TryParseStance(
                command.DiplomacyStance,
                out DiplomacyStance stance))
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage("El sistema o la postura diplomática no son válidos.", true));
            return;
        }

        bool success = Runtime.Diplomacy.TrySetStance(
            command.SourceTeamId,
            command.TargetTeamId,
            stance,
            RuntimeController?.LocalParticipantId ?? -1,
            "host-console",
            out string rejection);
        ReplyToSender(senderClientId, isLocalAuthoritySubmission,
            SystemMessage(success
                ? $"Diplomacia: Equipo {command.SourceTeamId} → Equipo {command.TargetTeamId} = {stance}."
                : $"Cambio diplomático rechazado: {rejection}",
                !success));
    }

    private void ExecuteWaveControl(
        ulong senderClientId,
        bool isLocalAuthoritySubmission,
        MatchTextCommandParseResult command)
    {
        WaveRuntimeSystem waves = Runtime.Waves;
        if (waves == null)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage("El sistema de oleadas no está inicializado.", true));
            return;
        }

        bool success;
        string rejection;
        switch (command.WaveOperation)
        {
            case "start":
                success = waves.TryStart(command.WaveControllerId, Runtime.ElapsedTime, out rejection);
                break;
            case "pause":
                success = waves.TryPause(command.WaveControllerId, Runtime.ElapsedTime, out rejection);
                break;
            case "resume":
                success = waves.TryResume(command.WaveControllerId, Runtime.ElapsedTime, out rejection);
                break;
            case "stop":
                success = waves.TryStop(command.WaveControllerId, Runtime.ElapsedTime, out rejection);
                break;
            default:
                success = waves.TryAdvance(command.WaveControllerId, Runtime.ElapsedTime, out rejection);
                break;
        }

        ReplyToSender(senderClientId, isLocalAuthoritySubmission,
            SystemMessage(success
                ? $"Oleadas: {command.WaveOperation} aplicado a '{command.WaveControllerId}'."
                : $"Operación de oleada rechazada: {rejection}",
                !success));
    }

    private void ExecuteSpawn(
        int participantId,
        ulong senderClientId,
        bool isLocalAuthoritySubmission,
        MatchTextCommandParseResult command)
    {
        if (!TryResolveCommandOwner(
                command,
                participantId,
                out MatchParticipantRuntimeState owner,
                out int teamId,
                out int colorId,
                out string ownerRejection))
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage($"Spawn rechazado: {ownerRejection}", true));
            return;
        }

        if (Runtime.EntityCatalog == null ||
            !Runtime.EntityCatalog.TryResolveSpawnable(
                command.EntityDefinitionId,
                out string resolvedEntityId,
                out EntityDefinition definition))
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage(
                    $"La entidad '{command.EntityDefinitionId}' no está habilitada para esta partida. " +
                    "Usa /entities para ver el catálogo cargado por el escenario.",
                    true));
            return;
        }

        EntitySpawnRequest request = new()
        {
            EntityDefinitionId = resolvedEntityId,
            ScenarioInstanceId = $"command.spawn.{DateTime.UtcNow.Ticks}",
            OwnerParticipantId = owner?.ParticipantId ?? -1,
            TeamId = teamId,
            ColorId = colorId,
            Position = command.Position,
            Reason = EntityLifecycleReason.MatchCommand
        };

        if (!RuntimeController.QueueEntitySpawn(request, out string rejectionReason))
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage($"Spawn rechazado: {rejectionReason}", true));
            return;
        }

        string ownership = owner == null
            ? "neutral (E0)"
            : $"P{owner.ParticipantId}/{owner.SlotId}/E{owner.TeamId}";
        ReplyToSender(senderClientId, isLocalAuthoritySubmission, SystemMessage(
            $"Spawn encolado: {definition.name} [{resolvedEntityId}] para {ownership} en " +
            $"({command.Position.x:0.##}, {command.Position.y:0.##}, {command.Position.z:0.##}).",
            false));
    }

    private void ExecuteChangeOwner(
        ulong senderClientId,
        bool isLocalAuthoritySubmission,
        MatchTextCommandParseResult command)
    {
        MatchParticipantRuntimeState owner = null;
        if (string.Equals(command.OwnerSelector, "participant", StringComparison.OrdinalIgnoreCase))
        {
            Runtime.Participants.TryGet(command.OwnerParticipantId, out owner);
        }
        else if (string.Equals(command.OwnerSelector, "slot", StringComparison.OrdinalIgnoreCase))
        {
            Runtime.Participants.TryGetBySlotId(command.OwnerSlotId, out owner);
        }

        if (owner == null)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage("No se encontró el participante propietario solicitado.", true));
            return;
        }

        bool success = Runtime.EntityLifecycle.TryTransferOwnership(
            command.RuntimeEntityId,
            owner.ParticipantId,
            out string rejectionReason);
        ReplyToSender(senderClientId, isLocalAuthoritySubmission,
            SystemMessage(success
                ? $"Entidad {command.RuntimeEntityId} transferida a P{owner.ParticipantId}/{owner.SlotId}/E{owner.TeamId}."
                : $"Transferencia rechazada: {rejectionReason}",
                !success));
    }

    private void ExecuteChangeTeam(
        ulong senderClientId,
        bool isLocalAuthoritySubmission,
        MatchTextCommandParseResult command)
    {
        if (!TryResolveParticipantForTeam(command.OwnerTeamId, out MatchParticipantRuntimeState owner, out string rejection))
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage($"Cambio de equipo rechazado: {rejection}", true));
            return;
        }

        int ownerParticipantId = owner?.ParticipantId ?? -1;
        bool success = Runtime.EntityLifecycle.TryTransferOwnership(
            command.RuntimeEntityId,
            ownerParticipantId,
            out string transferRejection);
        string destination = owner == null
            ? "equipo neutral E0"
            : $"P{owner.ParticipantId}/{owner.SlotId}/E{owner.TeamId}";
        ReplyToSender(senderClientId, isLocalAuthoritySubmission,
            SystemMessage(success
                ? $"Entidad {command.RuntimeEntityId} transferida a {destination}."
                : $"Cambio de equipo rechazado: {transferRejection}",
                !success));
    }

    private bool TryResolveCommandOwner(
        MatchTextCommandParseResult command,
        int defaultParticipantId,
        out MatchParticipantRuntimeState owner,
        out int teamId,
        out int colorId,
        out string rejectionReason)
    {
        owner = null;
        teamId = 0;
        colorId = PlayerColorPalette.Neutral;
        rejectionReason = null;

        if (string.IsNullOrWhiteSpace(command.OwnerSelector))
        {
            if (!Runtime.Participants.TryGet(defaultParticipantId, out owner))
            {
                rejectionReason = $"No existe el participante {defaultParticipantId}.";
                return false;
            }
        }
        else if (string.Equals(command.OwnerSelector, "participant", StringComparison.OrdinalIgnoreCase))
        {
            if (!Runtime.Participants.TryGet(command.OwnerParticipantId, out owner))
            {
                rejectionReason = $"No existe el participante {command.OwnerParticipantId}.";
                return false;
            }
        }
        else if (string.Equals(command.OwnerSelector, "slot", StringComparison.OrdinalIgnoreCase))
        {
            if (!Runtime.Participants.TryGetBySlotId(command.OwnerSlotId, out owner))
            {
                rejectionReason = $"No existe el slot '{command.OwnerSlotId}'.";
                return false;
            }
        }
        else if (string.Equals(command.OwnerSelector, "team", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryResolveParticipantForTeam(command.OwnerTeamId, out owner, out rejectionReason))
                return false;
        }
        else
        {
            rejectionReason = $"Selector de propietario desconocido: {command.OwnerSelector}.";
            return false;
        }

        if (owner != null)
        {
            teamId = owner.TeamId;
            colorId = owner.ColorId;
        }

        return true;
    }

    private bool TryResolveParticipantForTeam(
        int teamId,
        out MatchParticipantRuntimeState owner,
        out string rejectionReason)
    {
        owner = null;
        rejectionReason = null;
        if (teamId < 0)
        {
            rejectionReason = "El team-id no puede ser negativo.";
            return false;
        }

        if (teamId == 0)
            return true;

        MatchParticipantRuntimeState[] candidates = Runtime.Participants.All
            .Where(item => item.TeamId == teamId)
            .ToArray();
        if (candidates.Length == 0)
        {
            rejectionReason = $"El equipo {teamId} no tiene participantes propietarios.";
            return false;
        }

        if (candidates.Length > 1)
        {
            rejectionReason =
                $"El equipo {teamId} tiene {candidates.Length} participantes. Usa owner/participant o slot para elegir uno.";
            return false;
        }

        owner = candidates[0];
        return true;
    }

    private void ExecuteDespawn(
        ulong senderClientId,
        bool isLocalAuthoritySubmission,
        MatchTextCommandParseResult command)
    {
        int entityId = command.UseLastRuntimeEntity
            ? Runtime.World.Values.Select(item => item.UnitId).DefaultIfEmpty(-1).Max()
            : command.RuntimeEntityId;

        if (entityId <= 0)
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage("No hay entidades runtime disponibles para eliminar.", true));
            return;
        }

        if (!RuntimeController.QueueEntityDespawn(entityId, EntityLifecycleReason.MatchCommand, out string rejectionReason))
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage($"Despawn rechazado: {rejectionReason}", true));
            return;
        }

        ReplyToSender(senderClientId, isLocalAuthoritySubmission,
            SystemMessage($"Despawn encolado para la entidad runtime {entityId}.", false));
    }

    private void ExecuteAttack(
        ulong senderClientId,
        bool isLocalAuthoritySubmission,
        MatchTextCommandParseResult command)
    {
        if (!Runtime.World.TryGet(command.SourceRuntimeEntityId, out EntityRuntimeState source) ||
            !Runtime.World.TryGet(command.RuntimeEntityId, out EntityRuntimeState target))
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage("No existe el atacante o el objetivo indicado.", true));
            return;
        }

        Runtime.EnqueueRuleCommand(
            source.OwnerParticipantId,
            MatchCommandType.Attack,
            new EntityAttackCommand
            {
                SourceUnitId = source.UnitId,
                TargetUnitId = target.UnitId
            });

        ReplyToSender(senderClientId, isLocalAuthoritySubmission,
            SystemMessage($"Ataque encolado: {source.UnitId} → {target.UnitId}.", false));
    }

    private void ExecuteDamage(
        ulong senderClientId,
        bool isLocalAuthoritySubmission,
        MatchTextCommandParseResult command)
    {
        if (!Runtime.ApplyDamage(
                command.SourceRuntimeEntityId,
                command.RuntimeEntityId,
                command.Amount,
                "command",
                "match-text-command",
                out EntityDamageResult result))
        {
            ReplyToSender(senderClientId, isLocalAuthoritySubmission,
                SystemMessage($"Daño rechazado: {result?.Message}", true));
            return;
        }

        string fatal = result.WasFatal
            ? $" · resolución {result.FatalResolution}"
            : string.Empty;
        ReplyToSender(senderClientId, isLocalAuthoritySubmission,
            SystemMessage(
                $"Daño aplicado a {command.RuntimeEntityId}: {result.PreviousHealth} → {result.CurrentHealth}{fatal}.",
                false));
    }

    private string BuildEntityDefinitionList(string filter)
    {
        IEnumerable<MatchEntityCatalogEntry> definitions =
            Runtime.EntityCatalog?.SpawnableEntries ?? Array.Empty<MatchEntityCatalogEntry>();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            definitions = definitions.Where(item =>
                Contains(item.ReferenceId, filter) ||
                Contains(item.Definition?.name, filter) ||
                Contains(item.Definition?.kind, filter));
        }

        string[] ids = definitions
            .Take(MaxListResults)
            .Select(item => $"{item.ReferenceId} ({item.Definition.name})")
            .ToArray();

        return ids.Length == 0
            ? "El escenario no habilitó entidades de spawn dinámico con ese filtro."
            : $"Entidades habilitadas por la partida ({ids.Length}{(ids.Length == MaxListResults ? "+" : string.Empty)}): {string.Join(" · ", ids)}";
    }

    private string BuildRuntimeEntityList(string filter)
    {
        IEnumerable<EntityRuntimeState> entities = Runtime.World.Values;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            entities = entities.Where(item =>
                Contains(item.EntityDefinitionId, filter) ||
                Contains(item.UnitName, filter) ||
                item.UnitId.ToString() == filter);
        }

        string[] values = entities
            .OrderBy(item => item.UnitId)
            .Take(MaxListResults)
            .Select(item => $"{item.UnitId}:{item.EntityDefinitionId} P{item.OwnerParticipantId}/E{item.TeamId} " +
                            $"vida={item.Health}/{item.MaxHealth} estado={item.Status?.Activity ?? EntityActivityState.Idle}")
            .ToArray();

        return values.Length == 0
            ? "No se encontraron entidades runtime."
            : $"Entidades runtime ({values.Length}{(values.Length == MaxListResults ? "+" : string.Empty)}): {string.Join(" · ", values)}";
    }

    private string BuildMatchStateSummary()
    {
        string phase = Runtime.MatchState == null
            ? "sin estado"
            : Runtime.MatchState.Phase.ToString();
        string result = Runtime.MatchState != null && Runtime.MatchState.IsCompleted
            ? $" · resultado {Runtime.MatchState.Result} equipo {Runtime.MatchState.ResultTeamId}"
            : string.Empty;

        string participants = string.Join(" · ", Runtime.Participants.All.Select(item =>
        {
            string resources = FormatResources(item.Resources);
            string control = item.ControlEnabled ? "control=on" : "control=off";
            string attributes = item.Attributes.Values.Count > 0
                ? $" attrs={string.Join(",", item.Attributes.ToArray())}"
                : string.Empty;
            string variables = item.SnapshotVariables().Count > 0
                ? $" vars={string.Join(",", item.SnapshotVariables().Select(pair => $"{pair.Key}={pair.Value}"))}"
                : string.Empty;
            return $"P{item.ParticipantId}:{item.DisplayName}[slot={item.SlotId};E{item.TeamId};{item.ControllerKind};{item.LifeState};{control}{attributes}{variables}]" +
                   (string.IsNullOrEmpty(resources) ? string.Empty : $" {{{resources}}}");
        }));

        string teams = string.Join(" · ", Runtime.Teams.All.Select(item =>
        {
            string resources = FormatResources(item.Resources);
            return $"E{item.TeamId}" +
                   (string.IsNullOrEmpty(resources) ? string.Empty : $" {{{resources}}}");
        }));

        return $"Partida: {phase}{result}. Participantes: {participants}. Equipos: {teams}.";
    }

    private string BuildAreaSummary()
    {
        string[] values = Runtime.World.Values
            .Where(item => item.Area != null)
            .OrderBy(item => item.UnitId)
            .Take(MaxListResults)
            .Select(item => $"{item.UnitId}:{item.EntityDefinitionId} ocupantes={item.Area.OccupantCount}")
            .ToArray();

        return values.Length == 0
            ? "La partida no tiene entidades de área activas."
            : $"Áreas activas ({values.Length}{(values.Length == MaxListResults ? "+" : string.Empty)}): {string.Join(" · ", values)}";
    }

    private string BuildChannelSummary()
    {
        RuntimeChannelSystem channelSystem = Runtime.Channels;
        if (channelSystem == null)
            return "El sistema de canalizaciones no está inicializado.";

        string[] values = channelSystem.ActiveChannels
            .Take(MaxListResults)
            .Select(item => $"{item.ChannelId}: entidad={item.SourceEntityId} área={item.AreaEntityId} " +
                            $"participante={item.TargetParticipantId} progreso={item.Elapsed:0.00}/{item.Duration:0.00}")
            .ToArray();

        return values.Length == 0
            ? "No hay canalizaciones activas."
            : $"Canalizaciones activas ({values.Length}): {string.Join(" · ", values)}";
    }

    private string BuildNavigationSummary(string filter)
    {
        NavigationRuntimeSystem navigation = Runtime.Navigation;
        if (navigation == null)
            return "El sistema de navegación no está inicializado.";

        IReadOnlyList<string> values = navigation.BuildDiagnostics(filter);
        string detail = values.Count == 0
            ? "No hay entidades navegando con ese filtro."
            : string.Join(" · ", values.Take(MaxListResults));
        return $"Navegación: cell={navigation.CellSize:0.##}, obstáculos={navigation.ObstacleCount}, " +
               $"revisión={navigation.ObstacleRevision}. {detail}";
    }

    private string BuildDiplomacySummary()
    {
        DiplomacyRuntimeService diplomacy = Runtime.Diplomacy;
        if (diplomacy == null)
            return "El sistema de diplomacia no está inicializado.";

        int[] teams = diplomacy.TeamIds.ToArray();
        if (teams.Length == 0)
            return "No hay equipos registrados en la diplomacia.";

        string[] rows = teams.Select(source =>
        {
            string relations = string.Join(", ", teams
                .Where(target => target != source)
                .Select(target => $"E{target}={diplomacy.GetStance(source, target)}"));
            return $"E{source} → [{relations}]";
        }).ToArray();
        return $"Diplomacia direccional: {string.Join(" · ", rows)}";
    }

    private string BuildHeadlessControllerSummary(string filter)
    {
        HeadlessControllerRuntimeSystem system = Runtime.HeadlessControllers;
        if (system == null)
            return "El sistema de controladores Headless no está inicializado.";

        IEnumerable<HeadlessControllerInstanceState> controllers = system.Controllers;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            controllers = controllers.Where(item =>
                Contains(item.ParticipantName, filter) ||
                Contains(item.ProfileId, filter) ||
                Contains(item.RuntimeControllerId, filter) ||
                Contains(item.Status.ToString(), filter));
        }

        string[] values = controllers
            .Take(MaxListResults)
            .Select(item =>
                $"P{item.ParticipantId}:{item.ParticipantName} perfil={item.ProfileId ?? "-"} " +
                $"controlador={item.RuntimeControllerId ?? "-"} estado={item.Status} " +
                $"órdenes={item.OrdersIssued} decisión={item.LastDecision ?? "-"}")
            .ToArray();

        return values.Length == 0
            ? "No hay controladores Headless que coincidan."
            : $"Controladores Headless ({values.Length}): {string.Join(" · ", values)}";
    }

    private string BuildWaveSummary(string filter)
    {
        WaveRuntimeSystem waves = Runtime.Waves;
        if (waves == null)
            return "El sistema de oleadas no está inicializado.";

        IEnumerable<WaveControllerRuntimeState> controllers = waves.Controllers;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            controllers = controllers.Where(item =>
                Contains(item.ControllerId, filter) ||
                Contains(item.CurrentWaveId, filter) ||
                Contains(item.Status.ToString(), filter));
        }

        string[] values = controllers
            .Take(MaxListResults)
            .Select(item => item.ToString())
            .ToArray();
        return values.Length == 0
            ? "No hay controladores de oleadas con ese filtro."
            : $"Oleadas ({values.Length}{(values.Length == MaxListResults ? "+" : string.Empty)}): {string.Join(" · ", values)}";
    }

    private string BuildCombatSummary(string filter)
    {
        IEnumerable<EntityRuntimeState> entities = Runtime.World.Values
            .Where(item =>
                item.Attack != null ||
                item.Status?.InCombat == true ||
                item.Status?.IsUnderAttack == true ||
                item.Life?.State == EntityLifeState.Dead);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            entities = entities.Where(item =>
                Contains(item.EntityDefinitionId, filter) ||
                Contains(item.UnitName, filter) ||
                item.UnitId.ToString() == filter);
        }

        string[] values = entities
            .OrderBy(item => item.UnitId)
            .Take(MaxListResults)
            .Select(item =>
            {
                string attack = item.Attack == null
                    ? "sin ataque"
                    : $"{item.Attack.Delivery}/{item.Attack.DamageType} daño={item.Attack.BaseDamage} " +
                      $"fase={item.Attack.Phase} objetivo={item.Attack.TargetEntityId}";
                string flags = $"actividad={item.Status?.Activity ?? EntityActivityState.Idle}" +
                               (item.Status?.InCombat == true ? " combate" : string.Empty) +
                               (item.Status?.IsUnderAttack == true ? " bajo-ataque" : string.Empty);
                string death = item.Life?.State == EntityLifeState.Dead
                    ? $" muerte={item.Life.DeathOutcome}" +
                      (string.IsNullOrWhiteSpace(item.Life.DeathReplacementEntityId)
                          ? string.Empty
                          : $"->{item.Life.DeathReplacementEntityId}")
                    : string.Empty;
                return $"{item.UnitId}:{item.UnitName} vida={item.Health}/{item.MaxHealth} {attack} {flags}{death}";
            })
            .ToArray();

        return values.Length == 0
            ? "No hay entidades de combate con ese filtro."
            : $"Combate ({values.Length}{(values.Length == MaxListResults ? "+" : string.Empty)}): {string.Join(" · ", values)}";
    }

    private static string FormatResources(RuntimeResourceCollection resources)
    {
        if (resources == null)
            return string.Empty;
        return string.Join(", ", resources.Snapshot().Select(item => $"{item.Key}={item.Value}"));
    }

    private void BroadcastChat(int participantId, string text)
    {
        string displayName = ResolveParticipantName(participantId);
        MatchTextDisplayPayload payload = new()
        {
            SenderName = displayName,
            Text = text,
            IsSystem = false,
            IsError = false
        };

        RaiseDisplay(payload);
        if (Manager != null && Manager.IsServer && Manager.ConnectedClientsIds.Count > 1)
            SendPayloadToAll(MatchTextNetworkMessageNames.Display, payload);
    }

    private void ReplyToSender(
        ulong senderClientId,
        bool isLocalAuthoritySubmission,
        MatchTextDisplayPayload payload)
    {
        if (isLocalAuthoritySubmission || !NetworkIsActive || senderClientId == Manager.LocalClientId)
        {
            RaiseDisplay(payload);
            return;
        }

        SendDisplayToClient(senderClientId, payload);
    }

    private void SendDisplayToClient(ulong clientId, MatchTextDisplayPayload payload)
    {
        if (Manager?.CustomMessagingManager == null || payload == null)
            return;

        SendPayloadToClient(MatchTextNetworkMessageNames.Display, clientId, payload);
    }

    private bool CanMutateRuntime(ulong senderClientId, bool isLocalAuthoritySubmission)
    {
        if (!NetworkIsActive)
            return true;
        if (Manager == null || !Manager.IsServer)
            return false;

        // Las solicitudes locales del host se procesan directamente. Un cliente
        // remoto no obtiene privilegios de depuración por estar conectado.
        return isLocalAuthoritySubmission && senderClientId == Manager.LocalClientId;
    }

    private string ResolveParticipantName(int participantId)
    {
        if (Runtime?.Participants != null && Runtime.Participants.TryGet(participantId, out MatchParticipantRuntimeState state))
            return string.IsNullOrWhiteSpace(state.DisplayName) ? $"Participante {participantId}" : state.DisplayName;
        return $"Participante {participantId}";
    }

    private static bool Contains(string source, string value)
    {
        return !string.IsNullOrWhiteSpace(source) &&
               source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static MatchTextDisplayPayload SystemMessage(string text, bool isError)
    {
        return new MatchTextDisplayPayload
        {
            SenderName = "Sistema",
            Text = Limit(text, MaxDisplayCharacters),
            IsSystem = true,
            IsError = isError
        };
    }

    private void RaiseDisplay(MatchTextDisplayPayload payload)
    {
        if (payload == null)
            return;

        payload.SenderName = Limit(payload.SenderName, 64);
        payload.Text = Limit(payload.Text, MaxDisplayCharacters);
        TextDisplayed?.Invoke(payload);
    }

    private static string NormalizeSubmittedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return Limit(normalized, MaxSubmittedCharacters);
    }

    private static string Limit(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;
        return value.Substring(0, maxLength);
    }

    private void TrySubscribeRuntimeMessages()
    {
        AuthoritativeMatchRuntime current = Runtime;
        if (ReferenceEquals(current, subscribedRuntime))
            return;

        UnsubscribeRuntimeMessages();
        subscribedRuntime = current;
        if (subscribedRuntime != null)
            subscribedRuntime.RuntimeMessageRaised += OnRuntimeMessageRaised;
    }

    private void UnsubscribeRuntimeMessages()
    {
        if (subscribedRuntime != null)
            subscribedRuntime.RuntimeMessageRaised -= OnRuntimeMessageRaised;
        subscribedRuntime = null;
    }

    private void OnRuntimeMessageRaised(string message, bool isError)
    {
        MatchTextDisplayPayload payload = SystemMessage(message, isError);
        RaiseDisplay(payload);
        if (Manager != null && Manager.IsServer && Manager.ConnectedClientsIds.Count > 1)
            SendPayloadToAll(MatchTextNetworkMessageNames.Display, payload);
    }

    private void TryRegisterHandlers()
    {
        if (!NetworkIsActive || handlersRegistered || Manager?.CustomMessagingManager == null)
            return;

        Manager.CustomMessagingManager.RegisterNamedMessageHandler(
            MatchTextNetworkMessageNames.Submit,
            HandleSubmitMessage);
        Manager.CustomMessagingManager.RegisterNamedMessageHandler(
            MatchTextNetworkMessageNames.Display,
            HandleDisplayMessage);
        handlersRegistered = true;
    }

    private void UnregisterHandlers()
    {
        if (!handlersRegistered || Manager?.CustomMessagingManager == null)
            return;

        Manager.CustomMessagingManager.UnregisterNamedMessageHandler(MatchTextNetworkMessageNames.Submit);
        Manager.CustomMessagingManager.UnregisterNamedMessageHandler(MatchTextNetworkMessageNames.Display);
        handlersRegistered = false;
    }

    private void SendPayloadToServer(string messageName, object payload)
    {
        if (Manager?.CustomMessagingManager == null)
            return;

        SendPayloadToClient(messageName, NetworkManager.ServerClientId, payload);
    }

    private void SendPayloadToAll(string messageName, object payload)
    {
        if (Manager?.CustomMessagingManager == null || payload == null)
            return;

        byte[] bytes = Serialize(payload);
        using FastBufferWriter writer = CreateWriter(bytes);
        Manager.CustomMessagingManager.SendNamedMessageToAll(
            messageName,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void SendPayloadToClient(string messageName, ulong clientId, object payload)
    {
        if (Manager?.CustomMessagingManager == null || payload == null)
            return;

        byte[] bytes = Serialize(payload);
        using FastBufferWriter writer = CreateWriter(bytes);
        Manager.CustomMessagingManager.SendNamedMessage(
            messageName,
            clientId,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    private static byte[] Serialize(object payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
        if (bytes.Length > MaxPayloadBytes)
            throw new InvalidOperationException($"El mensaje de texto excede {MaxPayloadBytes} bytes.");
        return bytes;
    }

    private static FastBufferWriter CreateWriter(byte[] bytes)
    {
        FastBufferWriter writer = new(sizeof(int) + bytes.Length, Allocator.Temp);
        writer.WriteValueSafe(bytes.Length);
        writer.WriteBytesSafe(bytes, bytes.Length);
        return writer;
    }

    private static T ReadPayload<T>(FastBufferReader reader) where T : class
    {
        reader.ReadValueSafe(out int length);
        if (length <= 0 || length > MaxPayloadBytes)
            return null;

        byte[] bytes = new byte[length];
        reader.ReadBytesSafe(ref bytes, length);
        return JsonUtility.FromJson<T>(Encoding.UTF8.GetString(bytes));
    }
}
