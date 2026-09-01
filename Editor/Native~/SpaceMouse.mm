// Native bridge from the 3Dconnexion driver to the Unity editor.
//
// A working connection under 3DxWare on macOS needs three things at once, and
// leaving out any one of them fails silently — no error, no data:
//
//   1. A bundled application. The driver identifies its clients by bundle, so a
//      plain command line process is never served.
//   2. The classic ConnexionClient registration, which is what makes the driver
//      know us as an application at all. Fusion 360 does exactly this, and its
//      entry in ~/Library/Preferences/3Dconnexion/Applications shows it.
//   3. A navlib connection, which carries the actual navigation. navlib asks us
//      for the camera through view.affine and writes the moved camera back the
//      same way, so the driver — not us — owns the navigation feel.
//
// The Unity editor satisfies (1) on its own. This plugin adds (2) and (3).
//
// Threading: navlib is created single threaded, so its callbacks arrive on the
// run loop of the thread that called NlCreate — the editor main thread. The
// mutex still guards the shared state, because the managed side reads and
// writes it from the same thread but at unrelated moments.

#import <Foundation/Foundation.h>
#include <cstddef>
#include <cwchar>
#ifndef __cdecl
#define __cdecl
#endif
#include <3DconnexionNavlib/navlib.h>
#include <3DconnexionClient/ConnexionClientAPI.h>
#include <cstring>
#include <mutex>
#include <string>

using namespace navlib;

namespace {

struct ViewState {
    double affine[16];      // camera to world, row vectors: translation in 12..14
    double extentsMin[3];
    double extentsMax[3];
    double pivot[3];
    double fov;             // radians
    int    perspective;
};

std::mutex  g_lock;
ViewState   g_view          = {};
bool        g_viewDirty     = false;   // navlib moved the camera, the editor has not picked it up yet
bool        g_moving        = false;
int         g_affineWrites  = 0;
nlHandle_t  g_nav           = 0;
uint16_t    g_client        = 0;
bool        g_open          = false;
bool        g_leftHanded    = true;
std::string g_error;

void SetIdentity(double m[16]) {
    memset(m, 0, sizeof(double) * 16);
    m[0] = m[5] = m[10] = m[15] = 1.0;
}

void ToMatrix(const double src[16], matrix_t &dst) { memcpy(&dst.m00, src, sizeof(double) * 16); }
void FromMatrix(const matrix_t &src, double dst[16]) { memcpy(dst, &src.m00, sizeof(double) * 16); }

// navlib works in a right handed system, Unity is left handed. Declaring the
// difference here would let navlib convert, so that every matrix we exchange
// stays in plain Unity coordinates.
void HostCoordinateSystem(matrix_t &m) {
    memset(&m.m00, 0, sizeof(double) * 16);
    m.m00 = 1.0; m.m11 = 1.0; m.m33 = 1.0;
    m.m22 = g_leftHanded ? -1.0 : 1.0;
}

long __cdecl GetProperty(const param_t, const property_t name, value_t *value) {
    std::lock_guard<std::mutex> guard(g_lock);
    const std::string n(name);

    if (n == "view.affine")      { matrix_t m; ToMatrix(g_view.affine, m); *value = m; return 0; }
    if (n == "coordinateSystem") { matrix_t m; HostCoordinateSystem(m);    *value = m; return 0; }
    if (n == "views.front")      { double f[16]; SetIdentity(f); matrix_t m; ToMatrix(f, m); *value = m; return 0; }
    if (n == "view.perspective") { *value = g_view.perspective != 0; return 0; }
    if (n == "view.rotatable")   { *value = true;  return 0; }
    if (n == "view.fov")         { *value = g_view.fov; return 0; }
    if (n == "selection.empty")  { *value = true;  return 0; }
    if (n == "model.extents") {
        box_t b;
        b.min.x = g_view.extentsMin[0]; b.min.y = g_view.extentsMin[1]; b.min.z = g_view.extentsMin[2];
        b.max.x = g_view.extentsMax[0]; b.max.y = g_view.extentsMax[1]; b.max.z = g_view.extentsMax[2];
        *value = b;
        return 0;
    }
    if (n == "pivot.position" || n == "hit.lookfrom") {
        point_t p; p.x = g_view.pivot[0]; p.y = g_view.pivot[1]; p.z = g_view.pivot[2];
        *value = p;
        return 0;
    }
    return make_result_code(navlib_errc::property_not_found);
}

long __cdecl SetProperty(const param_t, const property_t name, const value_t *value) {
    std::lock_guard<std::mutex> guard(g_lock);
    const std::string n(name);

    if (n == "view.affine") {
        FromMatrix(value->matrix, g_view.affine);
        g_viewDirty = true;
        g_affineWrites++;
        return 0;
    }
    if (n == "motion") { g_moving = value->b != 0; return 0; }
    return 0;   // everything else is accepted and ignored on purpose
}

void __cdecl DeviceAdded(unsigned int)   {}
void __cdecl DeviceRemoved(unsigned int) {}

const char *kProperties[] = {
    "view.affine", "view.perspective", "view.rotatable", "view.fov", "view.extents",
    "view.target", "view.frustum", "views.front", "coordinateSystem",
    "model.extents", "pivot.position", "pivot.visible", "pivot.user",
    "selection.empty", "selection.affine", "selection.extents",
    "hit.lookfrom", "hit.direction", "hit.aperture", "hit.lookat",
    "motion", "transaction", "settings.changed", "frame.time", "focus", "device.present",
};
const size_t kPropertyCount = sizeof(kProperties) / sizeof(kProperties[0]);
accessor_t   g_accessors[kPropertyCount];

} // namespace

extern "C" {

/// Opens the connection. Safe to call again; a second call is a no-op.
/// leftHanded declares the host coordinate system to navlib.
/// Returns 0 on success, otherwise a navlib error code.
int SM_Open(const char *profileName, int leftHanded) {
    if (g_open) return 0;
    g_error.clear();
    g_leftHanded = leftHanded != 0;

    {
        std::lock_guard<std::mutex> guard(g_lock);
        SetIdentity(g_view.affine);
        g_view.extentsMin[0] = g_view.extentsMin[1] = g_view.extentsMin[2] = -1.0;
        g_view.extentsMax[0] = g_view.extentsMax[1] = g_view.extentsMax[2] =  1.0;
        g_view.pivot[0] = g_view.pivot[1] = g_view.pivot[2] = 0.0;
        g_view.fov = 1.047;                 // 60 degrees, replaced by the editor at once
        g_view.perspective = 1;
        g_viewDirty = false;
        g_moving = false;
        g_affineWrites = 0;
    }

    // Step 2: make the driver aware of us as an application.
    SetConnexionHandlers(nullptr, DeviceAdded, DeviceRemoved, false);
    uint8_t name[] = {5, 'U', 'n', 'i', 't', 'y'};      // Pascal string, as the old API wants
    g_client = RegisterConnexionClient('UNTY', name, kConnexionClientModeTakeOver, kConnexionMaskAll);

    // Step 3: the navigation itself.
    for (size_t i = 0; i < kPropertyCount; i++) {
        g_accessors[i].name  = kProperties[i];
        g_accessors[i].fnGet = GetProperty;
        g_accessors[i].fnSet = SetProperty;
        g_accessors[i].param = 0;
    }

    nlCreateOptions_t options;
    memset(&options, 0, sizeof(options));
    options.size = sizeof(options);
    options.bMultiThreaded = false;
    options.options = none;

    const long err = NlCreate(&g_nav, profileName ? profileName : "Unity",
                              g_accessors, kPropertyCount, &options);
    if (err != 0) {
        g_error = "NlCreate failed";
        UnregisterConnexionClient(g_client);
        CleanupConnexionHandlers();
        return (int)err;
    }

    value_t active(true);
    NlWriteValue(g_nav, active_k, &active);
    g_open = true;
    return 0;
}

void SM_Close(void) {
    if (!g_open) return;
    NlClose(g_nav);
    g_nav = 0;
    UnregisterConnexionClient(g_client);
    CleanupConnexionHandlers();
    g_open = false;
}

/// Tells the driver whether the editor currently owns the 3D mouse. Without
/// this the driver has no reason to route the device to us: active_k only picks
/// between our own connections, focus_k is what actually points the mouse here.
void SM_SetFocus(int hasFocus) {
    if (!g_open) return;
    value_t focus(hasFocus != 0);
    NlWriteValue(g_nav, focus_k, &focus);
}

/// The editor states where the camera is now, so navlib moves on from the real
/// view even when it was last changed by hand.
void SM_PutView(const double affine[16], const double extentsMin[3], const double extentsMax[3],
                const double pivot[3], double fovRadians, int perspective) {
    std::lock_guard<std::mutex> guard(g_lock);
    memcpy(g_view.affine, affine, sizeof(double) * 16);
    memcpy(g_view.extentsMin, extentsMin, sizeof(double) * 3);
    memcpy(g_view.extentsMax, extentsMax, sizeof(double) * 3);
    memcpy(g_view.pivot, pivot, sizeof(double) * 3);
    g_view.fov = fovRadians;
    g_view.perspective = perspective;
    g_viewDirty = false;
}

/// Hands over a camera the driver moved. Returns 1 when there was one.
int SM_TakeView(double affineOut[16]) {
    std::lock_guard<std::mutex> guard(g_lock);
    if (!g_viewDirty) return 0;
    memcpy(affineOut, g_view.affine, sizeof(double) * 16);
    g_viewDirty = false;
    return 1;
}

int SM_IsMoving(void)     { std::lock_guard<std::mutex> guard(g_lock); return g_moving ? 1 : 0; }
int SM_AffineWrites(void) { std::lock_guard<std::mutex> guard(g_lock); return g_affineWrites; }
int SM_IsOpen(void)       { return g_open ? 1 : 0; }

} // extern "C"
