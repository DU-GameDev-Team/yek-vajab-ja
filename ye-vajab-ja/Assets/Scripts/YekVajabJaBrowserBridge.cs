using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>WebGL bridge for the accessible HTML game interface.</summary>
public static class YekVajabJaBrowserBridge
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void YV_UpdateUi(string jsonState);
#endif

    public static void UpdateUi(string jsonState)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        YV_UpdateUi(jsonState);
#endif
    }
}
