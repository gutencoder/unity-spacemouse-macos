using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Gutenbrook.SpaceMouse
{
    /// <summary>Keeps the connection to the 3D mouse open and drives a camera every editor tick.</summary>
    /// <remarks>
    /// Three conditions have to hold at once before a 3Dconnexion device sends
    /// anything on macOS, and missing one of them fails without a word: the client
    /// must be a bundled application, it must register through the old
    /// ConnexionClient API so the driver knows it as an application, and it must
    /// run a navlib connection for the navigation. The editor is the bundle; the
    /// native plugin does the other two.
    ///
    /// Focus is what actually points the device here. The driver serves whichever
    /// application is in front, so this class follows the editor's focus and says
    /// so — which is also what hands the 3D mouse straight back to Fusion or
    /// SolidWorks the moment you switch away.
    /// </remarks>
    [InitializeOnLoad]
    public static class SpaceMouseDriver
    {
        private const string EnabledKey = "Gutenbrook.SpaceMouse.Enabled";
        private const string SpeedKey   = "Gutenbrook.SpaceMouse.Speed";
        private const string MenuPath   = "Tools/SpaceMouse/Enabled";

        /// <summary>The speed a fresh installation starts at.</summary>
        public const float DefaultSpeed = 0.35f;

        private static readonly List<ISpaceMouseNavigator> s_navigators = new List<ISpaceMouseNavigator>();

        private static bool   s_open;
        private static bool   s_hadFocus;
        private static double s_nextExtentsRefresh;
        private static Bounds s_extents = new Bounds(Vector3.zero, Vector3.one * 10f);

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, true);
            set { EditorPrefs.SetBool(EnabledKey, value); if (!value) Close(); }
        }

        /// <summary>How much of an offered movement is taken, between 0.02 and 2.</summary>
        /// <remarks>
        /// navlib owns the navigation and hands over finished camera poses; there
        /// is no speed to ask it for. What is ours to decide is how far along we
        /// follow each one. The damped pose is reported straight back, so navlib
        /// carries on from where the camera really is and the whole path scales
        /// with this factor instead of drifting apart from it.
        ///
        /// This sits on top of the speed in the 3Dconnexion panel rather than
        /// replacing it — that one is per application and applies to the whole
        /// system, this one only to the editor.
        /// </remarks>
        public static float Speed
        {
            get => Mathf.Clamp(EditorPrefs.GetFloat(SpeedKey, DefaultSpeed), 0.02f, 2f);
            set => EditorPrefs.SetFloat(SpeedKey, Mathf.Clamp(value, 0.02f, 2f));
        }

        /// <summary>How big the world is, which is how the driver scales its speed.</summary>
        /// <remarks>
        /// Getting this wrong is not cosmetic. With a box far smaller than the
        /// distance the camera actually travels, the driver overshoots by orders
        /// of magnitude on every push.
        /// </remarks>
        public static Bounds WorldExtents => s_extents;

        static SpaceMouseDriver()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Close;
            EditorApplication.quitting += Close;

            Register(new SpaceMouseSceneView());
        }

        /// <summary>Adds a camera the 3D mouse may fly. Registering twice is a no-op.</summary>
        public static void Register(ISpaceMouseNavigator navigator)
        {
            if (navigator == null || s_navigators.Contains(navigator)) return;
            s_navigators.Add(navigator);
            s_navigators.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public static void Unregister(ISpaceMouseNavigator navigator) => s_navigators.Remove(navigator);

        [MenuItem(MenuPath)]
        private static void Toggle() => Enabled = !Enabled;

        [MenuItem(MenuPath, validate = true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        [MenuItem("Tools/SpaceMouse/Settings")]
        private static void OpenSettings() => SettingsService.OpenUserPreferences(SpaceMouseSettings.Path);

        [MenuItem("Tools/SpaceMouse/Diagnostics")]
        private static void Diagnostics() =>
            Debug.Log($"SpaceMouse: open={SpaceMouseNative.SM_IsOpen()}, " +
                      $"moving={SpaceMouseNative.SM_IsMoving()}, " +
                      $"camera updates from the driver={SpaceMouseNative.SM_AffineWrites()}, " +
                      $"navigators={s_navigators.Count}, world extents={s_extents.size}, speed={Speed:F2}");

        private static void Tick()
        {
            if (!Enabled || !Open()) return;

            var focused = UnityEditorInternal.InternalEditorUtility.isApplicationActive;
            if (focused != s_hadFocus)
            {
                SpaceMouseNative.SM_SetFocus(focused ? 1 : 0);
                s_hadFocus = focused;
            }
            if (!focused) return;

            RefreshExtents();

            for (var i = 0; i < s_navigators.Count; i++)
                if (s_navigators[i].TryNavigate())
                    return;
        }

        private static bool Open()
        {
            if (s_open) return true;

            // leftHanded: Unity is left handed, navlib is not. Declaring it lets
            // navlib convert, and every matrix stays in plain Unity coordinates.
            // Claiming the identity instead makes the driver's idea of the camera
            // and ours drift apart, and the view runs away exponentially.
            int err;
            try
            {
                err = SpaceMouseNative.SM_Open("Unity", leftHanded: 1);
            }
            catch (System.DllNotFoundException)
            {
                Debug.LogWarning("SpaceMouse: the native plugin could not be loaded. " +
                                 "This package supports macOS only.");
                Enabled = false;
                return false;
            }

            if (err != 0)
            {
                Debug.LogWarning($"SpaceMouse: the driver refused the connection (navlib error {err}). " +
                                 "Is 3DxWare installed and running?");
                Enabled = false;
                return false;
            }

            s_open = true;
            s_hadFocus = false;
            return true;
        }

        private static void Close()
        {
            if (!s_open) return;
            SpaceMouseNative.SM_Close();
            s_open = false;
            s_hadFocus = false;
        }

        private static void RefreshExtents()
        {
            if (EditorApplication.timeSinceStartup < s_nextExtentsRefresh) return;
            s_nextExtentsRefresh = EditorApplication.timeSinceStartup + 2.0;

            var renderers = Object.FindObjectsByType<Renderer>();
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            if (bounds.size.sqrMagnitude > 1e-6f) s_extents = bounds;
        }
    }
}
