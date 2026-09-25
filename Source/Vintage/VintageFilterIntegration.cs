using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FuckTheseFX.Source.Vintage
{
    // Uses VintageBeGone's event-driven camera handling and selective restoration.
    internal sealed class VintageFilterIntegration : IDisposable
    {
        private readonly HashSet<CC_Vintage> disabledByPlugin = new HashSet<CC_Vintage>();
        private readonly Dictionary<int, CameraEffects> cameraEffects = new Dictionary<int, CameraEffects>();

        internal void Initialize()
        {
            try
            {
                Plugin.VintageEnabled.SettingChanged += OnSettingChanged;
                SceneManager.sceneLoaded += OnSceneLoaded;
                Camera.onPreCull += OnCameraPreCull;
                ApplyConfiguredStateToLoadedEffects();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void OnSettingChanged(object sender, EventArgs eventArgs)
        {
            cameraEffects.Clear();
            ApplyConfiguredStateToLoadedEffects();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            cameraEffects.Clear();
            ApplyConfiguredStateToLoadedEffects();
        }

        private void OnCameraPreCull(Camera camera)
        {
            if (Plugin.VintageEnabled.Value || camera == null) return;
            int cameraId = camera.GetInstanceID();
            if (!cameraEffects.TryGetValue(cameraId, out CameraEffects entry) || entry.Camera != camera)
            {
                entry = new CameraEffects(camera, camera.GetComponents<CC_Vintage>());
                cameraEffects[cameraId] = entry;
            }
            foreach (CC_Vintage effect in entry.Effects)
                DisableAndRemember(effect);
        }

        private void ApplyConfiguredStateToLoadedEffects()
        {
            disabledByPlugin.RemoveWhere(effect => effect == null);
            if (Plugin.VintageEnabled.Value)
            {
                RestoreRememberedEffects();
                return;
            }
            foreach (CC_Vintage effect in Resources.FindObjectsOfTypeAll<CC_Vintage>())
            {
                if (IsLoadedSceneComponent(effect))
                    DisableAndRemember(effect);
            }
        }

        private void DisableAndRemember(CC_Vintage effect)
        {
            if (effect == null || !effect.enabled) return;
            disabledByPlugin.Add(effect);
            effect.enabled = false;
        }

        private void RestoreRememberedEffects()
        {
            foreach (CC_Vintage effect in disabledByPlugin)
            {
                if (effect != null) effect.enabled = true;
            }
            disabledByPlugin.Clear();
        }

        private static bool IsLoadedSceneComponent(CC_Vintage effect)
        {
            if (effect == null || effect.gameObject == null) return false;
            Scene scene = effect.gameObject.scene;
            return scene.IsValid() && scene.isLoaded;
        }

        public void Dispose()
        {
            Camera.onPreCull -= OnCameraPreCull;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Plugin.VintageEnabled != null)
                Plugin.VintageEnabled.SettingChanged -= OnSettingChanged;
            cameraEffects.Clear();
            RestoreRememberedEffects();
        }

        private sealed class CameraEffects
        {
            internal CameraEffects(Camera camera, CC_Vintage[] effects)
            {
                Camera = camera;
                Effects = effects;
            }
            internal Camera Camera { get; }
            internal CC_Vintage[] Effects { get; }
        }
    }
}
