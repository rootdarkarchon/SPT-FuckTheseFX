using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using FuckTheseFX;
using FuckTheseFX.Source.Highlight;

public static class AmbientHighlightFixture
{
    public static void Run(Camera camera)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception("Ambient Highlight fixture: " + message); }
        var owner = camera.gameObject.AddComponent<AmbientHighlight>();
        var otherObject = new GameObject("Highlight newly spawned");
        var other = otherObject.AddComponent<AmbientHighlight>();
        using (var unrelated = new CommandBuffer { name = "Unrelated lighting" })
        using (var direct = new CommandBuffer())
        {
            camera.AddCommandBuffer(CameraEvent.AfterLighting, unrelated);
            using (var integration = new AmbientHighlightIntegration())
            {
                integration.Initialize();
                var sh = new SphericalHarmonicsL2(); sh[0, 0] = 1;
                owner.SetSH(sh); owner.ManualOnRenderObject(camera);
                Check(owner.LightingCalls == 1 && owner.RecordCalls == 1 && camera.GetCommandBuffers(CameraEvent.AfterLighting).Length == 2, "Enabled native path runs");
                Plugin.AmbientHighlightEnabled.Value = false;
                integration.Update();
                Check(camera.GetCommandBuffers(CameraEvent.AfterLighting).Length == 1 && camera.GetCommandBuffers(CameraEvent.AfterLighting)[0].name == unrelated.name, "Disable detaches recorded highlight only; remaining=" + camera.GetCommandBuffers(CameraEvent.AfterLighting).Length);
                sh[0, 0] = 2; owner.SetSH(sh); sh[0, 0] = 3; owner.SetSH(sh);
                owner.ManualOnRenderObject(camera);
                Check(owner.LightingCalls == 1 && owner.RenderCalls == 1 && owner.RecordCalls == 1, "Disabled skips preparation and recording");
                direct.SetGlobalFloat("_FixtureHighlight", 9);
                owner.UpdateAmbientBuffer(direct, camera);
                Check(direct.sizeInBytes == 0 && owner.RecordCalls == 1, "Direct caller cannot retain stale draw commands");
                other.ManualOnRenderObject(camera);
                Check(other.RenderCalls == 0 && camera.GetCommandBuffers(CameraEvent.AfterLighting).Length == 1, "New owner disabled before first render");
                Plugin.AmbientHighlightEnabled.Value = true;
                integration.Update(); owner.ManualOnRenderObject(camera);
                Check(owner.LightingCalls == 2 && owner.LastLighting[0, 0] == 3 && owner.RecordCalls == 2, "Re-enable uses latest lighting and native rebuild");
                Check(camera.GetCommandBuffers(CameraEvent.AfterLighting).Length == 2, "Native buffer reattached");
                Plugin.AmbientHighlightEnabled.Value = false;
                integration.Update(); sh[0, 0] = 4; owner.SetSH(sh);
            }
            Check(owner.LightingCalls == 3 && owner.LastLighting[0, 0] == 4, "Disposal restores pending lighting");
            owner.ManualOnRenderObject(camera);
            Check(owner.RenderCalls == 3 && camera.GetCommandBuffers(CameraEvent.AfterLighting).Length == 2, "Disposal removes suppression");
            camera.RemoveCommandBuffer(CameraEvent.AfterLighting, unrelated);
        }
        Plugin.AmbientHighlightEnabled.Value = true;
        UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(otherObject);
        File.WriteAllText("highlight-validation.txt", "PASSED production Harmony highlight suppression, removal of existing camera buffers, no disabled lighting/recording work, direct-call buffer clearing, native re-enable with latest lighting, new owners, preservation of unrelated buffers and disposal restoration. Installed ABI checked separately; live EFT remains unverified.");
    }
}
