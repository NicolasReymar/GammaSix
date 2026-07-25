using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Fuente única de verdad para la selección local y el grupo inspeccionado.
/// No lee input ni conoce detalles de red; recibe reglas mediante delegados.
/// </summary>
public sealed class EntitySelectionService
{
    private readonly int maxSelection;
    private readonly Func<NetworkEntityView, bool> isOwnedByLocalPlayer;
    private readonly Func<NetworkEntityView> lockedTargetProvider;
    private readonly List<NetworkEntityView> selected = new();
    private int inspectedGroupIndex;

    public event Action SelectionChanged;
    public event Action InspectionChanged;

    public IReadOnlyList<NetworkEntityView> Selected => selected;
    public int InspectedGroupIndex => inspectedGroupIndex;
    public NetworkEntityView PrimarySelected => GetInspectedGroup()?.Representative;

    public EntitySelectionService(
        int maxSelection,
        Func<NetworkEntityView, bool> isOwnedByLocalPlayer,
        Func<NetworkEntityView> lockedTargetProvider)
    {
        this.maxSelection = Math.Max(1, maxSelection);
        this.isOwnedByLocalPlayer = isOwnedByLocalPlayer ?? throw new ArgumentNullException(nameof(isOwnedByLocalPlayer));
        this.lockedTargetProvider = lockedTargetProvider ?? (() => null);
    }

    public bool HasOwnedGroup => selected.Count(isOwnedByLocalPlayer) > 1;
    public bool HasNonOwnedSelection => selected.Any(view => view != null && !isOwnedByLocalPlayer(view));

    public void SetExclusive(NetworkEntityView view)
    {
        NetworkEntityView locked = lockedTargetProvider();
        ClearInternal();
        if (locked != null && locked != view)
            AddInternal(locked);
        AddInternal(view);
        NotifySelectionChanged();
    }

    public void Toggle(NetworkEntityView view)
    {
        if (view == null)
            return;

        if (selected.Contains(view))
        {
            if (view == lockedTargetProvider())
                return;
            RemoveInternal(view);
        }
        else
        {
            AddInternal(view);
        }

        NotifySelectionChanged();
    }

    public void Add(NetworkEntityView view)
    {
        if (!AddInternal(view))
            return;
        NotifySelectionChanged();
    }

    public void Remove(NetworkEntityView view)
    {
        if (!RemoveInternal(view))
            return;
        NotifySelectionChanged();
    }

    public void Clear()
    {
        if (selected.Count == 0)
            return;
        ClearInternal();
        NotifySelectionChanged();
    }

    public void ClearExceptLockedTarget()
    {
        NetworkEntityView locked = lockedTargetProvider();
        bool changed = false;

        foreach (NetworkEntityView view in selected.ToList())
        {
            if (view == null || view == locked)
                continue;
            changed |= RemoveInternal(view);
        }

        if (locked != null)
            changed |= AddInternal(locked);

        if (changed)
            NotifySelectionChanged();
    }

    public bool EnsureLockedTargetSelected()
    {
        NetworkEntityView locked = lockedTargetProvider();
        if (locked == null)
            return false;

        if (AddInternal(locked))
            NotifySelectionChanged();
        return true;
    }

    public IReadOnlyList<SelectionInspectionGroup> GetInspectionGroups()
    {
        return SelectionGroupBuilder.Build(selected);
    }

    public IReadOnlyList<SelectionInspectionGroup> GetExtendedInspectionGroups()
    {
        return SelectionGroupBuilder.Build(selected, isOwnedByLocalPlayer);
    }

    public SelectionInspectionGroup GetInspectedGroup()
    {
        IReadOnlyList<SelectionInspectionGroup> groups = GetInspectionGroups();
        if (groups.Count == 0)
            return null;

        inspectedGroupIndex = Mathf.Clamp(inspectedGroupIndex, 0, groups.Count - 1);
        return groups[inspectedGroupIndex];
    }

    public void SetInspectedGroup(int index)
    {
        IReadOnlyList<SelectionInspectionGroup> groups = GetInspectionGroups();
        int previous = inspectedGroupIndex;
        inspectedGroupIndex = groups.Count == 0 ? 0 : Mathf.Clamp(index, 0, groups.Count - 1);
        if (previous != inspectedGroupIndex)
            InspectionChanged?.Invoke();
    }

    public void CycleInspectedGroup()
    {
        IReadOnlyList<SelectionInspectionGroup> groups = GetInspectionGroups();
        if (groups.Count <= 1)
            return;
        inspectedGroupIndex = (inspectedGroupIndex + 1) % groups.Count;
        InspectionChanged?.Invoke();
    }

    public void RemoveDestroyedViews()
    {
        bool changed = false;
        for (int index = selected.Count - 1; index >= 0; index--)
        {
            if (selected[index] != null)
                continue;
            selected.RemoveAt(index);
            changed = true;
        }

        if (changed)
            NotifySelectionChanged();
    }

    private bool AddInternal(NetworkEntityView view)
    {
        if (view == null || selected.Contains(view) || selected.Count >= maxSelection)
            return false;
        selected.Add(view);
        view.SetSelected(true);
        return true;
    }

    private bool RemoveInternal(NetworkEntityView view)
    {
        if (view == null || !selected.Remove(view))
            return false;
        view.SetSelected(false);
        return true;
    }

    private void ClearInternal()
    {
        foreach (NetworkEntityView view in selected)
        {
            if (view != null)
                view.SetSelected(false);
        }
        selected.Clear();
        inspectedGroupIndex = 0;
    }

    private void NotifySelectionChanged()
    {
        NormalizeInspectedIndex();
        SelectionChanged?.Invoke();
        InspectionChanged?.Invoke();
    }

    private void NormalizeInspectedIndex()
    {
        int count = GetInspectionGroups().Count;
        inspectedGroupIndex = count == 0 ? 0 : Mathf.Clamp(inspectedGroupIndex, 0, count - 1);
    }
}
