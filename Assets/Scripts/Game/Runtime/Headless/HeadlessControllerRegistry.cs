using System;
using System.Collections.Generic;

/// <summary>
/// Registro de implementaciones seguras incluidas en GammaSix. Los escenarios
/// seleccionan y configuran IDs conocidos, pero no cargan ensamblados ni código.
/// </summary>
public static class HeadlessControllerRegistry
{
    public const string SimpleAssaultControllerId = "base:headless-controller.simple-assault";

    private static readonly Dictionary<string, Func<IHeadlessController>> Factories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [SimpleAssaultControllerId] = () => new SimpleAssaultHeadlessController()
        };

    public static bool IsRegistered(string controllerId)
    {
        return !string.IsNullOrWhiteSpace(controllerId) &&
               Factories.ContainsKey(controllerId.Trim());
    }

    public static bool TryCreate(string controllerId, out IHeadlessController controller)
    {
        controller = null;
        if (string.IsNullOrWhiteSpace(controllerId) ||
            !Factories.TryGetValue(controllerId.Trim(), out Func<IHeadlessController> factory))
        {
            return false;
        }

        controller = factory();
        return controller != null;
    }
}
