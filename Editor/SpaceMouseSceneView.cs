using UnityEditor;
using UnityEngine;

namespace Gutenbrook.SpaceMouse
{
    /// <summary>Flies the scene view. Registered by the driver itself.</summary>
    /// <remarks>
    /// The scene view has no settable camera transform: it is described by a
    /// pivot, a rotation and a size, and the camera is derived from those. So a
    /// pose that comes back from the driver is turned around — the rotation is
    /// taken as it is, and the pivot is placed the current camera distance ahead
    /// of the new position. Keeping the distance means an orbit stays an orbit
    /// and pushing the puck dollies instead of changing the zoom level.
    /// </remarks>
    internal sealed class SpaceMouseSceneView : ISpaceMouseNavigator
    {
        public int Priority => 0;

        public bool TryNavigate()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null) return false;

            var position = view.camera.transform.position;
            var rotation = view.camera.transform.rotation;

            if (SpaceMouseCamera.TryTakePose(position, rotation, out var moved, out var movedRotation))
            {
                position = moved;
                rotation = movedRotation;

                var distance = view.cameraDistance;
                view.rotation = rotation;
                view.pivot = position + rotation * Vector3.forward * distance;
                view.Repaint();
            }

            // Reported from what was just applied rather than from the camera,
            // which only catches up on the next repaint.
            SpaceMouseCamera.ReportPose(position, rotation, view.pivot,
                                        view.camera.fieldOfView * Mathf.Deg2Rad,
                                        !view.camera.orthographic);
            return true;
        }
    }
}
