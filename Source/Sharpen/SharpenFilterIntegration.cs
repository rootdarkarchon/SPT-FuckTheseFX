using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FuckTheseFX.Source.Sharpen
{
    // Match the vintage toggle's ownership rule: restore only effects we disabled.
    // Disabling the component removes its image pass, including a neutral copy.
    internal sealed class SharpenFilterIntegration : IDisposable
    {
        private readonly HashSet<CC_Sharpen> disabledByPlugin = new HashSet<CC_Sharpen>();
        private readonly Dictionary<int, CameraEffects> cameraEffects = new Dictionary<int, CameraEffects>();

        internal void Initialize()
        {
            try
            {
                Plugin.SharpenEnabled.SettingChanged += OnSettingChanged;
                SceneManager.sceneLoaded += OnSceneLoaded;
                Camera.onPreCull += OnCameraPreCull;
                ApplyConfiguredState();
            }
            catch { Dispose(); throw; }
        }

        private void OnSettingChanged(object sender, EventArgs args)
        {
            cameraEffects.Clear();
            ApplyConfiguredState();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            cameraEffects.Clear();
            disabledByPlugin.RemoveWhere(effect => effect == null);
            if (Plugin.SharpenEnabled.Value) { Restore(); return; }
            if (!scene.IsValid() || !scene.isLoaded) return;

            var roots = new List<GameObject>(scene.rootCount);
            var effects = new List<CC_Sharpen>();
            scene.GetRootGameObjects(roots);
            foreach (GameObject root in roots)
            {
                if (root == null) continue;
                root.GetComponentsInChildren(true, effects);
                foreach (CC_Sharpen effect in effects) DisableAndRemember(effect);
            }
        }

        private void OnCameraPreCull(Camera camera)
        {
            if (Plugin.SharpenEnabled.Value || camera == null) return;
            int id = camera.GetInstanceID();
            if (!cameraEffects.TryGetValue(id, out var entry) || entry.Camera != camera)
            {
                entry = new CameraEffects { Camera = camera, Effects = camera.GetComponents<CC_Sharpen>() };
                cameraEffects[id] = entry;
            }
            foreach (var effect in entry.Effects) DisableAndRemember(effect);
        }

        private void ApplyConfiguredState()
        {
            disabledByPlugin.RemoveWhere(effect => effect == null);
            if (Plugin.SharpenEnabled.Value) { Restore(); return; }
            foreach (var effect in Resources.FindObjectsOfTypeAll<CC_Sharpen>())
            {
                if (effect == null || effect.gameObject == null) continue;
                var scene = effect.gameObject.scene;
                if (scene.IsValid() && scene.isLoaded) DisableAndRemember(effect);
            }
        }

        private void DisableAndRemember(CC_Sharpen effect)
        {
            if (effect == null || !effect.enabled) return;
            disabledByPlugin.Add(effect);
            effect.enabled = false;
        }

        private void Restore()
        {
            foreach (var effect in disabledByPlugin) if (effect != null) effect.enabled = true;
            disabledByPlugin.Clear();
        }

        public void Dispose()
        {
            Camera.onPreCull -= OnCameraPreCull;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Plugin.SharpenEnabled != null) Plugin.SharpenEnabled.SettingChanged -= OnSettingChanged;
            cameraEffects.Clear();
            Restore();
        }

        private sealed class CameraEffects
        {
            internal Camera Camera;
            internal CC_Sharpen[] Effects;
        }
    }
}
