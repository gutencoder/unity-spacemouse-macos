using System.Runtime.InteropServices;
using UnityEngine;

namespace Gutenbrook.SpaceMouse
{
    /// <summary>Raw entry points of the native bridge.</summary>
    /// <remarks>
    /// Deliberately internal. Everything a host needs is on
    /// <see cref="SpaceMouseCamera"/>; the marshalling stays in here so the
    /// public surface does not harden around a native layout.
    ///
    /// Poses cross the boundary as a plain 16 double array: a camera to world
    /// matrix in row vector order, so rows 0..2 are right, up and forward, and
    /// row 3 is the position. That is what navlib expects and it keeps this side
    /// free of transposing back and forth.
    /// </remarks>
    internal static class SpaceMouseNative
    {
        private const string Plugin = "SpaceMouse";

        [DllImport(Plugin)] internal static extern int  SM_Open(string profileName, int leftHanded);
        [DllImport(Plugin)] internal static extern void SM_Close();
        [DllImport(Plugin)] internal static extern void SM_SetFocus(int hasFocus);
        [DllImport(Plugin)] internal static extern void SM_PutView(
            double[] affine, double[] extentsMin, double[] extentsMax, double[] pivot,
            double fovRadians, int perspective);
        [DllImport(Plugin)] internal static extern int  SM_TakeView(double[] affine);
        [DllImport(Plugin)] internal static extern int  SM_IsMoving();
        [DllImport(Plugin)] internal static extern int  SM_AffineWrites();
        [DllImport(Plugin)] internal static extern int  SM_IsOpen();

        /// <summary>Writes a pose into the 16 double layout the driver expects.</summary>
        internal static void Pack(Vector3 position, Quaternion rotation, double[] affine)
        {
            var right   = rotation * Vector3.right;
            var up      = rotation * Vector3.up;
            var forward = rotation * Vector3.forward;

            affine[0]  = right.x;    affine[1]  = right.y;    affine[2]  = right.z;    affine[3]  = 0.0;
            affine[4]  = up.x;       affine[5]  = up.y;       affine[6]  = up.z;       affine[7]  = 0.0;
            affine[8]  = forward.x;  affine[9]  = forward.y;  affine[10] = forward.z;  affine[11] = 0.0;
            affine[12] = position.x; affine[13] = position.y; affine[14] = position.z; affine[15] = 1.0;
        }

        /// <summary>Reads a pose the driver moved back out of the same layout.</summary>
        internal static void Unpack(double[] affine, out Vector3 position, out Quaternion rotation)
        {
            var up      = new Vector3((float)affine[4], (float)affine[5],  (float)affine[6]);
            var forward = new Vector3((float)affine[8], (float)affine[9],  (float)affine[10]);
            position    = new Vector3((float)affine[12], (float)affine[13], (float)affine[14]);

            // A driver that is still settling can hand over a degenerate frame.
            // Falling back keeps a single bad frame from destroying the view.
            rotation = forward.sqrMagnitude > 1e-12f && up.sqrMagnitude > 1e-12f
                ? Quaternion.LookRotation(forward, up)
                : Quaternion.identity;
        }
    }
}
