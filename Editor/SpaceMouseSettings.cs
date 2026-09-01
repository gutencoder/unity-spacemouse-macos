using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Gutenbrook.SpaceMouse
{
    /// <summary>The preferences page for the 3D mouse.</summary>
    /// <remarks>
    /// Under Preferences rather than Project Settings on purpose: which device is
    /// plugged in and how fast it should feel belongs to the person at the desk,
    /// not to the project, and so it does not travel in version control.
    /// </remarks>
    internal static class SpaceMouseSettings
    {
        internal const string Path = "Preferences/SpaceMouse";

        [SettingsProvider]
        public static SettingsProvider Create() =>
            new SettingsProvider(Path, SettingsScope.User)
            {
                label = "SpaceMouse",
                keywords = new HashSet<string>
                {
                    "3dconnexion", "spacemouse", "3d mouse", "navigation", "speed", "scene view"
                },
                guiHandler = _ => Draw(),
            };

        private static void Draw()
        {
            EditorGUIUtility.labelWidth = 170f;
            EditorGUILayout.Space();

            var enabled = EditorGUILayout.Toggle("Enabled", SpaceMouseDriver.Enabled);
            if (enabled != SpaceMouseDriver.Enabled)
                SpaceMouseDriver.Enabled = enabled;

            using (new EditorGUI.DisabledScope(!enabled))
            {
                var speed = EditorGUILayout.Slider("Speed", SpaceMouseDriver.Speed, 0.02f, 2f);
                if (!Mathf.Approximately(speed, SpaceMouseDriver.Speed))
                    SpaceMouseDriver.Speed = speed;

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUIUtility.labelWidth);
                    if (GUILayout.Button($"Back to {SpaceMouseDriver.DefaultSpeed:0.00}", GUILayout.Width(120f)))
                        SpaceMouseDriver.Speed = SpaceMouseDriver.DefaultSpeed;
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "The driver works out the movement itself and offers the editor a finished camera. " +
                "Speed is the share of that movement the editor follows, so 0.5 covers half the " +
                "distance for the same push.\n\n" +
                "The 3Dconnexion panel has a speed of its own, per application and system wide. " +
                "This one sits on top of it and stays inside the editor.",
                MessageType.None);
        }
    }
}
