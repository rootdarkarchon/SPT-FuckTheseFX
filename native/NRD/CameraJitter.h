#pragma once
#include "NRD.h"

namespace ftfx {
// The managed frame/capture ABI uses texture UV offsets. NRD expects pixels
// in its input rectangle, which can differ from the camera raster resolution.
// Backend dimensions stay fixed for its lifetime; resize recreates the backend
// and resets history, so current and previous jitter use the same dimensions.
inline void SetCameraJitter(nrd::CommonSettings& settings, const float* currentUv,
    const float* previousUv, uint32_t width, uint32_t height) {
    settings.cameraJitter[0] = currentUv[0] * float(width);
    settings.cameraJitter[1] = currentUv[1] * float(height);
    settings.cameraJitterPrev[0] = previousUv[0] * float(width);
    settings.cameraJitterPrev[1] = previousUv[1] * float(height);
}
}
