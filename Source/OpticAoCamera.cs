using EFT.CameraControl;
using UnityEngine;

namespace FuckTheseFX
{
    // The one EFT optic camera while an ordinary (not night/thermal) scope is active. Lives outside
    // Source/Occlusion, which is compiled into a fixture project with stub EFT types only.
    internal static class OpticAoCamera
    {
        private static Camera cached;
        private static BSG.CameraEffects.NightVision night;
        private static ThermalVision thermal;

        // Same eligibility inputs as the optic SSR path; component lookups are cached per camera instance.
        internal static Camera Current()
        {
            var manager = CameraManager.Instance?.OpticCameraManager;
            var camera = manager?.Camera;
            if(camera == null || !camera.isActiveAndEnabled) return null;
            var sight = manager.CurrentOpticSight;
            var scope = sight != null ? sight.ScopeData : null;
            if(scope == null) return null;
            if(scope.NightVisionData != null && scope.NightVisionData.NightVision) return null;
            if(scope.ThermalVisionData != null && scope.ThermalVisionData.ThermalVision) return null;
            if(!ReferenceEquals(cached,camera)) { cached = camera; night = camera.GetComponent<BSG.CameraEffects.NightVision>(); thermal = camera.GetComponent<ThermalVision>(); }
            if(night != null && night.isActiveAndEnabled && night.On) return null;
            if(thermal != null && thermal.isActiveAndEnabled && thermal.On) return null;
            return camera;
        }
    }
}
