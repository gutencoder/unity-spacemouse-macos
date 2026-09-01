using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Simulation;

namespace Gutenbrook.SpaceMouse.Simulation
{
    /// <summary>Flies the simulated AR device camera during play mode.</summary>
    /// <remarks>
    /// Compiled only when AR Foundation is in the project — the assembly carries
    /// a version define for it, so a project without AR Foundation simply never
    /// builds this and keeps the scene view navigation.
    ///
    /// XR Simulation drives its camera through <c>SimulationCameraPoseProvider</c>,
    /// which recomputes a pose from the keyboard and mouse on every input update
    /// and hands it to the XR subsystem. It builds that pose relative to its own
    /// transform, so moving the transform is enough: the provider picks the new
    /// position up on its next update and forwards it. Nothing in the package has
    /// to be touched.
    ///
    /// One limit comes with it. The provider rebuilds its rotation from yaw and
    /// pitch alone and clamps the pitch, so a roll of the puck is dropped and
    /// looking straight up or down stops at the clamp. That matches a handheld
    /// device better than free flight would, and it is AR Foundation's decision,
    /// not one made here.
    /// </remarks>
    internal sealed class SpaceMouseSimulationCamera : ISpaceMouseNavigator
    {
        private SimulationCameraPoseProvider m_Provider;

        [InitializeOnLoadMethod]
        private static void Register() => SpaceMouseDriver.Register(new SpaceMouseSimulationCamera());

        // Above the scene view: while the simulation runs, the device belongs to it.
        public int Priority => 100;

        public bool TryNavigate()
        {
            if (!EditorApplication.isPlaying) return false;

            if (m_Provider == null)
                m_Provider = Object.FindAnyObjectByType<SimulationCameraPoseProvider>();
            if (m_Provider == null) return false;

            var transform = m_Provider.transform;
            var position  = transform.position;
            var rotation  = transform.rotation;

            if (SpaceMouseCamera.TryTakePose(position, rotation, out var moved, out var movedRotation))
            {
                position = moved;
                rotation = movedRotation;
                transform.SetPositionAndRotation(position, rotation);
            }

            var camera = Camera.main;
            var fov = camera != null ? camera.fieldOfView : 60f;
            SpaceMouseCamera.ReportPose(position, rotation, SpaceMouseDriver.WorldExtents.center,
                                        fov * Mathf.Deg2Rad, true);
            return true;
        }
    }
}
