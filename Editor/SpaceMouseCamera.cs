using UnityEngine;

namespace Gutenbrook.SpaceMouse
{
    /// <summary>The exchange between a camera in the editor and the 3D mouse.</summary>
    /// <remarks>
    /// One tick is two calls, and the order matters. First ask whether the driver
    /// moved the camera and follow if it did, then report where the camera ended
    /// up. Reporting first would overwrite a pending move with the stale pose and
    /// the view would stutter.
    ///
    /// Reporting every tick, not only after a move, is what keeps the driver and
    /// the editor agreeing on where the camera is. Move the view by hand and the
    /// next push carries on from there.
    /// </remarks>
    public static class SpaceMouseCamera
    {
        private static readonly double[] s_affine     = new double[16];
        private static readonly double[] s_extentsMin = new double[3];
        private static readonly double[] s_extentsMax = new double[3];
        private static readonly double[] s_pivot      = new double[3];

        /// <summary>Takes a pose the driver moved, damped by <see cref="SpaceMouseDriver.Speed"/>.</summary>
        /// <returns>False when the driver has nothing new, which is most ticks.</returns>
        public static bool TryTakePose(Vector3 currentPosition, Quaternion currentRotation,
                                       out Vector3 position, out Quaternion rotation)
        {
            position = currentPosition;
            rotation = currentRotation;

            if (SpaceMouseNative.SM_TakeView(s_affine) == 0) return false;

            SpaceMouseNative.Unpack(s_affine, out position, out rotation);

            var speed = SpaceMouseDriver.Speed;
            if (!Mathf.Approximately(speed, 1f))
            {
                position = Vector3.LerpUnclamped(currentPosition, position, speed);
                rotation = Quaternion.SlerpUnclamped(currentRotation, rotation, speed);
            }
            return true;
        }

        /// <summary>States where the camera is, so the driver moves on from the truth.</summary>
        /// <param name="pivot">What an orbit turns around.</param>
        /// <param name="fieldOfViewRadians">Ignored while <paramref name="perspective"/> is false.</param>
        public static void ReportPose(Vector3 position, Quaternion rotation, Vector3 pivot,
                                      float fieldOfViewRadians, bool perspective)
        {
            SpaceMouseNative.Pack(position, rotation, s_affine);

            var extents = SpaceMouseDriver.WorldExtents;
            s_extentsMin[0] = extents.min.x; s_extentsMin[1] = extents.min.y; s_extentsMin[2] = extents.min.z;
            s_extentsMax[0] = extents.max.x; s_extentsMax[1] = extents.max.y; s_extentsMax[2] = extents.max.z;
            s_pivot[0] = pivot.x; s_pivot[1] = pivot.y; s_pivot[2] = pivot.z;

            SpaceMouseNative.SM_PutView(s_affine, s_extentsMin, s_extentsMax, s_pivot,
                                        fieldOfViewRadians, perspective ? 1 : 0);
        }

        /// <summary>True while the puck is deflected.</summary>
        public static bool IsMoving => SpaceMouseNative.SM_IsMoving() != 0;
    }

    /// <summary>Something the 3D mouse can fly.</summary>
    /// <remarks>
    /// Register one to navigate a camera of your own. The driver asks the
    /// registered navigators in order of falling priority and stops at the first
    /// that takes the tick, so a navigator that is not currently applicable
    /// simply returns false and lets the scene view have it.
    /// </remarks>
    public interface ISpaceMouseNavigator
    {
        /// <summary>Higher goes first. The built in scene view sits at zero.</summary>
        int Priority { get; }

        /// <summary>Returns true when this navigator handled the tick.</summary>
        bool TryNavigate();
    }
}
