using UnityEngine;

public static class DebugExtension
{
    public static void DrawSphere(Vector2 pos, Color color, float radius = 0.25f, float duration = 0.05f)
    {
        Debug.DrawLine(pos + Vector2.up * radius, pos - Vector2.up * radius, color, duration);
        Debug.DrawLine(pos + Vector2.right * radius, pos - Vector2.right * radius, color, duration);
    }

    // 👇 version "safe" sans GUI
    public static void DrawText(Vector2 position, string text, Color color, float size = 0.7f)
    {
        // En mode debug : juste affiche dans la console
        // (évite les GUI calls hors OnGUI)
#if UNITY_EDITOR
        UnityEngine.Debug.DrawLine(position, position + Vector2.up * 0.3f, color, 0.05f);
#endif
    }
}