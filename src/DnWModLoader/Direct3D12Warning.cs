using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DnWModLoader
{
    // Unity 6000.3's Direct3D 12 renderer crashes the game at random when the overlay opens. This warns players to use D3D11
    internal static class Direct3D12Warning
    {
        public const string Banner = "Direct3D 12 can cause crashes (Unity bug). Please add -force-d3d11 to the launch options (On Steam: Properties > General > Launch Options).";

        public const string LogMessage = "Game was launched in Direct3D 12 mode, which can cause crashes due to a Unity bug.";

        public static bool Applies()
        {
            try
            {
                return SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
