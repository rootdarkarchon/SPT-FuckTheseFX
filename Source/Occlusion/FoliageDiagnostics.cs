using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.Occlusion
{
    // Diagnostic-only camera-owned captures. No lighting replacement and no second scene render.
    internal sealed class FoliageDiagnostics : IDisposable
    {
        private readonly Camera camera;
        private readonly RenderTexture[] targets = new RenderTexture[8];
        private readonly CommandBuffer[] stages = new CommandBuffer[3];
        private readonly CameraEvent[] events = { CameraEvent.BeforeImageEffectsOpaque, CameraEvent.AfterForwardAlpha, CameraEvent.AfterEverything };
        private readonly string[] names = { "ao-inputs", "ambient-unoccluded", "ambient-removed", "before-ambient", "after-ambient", "before-opaque-effects", "after-forward-alpha", "after-everything-camera-target" };
        private string metadata;
        internal int Frames { get; private set; }
        internal int Frame { get; private set; }
        internal RenderTexture AO => targets[0];
        internal RenderTexture Unoccluded => targets[1];
        internal RenderTexture Removed => targets[2];
        internal RenderTexture Stage(int index) => targets[index];

        internal FoliageDiagnostics(Camera camera, int width, int height)
        {
            this.camera = camera;
            try
            {
                for(int i=0;i<targets.Length;i++) targets[i] = GpuAssets.Target("FTFX foliage "+names[i],width,height,GraphicsFormat.R16G16B16A16_SFloat,i<3);
                for(int i=0;i<stages.Length;i++) stages[i] = new CommandBuffer { name = "FTFX foliage capture: "+names[i+5] };
            }
            catch { Dispose(); throw; }
        }
        internal void BeginFrame(CommandBuffer cb, OcclusionSettings settings)
        {
            Frames++; Frame = Time.frameCount;
            foreach(var target in targets) { cb.SetRenderTarget(target); cb.ClearRenderTarget(false,true,Color.clear); }
            cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            var b = new StringBuilder();
            b.AppendLine("FTFX "+Plugin.Version+" foliage lighting capture");
            b.AppendLine("Frame="+Frame+" camera="+camera.name+" instance="+camera.GetInstanceID()+" size="+AO.width+"x"+AO.height);
            b.AppendLine("HDR="+camera.allowHDR+" colorSpace="+QualitySettings.activeColorSpace+" path="+camera.actualRenderingPath+" API="+SystemInfo.graphicsDeviceType);
            b.AppendLine("Position="+camera.transform.position.ToString("F6")+" rotation="+camera.transform.rotation.ToString("F6")+" FOV="+camera.fieldOfView.ToString(CultureInfo.InvariantCulture));
            b.AppendLine("near="+camera.nearClipPlane+" far="+camera.farClipPlane+" target="+(camera.targetTexture != null ? camera.targetTexture.name : "backbuffer"));
            b.AppendLine("AmbientEnabled="+settings.Ambient.Value+" strength="+settings.AmbientStrengthFraction+" foliage="+settings.FoliageFraction+" bent="+settings.BentNormals.Value);
            b.AppendLine("ReflectionOcclusionFix="+settings.ReflectionOcclusion.Value);
            b.AppendLine("Fade="+settings.FadeStart.Value+".."+settings.FadeEnd.Value+" resolved="+settings.DistanceFade+" quality="+settings.Quality.Value);
            b.AppendLine("Radius="+settings.Radius.Value+" multiplier="+settings.RadiusMultiplier.Value+" power="+settings.Power.Value+" denoise="+settings.Denoise.Value+" normals="+settings.Normals.Value);
            b.AppendLine("ActorPass="+settings.Actors.Value+" contactGI="+settings.Bleed.Enabled.Value+" contactGIStrength="+settings.Bleed.Strength.Value);
            b.AppendLine("FoliageExtraAO="+settings.FoliageExtraAOFraction+" (before native opaque post-processing and fog, main-camera HDR only)");
            metadata = b.ToString();
        }
        private static void Copy(CommandBuffer cb, RenderTexture destination)
        {
            cb.Blit(BuiltinRenderTextureType.CameraTarget,destination);
            cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        }
        internal void BeforeAmbient(CommandBuffer cb)
        {
            Copy(cb,targets[3]);
            cb.SetRandomWriteTarget(2,Unoccluded); cb.SetRandomWriteTarget(3,Removed);
        }
        internal void AfterAmbient(CommandBuffer cb) => Copy(cb,targets[4]);
        internal void RecordStages()
        {
            // Append after currently registered buffers at each stage; inventory is saved with the capture.
            for(int i=0;i<stages.Length;i++)
            {
                camera.RemoveCommandBuffer(events[i],stages[i]); stages[i].Clear();
                Copy(stages[i],targets[i+5]); camera.AddCommandBuffer(events[i],stages[i]);
            }
        }
        internal string Save(string root)
        {
            string folder = Path.Combine(root,DateTime.Now.ToString("yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture)+"-frame"+Frame);
            Directory.CreateDirectory(folder);
            var previous = RenderTexture.active;
            Texture2D read = null, preview = null;
            try
            {
                read = new Texture2D(AO.width,AO.height,TextureFormat.RGBAFloat,false,true);
                preview = new Texture2D(AO.width,AO.height,TextureFormat.RGBA32,false,true);
                for(int i=0;i<targets.Length;i++)
                {
                    RenderTexture.active=targets[i];read.ReadPixels(new Rect(0,0,AO.width,AO.height),0,0);read.Apply();
                    File.WriteAllBytes(Path.Combine(folder,names[i]+".exr"),read.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat | Texture2D.EXRFlags.CompressZIP));
                    var pixels=read.GetPixels();
                    if(i==0)
                    {
                        int valid=0,missing=0;
                        foreach(var c in pixels) { if(c.a==1) valid++; else if(c.a>1) missing++; }
                        metadata += "Ambient pixel status: valid="+valid+" missingInputs="+missing+" notWritten="+(pixels.Length-valid-missing)+Environment.NewLine;
                        for(int channel=0;channel<3;channel++)
                        {
                            var display=new Color[pixels.Length];
                            for(int p=0;p<pixels.Length;p++)
                            {
                                var c=pixels[p];float value=channel==0?c.r:channel==1?c.g:c.b;
                                display[p]=c.a<.5f?Color.cyan:c.a>1.5f?Color.magenta:new Color(value,value,value,1);
                            }
                            preview.SetPixels(display);preview.Apply();
                            File.WriteAllBytes(Path.Combine(folder,(channel==0?"effective-ao":channel==1?"supplied-ao":"zero-alpha-materials")+".png"),preview.EncodeToPNG());
                        }
                    }
                    else
                    {
                        // A shared 4x display gain for ambient terms; scene previews use 1x. EXRs are never transformed.
                        float gain=i<3?4:1;
                        for(int p=0;p<pixels.Length;p++)
                        {
                            var c=pixels[p];
                            if(i<3 && c.a<.5f) { pixels[p]=Color.cyan; continue; }
                            if(i>=3 && !camera.allowHDR && i<=4)
                                c=new Color(-Mathf.Log(Mathf.Max(c.r,1e-20f),2),-Mathf.Log(Mathf.Max(c.g,1e-20f),2),-Mathf.Log(Mathf.Max(c.b,1e-20f),2),1);
                            pixels[p]=new Color(Mathf.Clamp01(c.r*gain),Mathf.Clamp01(c.g*gain),Mathf.Clamp01(c.b*gain),1);
                        }
                        preview.SetPixels(pixels);preview.Apply();File.WriteAllBytes(Path.Combine(folder,names[i]+".png"),preview.EncodeToPNG());
                    }
                }
                foreach(CameraEvent evt in Enum.GetValues(typeof(CameraEvent)))
                    foreach(var buffer in camera.GetCommandBuffers(evt)) metadata += evt+": "+buffer.name+Environment.NewLine;
                File.WriteAllText(Path.Combine(folder,"capture.txt"),metadata+Environment.NewLine+Readme);
                File.WriteAllText(Path.Combine(folder,"COMPLETE.txt"),"All textures and metadata saved.");
            }
            finally { RenderTexture.active=previous;if(read!=null)UnityEngine.Object.DestroyImmediate(read);if(preview!=null)UnityEngine.Object.DestroyImmediate(preview); }
            return folder;
        }
        private const string Readme =
            "All EXRs are raw same-camera captures from the recorded frame. No gain, clipping, gamma conversion or tone mapping is applied to EXRs.\n"+
            "ao-inputs RGBA: effective ambient AO visibility, supplied decoded AO visibility, authored zero-alpha classification, status (0 no draw, 1 valid, 2 missing material capture, 3 missing separate AO). PNG AO: black fully occluded, white unoccluded; cyan no draw; magenta missing inputs. Zero-alpha is a compatibility class, not a guaranteed foliage mask.\n"+
            "ambient-unoccluded RGB: current diffuse+fill before scalar AO; ambient-removed RGB: that contribution*(1-effectiveAO). These exclude reflections and hold bent-normal lighting fixed. Alpha 1 means written by an actual ambient fragment, 0 means no draw. PNGs use 4x gain, which may clip.\n"+
            "before/after-ambient surround the native ambient draw. If several draws occur, the last draw is captured. In HDR these are target color values; in LDR these two EXRs retain exp2(-lighting) encoding. Only their PNG previews decode LDR.\n"+
            "Later stage images preserve the running scene, including enabled contact GI and actor AO. Later fog, SSR, transparency, grading and upscaling may alter values/coordinates. Do not subtract raw EXRs across incompatible encodings or resolutions and call it lost AO. AfterEverything is CameraTarget, not a verified final DLSS/SSAA display tap.\n"+
            "Capture compares stages, not an AO-off counterfactual: it cannot by itself prove that later rendering erases AO. PNG scene previews are clamped raw 1x references, not calibrated screenshots. A normal game screenshot is useful alongside this folder.\n";
        public void Dispose()
        {
            for(int i=0;i<stages.Length;i++) if(stages[i]!=null) { if(camera!=null)camera.RemoveCommandBuffer(events[i],stages[i]);stages[i].Dispose(); }
            for(int i=0;i<targets.Length;i++) GpuAssets.Release(ref targets[i]);
        }
    }
}
