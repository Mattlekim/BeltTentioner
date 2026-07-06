// MonoXR shared ABI — single source of truth for the cross-process contract
// between the MonoGame client (MonoXR.Client, C#) and the OpenXR API layer
// (XR_APILAYER_NOVELTY_monoxr, C++).
//
// The C# mirror of this file is shared/MonoXrAbi.cs. If you change one, change
// the other. Layout is Pack=4 and must match byte-for-byte.
#ifndef MONOXR_ABI_H
#define MONOXR_ABI_H

#include <stdint.h>

// Named kernel objects (Local\ = per-session namespace, good enough since the
// game and the MonoGame client run in the same interactive session).
#define MONOXR_CONTROL_NAME L"Local\\MonoXR-Control"

#define MONOXR_MAGIC        0x4D584F52u   // 'MXOR'
#define MONOXR_VERSION      1u
#define MONOXR_MAX_OVERLAYS 16u
#define MONOXR_NAME_LEN     64u           // wchar_t count incl. null

// Reference space an overlay's pose is expressed in.
enum MonoXrSpace {
    MONOXR_SPACE_WORLD = 0,  // stationary in the play space (XR_REFERENCE_SPACE_TYPE_LOCAL)
    MONOXR_SPACE_HEAD  = 1,  // locked to the headset (XR_REFERENCE_SPACE_TYPE_VIEW)
};

#pragma pack(push, 4)

// One overlay = one MonoGame RenderTarget published as a shared texture.
struct MonoXrOverlaySlot {
    uint32_t active;    // client owns this slot
    uint32_t visible;   // draw it this frame
    uint32_t space;     // MonoXrSpace
    uint32_t texWidth;
    uint32_t texHeight;
    uint32_t format;    // DXGI_FORMAT of the shared texture & requested swapchain

    // Pose of the quad centre, in the chosen reference space (meters / quaternion).
    float posX, posY, posZ;
    float quatX, quatY, quatZ, quatW;

    // Quad size in meters (world) or in meters at 1m (head-locked).
    float sizeX, sizeY;

    // Sort order; higher draws on top. Also used to keep a stable layer order.
    int32_t zOrder;

    uint32_t _pad0;

    // Bumped by the client every time a fresh frame is copied into the shared
    // texture. The layer copies only when this changes (cheap idle path).
    uint64_t frameIndex;

    // Name passed to IDXGIResource1::CreateSharedHandle on the client side and to
    // ID3D11Device1::OpenSharedResourceByName on the layer side. Empty => no texture yet.
    wchar_t sharedName[MONOXR_NAME_LEN];
};

struct MonoXrControlBlock {
    uint32_t magic;         // MONOXR_MAGIC
    uint32_t version;       // MONOXR_VERSION
    uint32_t maxOverlays;   // MONOXR_MAX_OVERLAYS
    uint32_t clientPid;     // 0 when no client attached
    uint64_t clientHeartbeat;   // client bumps periodically; layer can detect death
    uint32_t layerActive;   // layer sets 1 while a session is running
    uint32_t layerPid;      // game's process id while layerActive; lets the client
                            // detect a game that died without clearing layerActive
    struct MonoXrOverlaySlot slots[MONOXR_MAX_OVERLAYS];
};

#pragma pack(pop)

// Keyed-mutex handshake on each shared texture:
//   producer (client) acquires KEY_PRODUCER, copies RT -> shared, releases KEY_CONSUMER
//   consumer (layer)  acquires KEY_CONSUMER, copies shared -> swapchain, releases KEY_PRODUCER
// Initial owner is the producer (created acquired at KEY_PRODUCER).
#define MONOXR_KEY_PRODUCER 0ull
#define MONOXR_KEY_CONSUMER 1ull

#endif // MONOXR_ABI_H
