using UnityEngine;

public static class PlayerColorPalette
{
    public const int Neutral = -1;
    public const int Red = 0;
    public const int Blue = 1;
    public const int Yellow = 2;
    public const int Green = 3;
    public const int Purple = 4;
    public const int Orange = 5;
    public const int Brown = 6;
    public const int Cyan = 7;
    public const int Pink = 8;
    public const int Count = 9;

    public static string GetName(int colorId)
    {
        return colorId switch
        {
            Neutral => "Neutral",
            Red => "Rojo",
            Blue => "Azul",
            Yellow => "Amarillo",
            Green => "Verde",
            Purple => "Morado",
            Orange => "Naranja",
            Brown => "Café",
            Cyan => "Celeste",
            Pink => "Rosa",
            _ => "Desconocido"
        };
    }

    public static Color GetColor(int colorId)
    {
        return colorId switch
        {
            Neutral => new Color(0.78f, 0.80f, 0.82f),
            Red => new Color(0.86f, 0.12f, 0.12f),
            Blue => new Color(0.12f, 0.35f, 0.95f),
            Yellow => new Color(0.95f, 0.82f, 0.12f),
            Green => new Color(0.12f, 0.70f, 0.25f),
            Purple => new Color(0.55f, 0.20f, 0.78f),
            Orange => new Color(0.95f, 0.45f, 0.10f),
            Brown => new Color(0.45f, 0.25f, 0.12f),
            Cyan => new Color(0.18f, 0.75f, 0.90f),
            Pink => new Color(0.95f, 0.40f, 0.68f),
            _ => Color.white
        };
    }

    public static int Normalize(int colorId)
    {
        return colorId == Neutral ? Neutral : Mathf.Clamp(colorId, 0, Count - 1);
    }
}
