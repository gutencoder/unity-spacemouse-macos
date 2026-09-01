# SpaceMouse for macOS

Navigate the Unity editor with a 3Dconnexion SpaceMouse on macOS.

Unity ships no support for these devices — in Unity 6000.5 the string
"SpaceMouse" does not occur anywhere in the editor bundle — and 3Dconnexion
ships a plugin for Unreal but not for Unity. This package fills that gap.

- Fly the **scene view** the way you fly a model in Fusion 360 or SolidWorks.
- Fly the **simulated AR camera** in play mode, when AR Foundation is present.
- Register **your own camera** through a small interface.

The driver does the navigation itself. Speed, motion model and pivot behaviour
come from the 3Dconnexion panel, where you already tuned them, rather than being
reinvented here.

## Requirements

| | |
|---|---|
| Platform | macOS, Apple silicon or Intel |
| Unity | 6000.0 or newer |
| Driver | 3DxWare installed and running |

The package contains no 3Dconnexion code. It links the frameworks that 3DxWare
installs into `/Library/Frameworks`, so the driver has to be present — which it
is anyway, or the device would not work at all.

## Install

Package Manager → Add package from git URL:

```
https://github.com/gutencoder/unity-spacemouse-macos.git
```

There is nothing to configure. The connection opens by itself and follows the
editor's focus, so the device goes back to your CAD application the moment you
switch away.

Speed lives in **Preferences → SpaceMouse**, along with an on/off switch.

## The three ingredients

This is the part worth writing down, because getting it wrong costs a day.

A 3Dconnexion device on macOS delivers motion data only when **all three** of
these hold at once. Miss one and nothing arrives — no error, no warning, no
callback:

1. **The client is a bundled application.** The driver identifies its clients by
   bundle. A command line tool is never served, whatever it does otherwise.
2. **The client registers through the old ConnexionClient API.** This is what
   makes the driver know it as an application in the first place. You can see the
   result in `~/Library/Preferences/3Dconnexion/Applications`, where every
   application that works has an entry with a bundle id and a process id.
3. **The client runs a navlib connection** and sets both `active` and `focus`.
   `active` only picks between several connections of your own; `focus` is what
   actually points the device at you.

Fusion 360 does all three, which is why it works.

The failure is silent in a way that misleads. With only navlib, `NlCreate`
returns success and a valid handle, the driver creates
`~/Library/Preferences/3Dconnexion/navlib/<App>.user.config`, and it even pushes
`settings.changed` at you — while never once asking for a camera property. It
looks connected because it is connected. It simply is not the navigation target.

One red herring worth naming: `RegisterConnexionClient` returns the client id
`0xDEAF` (57007). That looks like a sentinel for a failed registration and it is
not — a working connection returns it too.

## Coordinates

navlib works in a right handed system, Unity is left handed. The plugin declares
the difference through the `coordinateSystem` property, so every matrix crossing
the boundary stays in plain Unity coordinates.

Declaring the identity instead does not merely mirror the controls. The driver's
model of the camera and the editor's disagree, each frame compounds the error,
and the view accelerates away — measured here to a camera position of 10^15
within thirty seconds.

## Speed

navlib hands over finished camera poses; there is no speed to ask it for. The
**Speed** setting is the share of each offered movement that the editor follows.
The damped pose is reported straight back, so the driver carries on from where
the camera really is: the whole path scales, and nothing drifts apart.

It sits on top of the per-application speed in the 3Dconnexion panel. If the
lowest setting here is still too fast, the panel is the right place to look.

## Driving your own camera

```csharp
using Gutenbrook.SpaceMouse;

internal sealed class MyNavigator : ISpaceMouseNavigator
{
    public int Priority => 50;   // above the scene view, below the AR simulation

    public bool TryNavigate()
    {
        if (!MyView.IsOpen) return false;      // let someone else have the tick

        var position = MyView.CameraPosition;
        var rotation = MyView.CameraRotation;

        if (SpaceMouseCamera.TryTakePose(position, rotation, out var moved, out var movedRotation))
        {
            position = moved;
            rotation = movedRotation;
            MyView.SetCamera(position, rotation);
        }

        SpaceMouseCamera.ReportPose(position, rotation, MyView.Pivot, MyView.FieldOfViewRadians, true);
        return true;
    }
}

[InitializeOnLoadMethod]
private static void Register() => SpaceMouseDriver.Register(new MyNavigator());
```

Report every tick, not only after a move — that is what keeps the driver and the
editor agreeing on where the camera is, so moving the view by hand does not throw
the next push off.

## Building the native bridge

A prebuilt universal bundle ships with the package. To rebuild it:

```
sh Editor/Native~/build.sh
```

Needs the Xcode command line tools and an installed 3DxWare, whose frameworks
supply the headers.

## Limitations

- macOS only. The three ingredients above are a macOS story; Windows works
  differently and is not implemented.
- In play mode with XR Simulation, AR Foundation rebuilds the camera rotation
  from yaw and pitch alone and clamps the pitch, so a roll of the puck is dropped.
  That is the package's own behaviour, not this one's.
- The world extents that scale the driver's speed are taken from the renderer
  bounds of the open scenes, refreshed every two seconds.

## Licence

MIT, see [LICENSE.md](LICENSE.md). This covers the code in this repository only.
The 3Dconnexion frameworks it links against are 3Dconnexion's and are governed by
their own agreement.
