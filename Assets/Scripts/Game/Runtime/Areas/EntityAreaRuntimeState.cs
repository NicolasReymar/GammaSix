using System;
using UnityEngine;

public sealed class EntityAreaRuntimeState
{
    public string Shape = EntityAreaShapes.Circle;
    public float Radius = 1f;
    public Vector3 Size = new(2f, 1f, 2f);
    public string Relationship = EntityAreaRelationships.All;
    public string[] RequiredAttributes = Array.Empty<string>();
    public string[] ExcludedAttributes = Array.Empty<string>();
    public bool EmitEnter = true;
    public bool EmitStay;
    public bool EmitExit = true;
    public float StayInterval = 1f;
    public bool Visible = true;
    public int OccupantCount;
}
