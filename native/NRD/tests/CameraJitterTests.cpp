#include "CameraJitter.h"
#include <cstdio>
#include <cstring>
#include <initializer_list>
#include <stdexcept>

namespace {
void Require(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
}
struct Instance {
    nrd::Instance* value = nullptr;
    explicit Instance(nrd::Denoiser denoiser) {
        nrd::DenoiserDesc method{1, denoiser};
        nrd::InstanceCreationDesc desc{}; desc.denoisers = &method; desc.denoisersNum = 1;
        Require(nrd::CreateInstance(desc, value) == nrd::Result::SUCCESS, "CreateInstance");
    }
    ~Instance() { if (value) nrd::DestroyInstance(*value); }
};
void Dispatch(Instance& instance, const nrd::CommonSettings& settings,
    const nrd::DispatchDesc*& dispatches, uint32_t& count) {
    Require(nrd::SetCommonSettings(*instance.value, settings) == nrd::Result::SUCCESS, "SetCommonSettings");
    nrd::Identifier id = 1;
    Require(nrd::GetComputeDispatches(*instance.value, &id, 1, dispatches, count) == nrd::Result::SUCCESS, "GetComputeDispatches");
    Require(count > 0, "No upstream dispatches");
}
bool EqualConstants(const nrd::DispatchDesc* a, uint32_t an, const nrd::DispatchDesc* b, uint32_t bn) {
    Require(an == bn, "Dispatch count differs");
    bool equal = true;
    for (uint32_t i = 0; i < an; ++i) {
        Require(a[i].pipelineIndex == b[i].pipelineIndex && a[i].constantBufferDataSize == b[i].constantBufferDataSize,
            "Dispatch layout differs");
        if (a[i].constantBufferDataSize && std::memcmp(a[i].constantBufferData, b[i].constantBufferData, a[i].constantBufferDataSize)) equal = false;
    }
    return equal;
}
struct Case {
    const char* name; uint16_t width, height;
    float currentUv[2], previousUv[2], currentPixels[2], previousPixels[2];
};
void Test(const Case& c, nrd::Denoiser denoiser) {
    nrd::CommonSettings actual{};
    // Fixed non-jittered RH projection and identity view, column-major.
    const float projection[16] = {1,0,0,0, 0,-1,0,0, 0,0,-1.001001f,-1, 0,0,-.1001001f,0};
    const float view[16] = {1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1};
    std::memcpy(actual.viewToClipMatrix, projection, sizeof(projection));
    std::memcpy(actual.viewToClipMatrixPrev, projection, sizeof(projection));
    std::memcpy(actual.worldToViewMatrix, view, sizeof(view));
    std::memcpy(actual.worldToViewMatrixPrev, view, sizeof(view));
    actual.resourceSize[0] = actual.resourceSizePrev[0] = actual.rectSize[0] = actual.rectSizePrev[0] = c.width;
    actual.resourceSize[1] = actual.resourceSizePrev[1] = actual.rectSize[1] = actual.rectSizePrev[1] = c.height;
    actual.timeDeltaBetweenFrames = 16.666667f;
    actual.enableValidation = true;
    auto reference = actual;
    ftfx::SetCameraJitter(actual, c.currentUv, c.previousUv, c.width, c.height);
    std::memcpy(reference.cameraJitter, c.currentPixels, sizeof(c.currentPixels));
    std::memcpy(reference.cameraJitterPrev, c.previousPixels, sizeof(c.previousPixels));
    Require(!std::memcmp(&actual, &reference, sizeof(actual)), "Pixel reference or unrelated settings differ");
    auto wrong = reference;
    std::memcpy(wrong.cameraJitter, c.currentUv, sizeof(c.currentUv));
    std::memcpy(wrong.cameraJitterPrev, c.previousUv, sizeof(c.previousUv));
    Instance converted(denoiser), expected(denoiser), unscaled(denoiser);
    const nrd::DispatchDesc *a = nullptr, *b = nullptr, *bad = nullptr;
    uint32_t an = 0, bn = 0, badn = 0;
    // Warm up: first use forces history reset and replaces previous jitter.
    Dispatch(converted, actual, a, an); Dispatch(expected, reference, b, bn); Dispatch(unscaled, wrong, bad, badn);
    actual.frameIndex = reference.frameIndex = wrong.frameIndex = 1;
    Dispatch(converted, actual, a, an); Dispatch(expected, reference, b, bn); Dispatch(unscaled, wrong, bad, badn);
    Require(EqualConstants(a, an, b, bn), "Upstream constants differ from literal pixel reference");
    if (c.currentPixels[0] || c.currentPixels[1] || c.previousPixels[0] || c.previousPixels[1])
        Require(!EqualConstants(a, an, bad, badn), "Regression test accepts the old UV mismatch");
    std::printf("PASS %s %s\n", nrd::GetDenoiserString(denoiser), c.name);
}
}
int main() {
    try {
        // Exact binary UV inputs and independently specified pixel expectations.
        const Case cases[] = {
            {"zero",1920,1080,{0,0},{0,0},{0,0},{0,0}},
            {"rectangular signed",1920,1080,{.0001220703125f,-.000244140625f},{-.0001220703125f,.000244140625f},{.234375f,-.263671875f},{-.234375f,.263671875f}},
            {"square SSR",512,512,{.00048828125f,-.0009765625f},{-.00048828125f,.0009765625f},{.25f,-.5f},{-.25f,.5f}},
            {"half resolution",960,540,{.0001220703125f,-.000244140625f},{-.0001220703125f,.000244140625f},{.1171875f,-.1318359375f},{-.1171875f,.1318359375f}},
            {"quarter resolution",480,270,{.0001220703125f,-.000244140625f},{-.0001220703125f,.000244140625f},{.05859375f,-.06591796875f},{-.05859375f,.06591796875f}},
            {"odd dimensions",65,37,{.00390625f,-.00390625f},{-.00390625f,.0078125f},{.25390625f,-.14453125f},{-.25390625f,.2890625f}},
            {"reset same jitter",1024,512,{-.000244140625f,.00048828125f},{-.000244140625f,.00048828125f},{-.25f,.25f},{-.25f,.25f}},
            {"previous only",1024,512,{0,0},{.000244140625f,-.00048828125f},{0,0},{.25f,-.25f}},
        };
        for (auto denoiser : {nrd::Denoiser::REBLUR_SPECULAR, nrd::Denoiser::REBLUR_DIFFUSE})
            for (const auto& c : cases) Test(c, denoiser);
    } catch (const std::exception& e) { std::fprintf(stderr, "%s\n", e.what()); return 1; }
    return 0;
}
