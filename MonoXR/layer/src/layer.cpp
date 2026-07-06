// XR_APILAYER_NOVELTY_monoxr
//
// An OpenXR API layer that appends quad composition layers fed by an external
// MonoGame process. Each overlay slot in the Local\MonoXR-Control shared memory
// points at a named shared D3D11 texture; every frame the layer copies that
// texture into an XrSwapchain image and submits an XrCompositionLayerQuad with
// the pose/size the client requested.
//
// Graphics API: D3D11 only in this version (XR_KHR_D3D11_enable).

#include <windows.h>
#include <d3d11_1.h>
#include <dxgi1_2.h>

#include <cstdio>
#include <cstring>
#include <cwchar>
#include <mutex>
#include <share.h>
#include <string>
#include <vector>

#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>
#include <openxr/openxr_loader_negotiation.h>

#include "monoxr_abi.h"

// ---------------------------------------------------------------------------
// Logging
//
// Verbose, step-by-step tracing. The log is appended (not truncated) so that
// multiple processes loading the layer (e.g. the OpenXR loader probing, then
// the game itself) all show up. Location, in order of preference:
//   1. %MONOXR_LOG%              (explicit override — a path we control)
//   2. %TEMP%\MonoXR\layer.log
//   3. C:\MonoXR\layer.log       (fallback if TEMP is unavailable)
// Every line is prefixed with a timestamp, the process id, and the process
// image name, so we can tell exactly which process loaded the layer.
// ---------------------------------------------------------------------------
static const char* ProcessName() {
    static char name[MAX_PATH] = {};
    if (!name[0]) {
        char full[MAX_PATH] = {};
        DWORD n = GetModuleFileNameA(nullptr, full, MAX_PATH);
        const char* base = full;
        for (DWORD i = 0; i < n; ++i) if (full[i] == '\\' || full[i] == '/') base = full + i + 1;
        strncpy_s(name, base, _TRUNCATE);
        if (!name[0]) strncpy_s(name, "?", _TRUNCATE);
    }
    return name;
}

static FILE* OpenLog() {
    // _fsopen with _SH_DENYNO so other processes (tail, editors) can read the
    // log while the game holds it open.
    char path[MAX_PATH];
    // 1. explicit override
    DWORD n = GetEnvironmentVariableA("MONOXR_LOG", path, MAX_PATH);
    if (n && n < MAX_PATH) {
        FILE* f = _fsopen(path, "a", _SH_DENYNO);
        if (f) return f;
    }
    // 2. %TEMP%\MonoXR\layer.log
    n = GetEnvironmentVariableA("TEMP", path, MAX_PATH);
    if (n && n < MAX_PATH) {
        std::string dir = std::string(path) + "\\MonoXR";
        CreateDirectoryA(dir.c_str(), nullptr);
        std::string file = dir + "\\layer.log";
        FILE* f = _fsopen(file.c_str(), "a", _SH_DENYNO);
        if (f) return f;
    }
    // 3. C:\MonoXR\layer.log
    CreateDirectoryA("C:\\MonoXR", nullptr);
    FILE* f = _fsopen("C:\\MonoXR\\layer.log", "a", _SH_DENYNO);
    if (f) return f;
    return nullptr;
}

static void LogLine(const char* fmt, ...) {
    char msg[1024];
    va_list ap;
    va_start(ap, fmt);
    _vsnprintf_s(msg, sizeof(msg), _TRUNCATE, fmt, ap);
    va_end(ap);

    SYSTEMTIME st;
    GetLocalTime(&st);
    char line[1200];
    _snprintf_s(line, sizeof(line), _TRUNCATE,
                "%02d:%02d:%02d.%03d [pid %lu %s] %s",
                st.wHour, st.wMinute, st.wSecond, st.wMilliseconds,
                GetCurrentProcessId(), ProcessName(), msg);

    OutputDebugStringA("[MonoXR] ");
    OutputDebugStringA(line);
    OutputDebugStringA("\n");

    static FILE* f = nullptr;
    static bool tried = false;
    if (!tried) { tried = true; f = OpenLog(); }
    if (f) { fprintf(f, "%s\n", line); fflush(f); }
}

// ---------------------------------------------------------------------------
// Down-chain dispatch table
// ---------------------------------------------------------------------------
static PFN_xrGetInstanceProcAddr        g_nextGipa = nullptr;

static PFN_xrCreateSession               down_xrCreateSession = nullptr;
static PFN_xrDestroySession              down_xrDestroySession = nullptr;
static PFN_xrEndFrame                    down_xrEndFrame = nullptr;
static PFN_xrCreateReferenceSpace        down_xrCreateReferenceSpace = nullptr;
static PFN_xrDestroySpace                down_xrDestroySpace = nullptr;
static PFN_xrEnumerateSwapchainFormats   down_xrEnumerateSwapchainFormats = nullptr;
static PFN_xrCreateSwapchain             down_xrCreateSwapchain = nullptr;
static PFN_xrDestroySwapchain            down_xrDestroySwapchain = nullptr;
static PFN_xrEnumerateSwapchainImages    down_xrEnumerateSwapchainImages = nullptr;
static PFN_xrAcquireSwapchainImage       down_xrAcquireSwapchainImage = nullptr;
static PFN_xrWaitSwapchainImage          down_xrWaitSwapchainImage = nullptr;
static PFN_xrReleaseSwapchainImage       down_xrReleaseSwapchainImage = nullptr;

// ---------------------------------------------------------------------------
// Per-overlay GPU resources, lazily built to match a control-block slot.
// ---------------------------------------------------------------------------
struct SlotResources {
    XrSwapchain swapchain = XR_NULL_HANDLE;
    std::vector<ID3D11Texture2D*> images;   // owned by runtime, do not release
    ID3D11Texture2D* sharedTex = nullptr;   // opened from client's named handle
    IDXGIKeyedMutex* mutex = nullptr;

    // cached to detect when the client changes the texture
    wchar_t  name[MONOXR_NAME_LEN] = {};
    uint32_t width = 0, height = 0, format = 0;
    uint64_t lastFrameIndex = ~0ull;

    void releaseShared() {
        if (mutex) { mutex->Release(); mutex = nullptr; }
        if (sharedTex) { sharedTex->Release(); sharedTex = nullptr; }
        name[0] = 0; width = height = format = 0; lastFrameIndex = ~0ull;
    }
};

struct SessionState {
    XrSession session = XR_NULL_HANDLE;
    ID3D11Device* device = nullptr;         // app's device (captured, not owned)
    ID3D11DeviceContext* context = nullptr; // app's immediate context (not owned)
    XrSpace worldSpace = XR_NULL_HANDLE;    // LOCAL
    XrSpace headSpace = XR_NULL_HANDLE;     // VIEW
    SlotResources slots[MONOXR_MAX_OVERLAYS];

    // Shared control block
    HANDLE mapping = nullptr;
    MonoXrControlBlock* control = nullptr;
    int openRetry = 0;
};

static std::mutex g_lock;
static SessionState g_session;   // single-session layer (typical for overlays)

// ---------------------------------------------------------------------------
// Control-block mapping (created by the client; we just open it)
// ---------------------------------------------------------------------------
static void TryOpenControl(SessionState& s) {
    if (s.control) return;
    if (s.openRetry-- > 0) return;      // throttle re-open attempts
    s.openRetry = 90;                   // ~ once per second at 90Hz

    HANDLE h = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, MONOXR_CONTROL_NAME);
    if (!h) return;
    void* view = MapViewOfFile(h, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(MonoXrControlBlock));
    if (!view) { CloseHandle(h); return; }

    auto* cb = reinterpret_cast<MonoXrControlBlock*>(view);
    if (cb->magic != MONOXR_MAGIC || cb->version != MONOXR_VERSION) {
        LogLine("control block magic/version mismatch (%08x/%u)", cb->magic, cb->version);
        UnmapViewOfFile(view); CloseHandle(h); return;
    }
    s.mapping = h;
    s.control = cb;
    s.control->layerPid = GetCurrentProcessId();
    s.control->layerActive = 1;
    LogLine("attached to MonoXR control block, client pid=%u", cb->clientPid);
}

static void CloseControl(SessionState& s) {
    if (s.control) { s.control->layerActive = 0; s.control->layerPid = 0; UnmapViewOfFile(s.control); s.control = nullptr; }
    if (s.mapping) { CloseHandle(s.mapping); s.mapping = nullptr; }
}

// ---------------------------------------------------------------------------
// Swapchain / shared-texture management for one slot
// ---------------------------------------------------------------------------
// Pick a swapchain format the runtime actually supports. The client renders
// R8G8B8A8_UNORM (28); some runtimes (e.g. SteamVR) only expose the sRGB
// variant (29) for the color path. Both are copy-compatible with UNORM data
// via CopyResource, so we fall back to sRGB, then to whatever the runtime
// lists first, rather than failing to create the swapchain (which would make
// the overlay silently invisible).
static int64_t ChooseSwapchainFormat(SessionState& s, uint32_t requested) {
    uint32_t count = 0;
    if (XR_FAILED(down_xrEnumerateSwapchainFormats(s.session, 0, &count, nullptr)) || count == 0)
        return static_cast<int64_t>(requested); // can't enumerate; try as-is
    std::vector<int64_t> formats(count);
    down_xrEnumerateSwapchainFormats(s.session, count, &count, formats.data());

    auto has = [&](int64_t f) {
        for (auto x : formats) if (x == f) return true;
        return false;
    };

    const int64_t DXGI_R8G8B8A8_UNORM = 28;
    const int64_t DXGI_R8G8B8A8_UNORM_SRGB = 29;

    if (has(static_cast<int64_t>(requested))) return static_cast<int64_t>(requested);
    if (requested == DXGI_R8G8B8A8_UNORM && has(DXGI_R8G8B8A8_UNORM_SRGB)) {
        LogLine("format %u unsupported; falling back to R8G8B8A8_UNORM_SRGB (29)", requested);
        return DXGI_R8G8B8A8_UNORM_SRGB;
    }
    LogLine("format %u unsupported; falling back to runtime format[0]=%lld (of %u offered)",
            requested, (long long)formats[0], count);
    return formats[0];
}

static bool EnsureSwapchain(SessionState& s, SlotResources& r,
                            const MonoXrOverlaySlot& slot) {
    if (r.swapchain != XR_NULL_HANDLE &&
        r.width == slot.texWidth && r.height == slot.texHeight && r.format == slot.format)
        return true;

    if (r.swapchain != XR_NULL_HANDLE) {
        down_xrDestroySwapchain(r.swapchain);
        r.swapchain = XR_NULL_HANDLE;
        r.images.clear();
    }

    int64_t chosenFormat = ChooseSwapchainFormat(s, slot.format);

    XrSwapchainCreateInfo ci{ XR_TYPE_SWAPCHAIN_CREATE_INFO };
    ci.usageFlags = XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT | XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT;
    ci.format = chosenFormat;
    ci.sampleCount = 1;
    ci.width = slot.texWidth;
    ci.height = slot.texHeight;
    ci.faceCount = 1;
    ci.arraySize = 1;
    ci.mipCount = 1;

    XrResult res = down_xrCreateSwapchain(s.session, &ci, &r.swapchain);
    if (XR_FAILED(res)) { LogLine("xrCreateSwapchain failed %d (fmt=%u)", res, slot.format); return false; }

    uint32_t count = 0;
    down_xrEnumerateSwapchainImages(r.swapchain, 0, &count, nullptr);
    std::vector<XrSwapchainImageD3D11KHR> imgs(count, { XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR });
    down_xrEnumerateSwapchainImages(r.swapchain, count, &count,
        reinterpret_cast<XrSwapchainImageBaseHeader*>(imgs.data()));
    r.images.clear();
    for (auto& i : imgs) r.images.push_back(i.texture);

    r.width = slot.texWidth; r.height = slot.texHeight; r.format = slot.format;
    LogLine("created swapchain %ux%u requestedFmt=%u actualFmt=%lld images=%u",
            r.width, r.height, r.format, (long long)chosenFormat, count);
    return true;
}

static bool EnsureSharedTexture(SessionState& s, SlotResources& r,
                                const MonoXrOverlaySlot& slot) {
    if (r.sharedTex && wcsncmp(r.name, slot.sharedName, MONOXR_NAME_LEN) == 0)
        return true;

    r.releaseShared();
    if (slot.sharedName[0] == 0) return false;

    ID3D11Device1* dev1 = nullptr;
    if (FAILED(s.device->QueryInterface(__uuidof(ID3D11Device1), (void**)&dev1)) || !dev1)
        return false;

    ID3D11Texture2D* tex = nullptr;
    HRESULT hr = dev1->OpenSharedResourceByName(slot.sharedName, DXGI_SHARED_RESOURCE_READ,
                                                __uuidof(ID3D11Texture2D), (void**)&tex);
    dev1->Release();
    if (FAILED(hr) || !tex) { LogLine("OpenSharedResourceByName failed hr=0x%08x", hr); return false; }

    IDXGIKeyedMutex* km = nullptr;
    tex->QueryInterface(__uuidof(IDXGIKeyedMutex), (void**)&km); // may be null if no keyed mutex

    r.sharedTex = tex;
    r.mutex = km;
    wcsncpy_s(r.name, slot.sharedName, MONOXR_NAME_LEN - 1);
    LogLine("opened shared texture '%ls' (keyedMutex=%d)", slot.sharedName, km ? 1 : 0);
    return true;
}

// Copy shared texture -> next swapchain image. Returns swapchain sub-image ready
// for a quad layer, or false to skip this slot.
static bool RenderSlot(SessionState& s, SlotResources& r, const MonoXrOverlaySlot& slot,
                       XrSwapchainSubImage& outSub) {
    if (!EnsureSwapchain(s, r, slot)) return false;
    if (!EnsureSharedTexture(s, r, slot)) return false;

    uint32_t idx = 0;
    XrSwapchainImageAcquireInfo ai{ XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO };
    if (XR_FAILED(down_xrAcquireSwapchainImage(r.swapchain, &ai, &idx))) return false;
    XrSwapchainImageWaitInfo wi{ XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO };
    wi.timeout = XR_INFINITE_DURATION;
    if (XR_FAILED(down_xrWaitSwapchainImage(r.swapchain, &wi))) {
        XrSwapchainImageReleaseInfo ri{ XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO };
        down_xrReleaseSwapchainImage(r.swapchain, &ri);
        return false;
    }

    // Producer/consumer handoff on the shared texture.
    bool locked = false;
    if (r.mutex) locked = SUCCEEDED(r.mutex->AcquireSync(MONOXR_KEY_CONSUMER, 16));
    // If there is no keyed mutex we copy unsynchronised (client should double-buffer).
    if (!r.mutex || locked) {
        s.context->CopyResource(r.images[idx], r.sharedTex);
        if (r.mutex) r.mutex->ReleaseSync(MONOXR_KEY_PRODUCER);
    }

    XrSwapchainImageReleaseInfo ri{ XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO };
    down_xrReleaseSwapchainImage(r.swapchain, &ri);

    if (r.mutex && !locked) return false; // couldn't sync this frame; skip cleanly

    outSub.swapchain = r.swapchain;
    outSub.imageArrayIndex = 0;
    outSub.imageRect = { {0,0}, { (int32_t)slot.texWidth, (int32_t)slot.texHeight } };
    return true;
}

// ---------------------------------------------------------------------------
// Hooked entry points
// ---------------------------------------------------------------------------
static XrResult XRAPI_CALL MonoXr_xrEndFrame(XrSession session, const XrFrameEndInfo* frameEndInfo) {
    std::lock_guard<std::mutex> guard(g_lock);
    SessionState& s = g_session;

    if (s.session != session || !s.device || !s.context)
        return down_xrEndFrame(session, frameEndInfo);

    TryOpenControl(s);
    if (!s.control)
        return down_xrEndFrame(session, frameEndInfo);

    // Re-assert liveness every frame so the client's view self-heals even if
    // something cleared these (e.g. a client that wrongly decided we died).
    s.control->layerActive = 1;
    s.control->layerPid = GetCurrentProcessId();

    // Storage must outlive the down-call; reserve so pointers stay stable.
    static std::vector<XrCompositionLayerQuad> quads;
    quads.clear();
    quads.reserve(MONOXR_MAX_OVERLAYS);
    std::vector<const XrCompositionLayerBaseHeader*> layers;

    // Keep the app's own layers first (world renders under the overlays).
    for (uint32_t i = 0; i < frameEndInfo->layerCount; ++i)
        layers.push_back(frameEndInfo->layers[i]);

    for (uint32_t i = 0; i < MONOXR_MAX_OVERLAYS; ++i) {
        MonoXrOverlaySlot slot = s.control->slots[i]; // copy snapshot
        SlotResources& r = s.slots[i];

        if (!slot.active || !slot.visible) {
            if (r.swapchain != XR_NULL_HANDLE) { /* keep for reuse */ }
            continue;
        }
        if (slot.texWidth == 0 || slot.texHeight == 0) continue;

        XrSwapchainSubImage sub{};
        if (!RenderSlot(s, r, slot, sub)) continue;

        XrCompositionLayerQuad q{ XR_TYPE_COMPOSITION_LAYER_QUAD };
        q.layerFlags = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT |
                       XR_COMPOSITION_LAYER_UNPREMULTIPLIED_ALPHA_BIT;
        q.space = (slot.space == MONOXR_SPACE_HEAD) ? s.headSpace : s.worldSpace;
        q.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
        q.subImage = sub;
        q.pose.position = { slot.posX, slot.posY, slot.posZ };
        q.pose.orientation = { slot.quatX, slot.quatY, slot.quatZ, slot.quatW };
        q.size = { slot.sizeX, slot.sizeY };

        quads.push_back(q);
        layers.push_back(reinterpret_cast<const XrCompositionLayerBaseHeader*>(&quads.back()));
    }

    XrFrameEndInfo patched = *frameEndInfo;
    patched.layerCount = (uint32_t)layers.size();
    patched.layers = layers.data();
    XrResult efRes = down_xrEndFrame(session, &patched);

    // Throttled visibility: report once a second what we're submitting (and the
    // exact pose/size/space of each quad + the compositor's result), so a
    // "quad submitted but nothing in VR" case shows whether it's mis-posed,
    // zero-sized, in the wrong space, or rejected by the runtime.
    static uint64_t frameCount = 0;
    if ((frameCount++ % 90) == 0) {
        LogLine("endFrame: appManaged=%u quads=%zu totalLayers=%zu down_xrEndFrame=%d",
                frameEndInfo->layerCount, quads.size(), layers.size(), efRes);
        for (size_t qi = 0; qi < quads.size(); ++qi) {
            const auto& q = quads[qi];
            const char* sp = (q.space == s.headSpace) ? "HEAD"
                           : (q.space == s.worldSpace) ? "WORLD" : "?";
            LogLine("  quad[%zu] space=%s pos=(%.2f,%.2f,%.2f) size=(%.2f,%.2f) "
                    "quat=(%.2f,%.2f,%.2f,%.2f) flags=0x%llx",
                    qi, sp, q.pose.position.x, q.pose.position.y, q.pose.position.z,
                    q.size.width, q.size.height,
                    q.pose.orientation.x, q.pose.orientation.y,
                    q.pose.orientation.z, q.pose.orientation.w,
                    (unsigned long long)q.layerFlags);
        }
    }

    return efRes;
}

static XrResult XRAPI_CALL MonoXr_xrCreateSession(XrInstance instance,
                                                  const XrSessionCreateInfo* createInfo,
                                                  XrSession* session) {
    // Log the graphics binding type in the next chain so we can see whether the
    // app is D3D11 (what we support) or something else (D3D12/Vulkan/OpenGL).
    int firstNextType = -1;
    if (createInfo && createInfo->next)
        firstNextType = (int)((const XrBaseInStructure*)createInfo->next)->type;
    LogLine("xrCreateSession called; createFlags=0x%llx next[0].type=%d",
            createInfo ? (unsigned long long)createInfo->createFlags : 0ull, firstNextType);

    XrResult res = down_xrCreateSession(instance, createInfo, session);
    LogLine("  down_xrCreateSession returned %d", res);
    if (XR_FAILED(res)) return res;

    std::lock_guard<std::mutex> guard(g_lock);
    SessionState& s = g_session;
    s = SessionState{};
    s.session = *session;

    // Find the D3D11 graphics binding in the next chain.
    for (const XrBaseInStructure* b = (const XrBaseInStructure*)createInfo->next; b; b = b->next) {
        if (b->type == XR_TYPE_GRAPHICS_BINDING_D3D11_KHR) {
            auto* g = reinterpret_cast<const XrGraphicsBindingD3D11KHR*>(b);
            s.device = g->device;
            if (s.device) s.device->GetImmediateContext(&s.context);
            LogLine("captured D3D11 device %p", (void*)s.device);
            break;
        }
    }
    if (!s.device) LogLine("WARNING: no D3D11 graphics binding found; overlays disabled for this session");

    // Reference spaces for world- and head-locked overlays.
    XrReferenceSpaceCreateInfo rs{ XR_TYPE_REFERENCE_SPACE_CREATE_INFO };
    rs.poseInReferenceSpace.orientation.w = 1.0f;
    rs.referenceSpaceType = XR_REFERENCE_SPACE_TYPE_LOCAL;
    down_xrCreateReferenceSpace(*session, &rs, &s.worldSpace);
    rs.referenceSpaceType = XR_REFERENCE_SPACE_TYPE_VIEW;
    down_xrCreateReferenceSpace(*session, &rs, &s.headSpace);

    TryOpenControl(s);
    return res;
}

static XrResult XRAPI_CALL MonoXr_xrDestroySession(XrSession session) {
    std::lock_guard<std::mutex> guard(g_lock);
    SessionState& s = g_session;
    if (s.session == session) {
        for (auto& r : s.slots) {
            if (r.swapchain != XR_NULL_HANDLE) down_xrDestroySwapchain(r.swapchain);
            r.releaseShared();
        }
        if (s.worldSpace) down_xrDestroySpace(s.worldSpace);
        if (s.headSpace) down_xrDestroySpace(s.headSpace);
        if (s.context) s.context->Release();
        CloseControl(s);
        s = SessionState{};
    }
    return down_xrDestroySession(session);
}

// ---------------------------------------------------------------------------
// gipa: hand out our wrappers, defer everything else down-chain
// ---------------------------------------------------------------------------
static XrResult XRAPI_CALL MonoXr_xrGetInstanceProcAddr(XrInstance instance, const char* name,
                                                        PFN_xrVoidFunction* function) {
    auto give = [&](void* f) { *function = reinterpret_cast<PFN_xrVoidFunction>(f); return XR_SUCCESS; };
    if (!name || !function) return XR_ERROR_VALIDATION_FAILURE;

    // Log each distinct function the app resolves, once, so we can see whether
    // the app ever asks for session/frame entry points (i.e. whether it renders
    // through this instance at all).
    {
        static std::mutex seenLock;
        static std::vector<std::string> seen;
        std::lock_guard<std::mutex> g(seenLock);
        bool isNew = true;
        for (auto& n : seen) if (n == name) { isNew = false; break; }
        if (isNew) { seen.emplace_back(name); LogLine("gipa request: %s", name); }
    }

    if (strcmp(name, "xrCreateSession") == 0)  return give((void*)MonoXr_xrCreateSession);
    if (strcmp(name, "xrDestroySession") == 0) return give((void*)MonoXr_xrDestroySession);
    if (strcmp(name, "xrEndFrame") == 0)       return give((void*)MonoXr_xrEndFrame);

    if (g_nextGipa) return g_nextGipa(instance, name, function);
    *function = nullptr;
    return XR_ERROR_FUNCTION_UNSUPPORTED;
}

// ---------------------------------------------------------------------------
// Layer instance creation: descend the chain and resolve our dispatch table
// ---------------------------------------------------------------------------
static XrResult XRAPI_CALL MonoXr_xrCreateApiLayerInstance(const XrInstanceCreateInfo* info,
                                                           const XrApiLayerCreateInfo* apiLayerInfo,
                                                           XrInstance* instance) {
    LogLine("xrCreateApiLayerInstance called (app='%s')",
            (info && info->applicationInfo.applicationName[0]) ? info->applicationInfo.applicationName : "?");
    if (!apiLayerInfo || !apiLayerInfo->nextInfo) {
        LogLine("  -> FAIL: null apiLayerInfo/nextInfo");
        return XR_ERROR_INITIALIZATION_FAILED;
    }

    PFN_xrGetInstanceProcAddr nextGipa = apiLayerInfo->nextInfo->nextGetInstanceProcAddr;
    PFN_xrCreateApiLayerInstance nextCreate = apiLayerInfo->nextInfo->nextCreateApiLayerInstance;

    XrApiLayerCreateInfo local = *apiLayerInfo;
    local.nextInfo = apiLayerInfo->nextInfo->next; // advance to the next layer

    XrResult res = nextCreate(info, &local, instance);
    LogLine("  chained nextCreateApiLayerInstance returned %d", res);
    if (XR_FAILED(res)) return res;

    g_nextGipa = nextGipa;

    #define RESOLVE(fn) nextGipa(*instance, #fn, (PFN_xrVoidFunction*)&down_##fn)
    RESOLVE(xrCreateSession);
    RESOLVE(xrDestroySession);
    RESOLVE(xrEndFrame);
    RESOLVE(xrCreateReferenceSpace);
    RESOLVE(xrDestroySpace);
    RESOLVE(xrEnumerateSwapchainFormats);
    RESOLVE(xrCreateSwapchain);
    RESOLVE(xrDestroySwapchain);
    RESOLVE(xrEnumerateSwapchainImages);
    RESOLVE(xrAcquireSwapchainImage);
    RESOLVE(xrWaitSwapchainImage);
    RESOLVE(xrReleaseSwapchainImage);
    #undef RESOLVE

    LogLine("xrCreateApiLayerInstance ok; endFrame=%p session=%p",
            (void*)down_xrEndFrame, (void*)down_xrCreateSession);
    return XR_SUCCESS;
}

// ---------------------------------------------------------------------------
// Loader negotiation entry point
// ---------------------------------------------------------------------------
extern "C" __declspec(dllexport) XrResult XRAPI_CALL
xrNegotiateLoaderApiLayerInterface(const XrNegotiateLoaderInfo* loaderInfo,
                                   const char* layerName,
                                   XrNegotiateApiLayerRequest* apiLayerRequest) {
    LogLine("xrNegotiateLoaderApiLayerInterface called (layerName=%s)",
            layerName ? layerName : "(null)");

    if (!loaderInfo || !apiLayerRequest) {
        LogLine("  -> FAIL: null loaderInfo/apiLayerRequest");
        return XR_ERROR_INITIALIZATION_FAILED;
    }
    LogLine("  loaderInfo: structType=%d version=%u minInterface=%u maxInterface=%u minApi=0x%llx maxApi=0x%llx",
            (int)loaderInfo->structType, loaderInfo->structVersion,
            loaderInfo->minInterfaceVersion, loaderInfo->maxInterfaceVersion,
            (unsigned long long)loaderInfo->minApiVersion,
            (unsigned long long)loaderInfo->maxApiVersion);
    LogLine("  apiLayerRequest: structType=%d version=%u",
            (int)apiLayerRequest->structType, apiLayerRequest->structVersion);
    LogLine("  our XR_CURRENT_LOADER_API_LAYER_VERSION=%u XR_CURRENT_API_VERSION=0x%llx",
            XR_CURRENT_LOADER_API_LAYER_VERSION,
            (unsigned long long)XR_CURRENT_API_VERSION);

    if (loaderInfo->structType != XR_LOADER_INTERFACE_STRUCT_LOADER_INFO ||
        apiLayerRequest->structType != XR_LOADER_INTERFACE_STRUCT_API_LAYER_REQUEST) {
        LogLine("  -> FAIL: struct type mismatch");
        return XR_ERROR_INITIALIZATION_FAILED;
    }

    apiLayerRequest->layerInterfaceVersion = XR_CURRENT_LOADER_API_LAYER_VERSION;
    apiLayerRequest->layerApiVersion = XR_CURRENT_API_VERSION;
    apiLayerRequest->getInstanceProcAddr = MonoXr_xrGetInstanceProcAddr;
    apiLayerRequest->createApiLayerInstance = MonoXr_xrCreateApiLayerInstance;

    LogLine("xrNegotiateLoaderApiLayerInterface ok -> layer accepted");
    return XR_SUCCESS;
}

// ---------------------------------------------------------------------------
// DllMain: earliest possible proof that the loader mapped our DLL into a
// process. If this line never appears for the game process, the loader never
// even found/loaded the layer (registration / arch / manifest problem), as
// opposed to a negotiation or session problem.
// ---------------------------------------------------------------------------
BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH)
        LogLine("DllMain DLL_PROCESS_ATTACH — MonoXR layer DLL mapped into process");
    return TRUE;
}
