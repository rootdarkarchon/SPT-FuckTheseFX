using System;
using System.IO;
using System.Linq;
using System.Text;
using FuckTheseFX.Source.Water;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class WaterValidation
{
    private const int Size=96;
    private static readonly Color Clear=new Color(.17f,.31f,.43f,.57f);
    private static readonly StringBuilder Report=new StringBuilder();
    private static Camera camera;
    private static Mesh mesh;
    private static Texture2D normal,detail,foam,ripple,background,backgroundNormal,depth;
    private static Cubemap cube;
    private static Material depthProbe;
    private static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    public static void Run()
    {
        try
        {
            Check(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Direct3D11,"Requires D3D11");
            PlayerSettings.colorSpace=ColorSpace.Gamma;
            AssetDatabase.Refresh();
            var source=AssetDatabase.LoadAssetAtPath<Shader>("Assets/WaterParity.shader");
            Check(source!=null && !ShaderUtil.ShaderHasError(source),"Water shader failed compilation");
            var manifest=BuildPipeline.BuildAssetBundles("Output",new[]{new AssetBundleBuild {assetBundleName="water",assetNames=new[]{"Assets/WaterParity.shader"}}},BuildAssetBundleOptions.ForceRebuildAssetBundle,BuildTarget.StandaloneWindows64);
            Check(manifest!=null,"Bundle build failed");
            var replacementBundle=AssetBundle.LoadFromFile(Path.GetFullPath("Output/water"));
            var references=new Material[8];
            for(int v=0;v<8;v++)
            {
                var bundle=AssetBundle.LoadFromFile(Path.GetFullPath("native-reference-"+v));
                references[v]=new Material(bundle.LoadAllAssets<Shader>().Single());bundle.Unload(false);
                Check(references[v].shader.name=="Hidden/WaterForSSRv2-ParityFixture-"+v,"Pinned reference identity");
            }
            Material native=references[0];
            var surface=new Material(replacementBundle.LoadAllAssets<Shader>().Single());
            var replacement=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/WaterParityBaseline.shader"));
            Check(surface.shader.name=="Hidden/FTFX/WaterParity" && replacement.shader.name=="Hidden/FTFX/WaterParityBaseline","Surface/baseline identity");
            Check(native.passCount==2 && replacement.passCount==2,"Two native passes");
            WaterIntegrationValidation.Run(surface.shader);
            Setup();
            int cases=0,changedNormals=0;
            Color[][] dryNative=null,dryReplacement=null;
            for(int variant=0;variant<8;variant++)
            for(int view=0;view<3;view++)
            for(int environment=0;environment<3;environment++)
            {
                native=references[variant];
                camera.transform.position=view==0 ? new Vector3(0,4,-8) : view==1 ? new Vector3(2,.7f,-8) : new Vector3(-1,8,-2);
                camera.transform.LookAt(new Vector3(0,0,3));
                camera.fieldOfView=view==2?25:65;
                string[] keys={"Rain","WINTER_SNOW","UNITY_HDR_ON"};
                camera.allowHDR=(variant&4)!=0;
                for(int k=0;k<3;k++) { if((variant&(1<<k))!=0) { Shader.EnableKeyword(keys[k]); native.EnableKeyword(keys[k]); replacement.EnableKeyword(keys[k]); } else { Shader.DisableKeyword(keys[k]); native.DisableKeyword(keys[k]); replacement.DisableKeyword(keys[k]); } }
                var a=Render(native,variant,environment,0);
                var b=Render(replacement,variant,environment,0);
                if(view==0 && environment==0)
                {
                    if(variant==0){dryNative=a;dryReplacement=b;}
                    for(int t=0;t<5;t++) Report.AppendLine($"target {t}: native mean={a[t].Average(c=>c.r):G8} ftfx mean={b[t].Average(c=>c.r):G8} native delta from dry={a[t].Select((c,i)=>Math.Abs(c.r-dryNative[t][i].r)).Max():G6} ftfx delta from dry={b[t].Select((c,i)=>Math.Abs(c.r-dryReplacement[t][i].r)).Max():G6}");
                    if(variant==1) Check(a[2].Select((c,i)=>Math.Abs(c.r-dryNative[2][i].r)).Max()>.001,"Rain fixture did not change the native normals");
                    if(variant==4) Check(a[3].Select((c,i)=>Math.Abs(c.r-dryNative[3][i].r)).Max()>.1,"HDR fixture did not change native encoding");
                }
                double max=0,mean=0;int changed=0;
                for(int target=0;target<5;target++)
                for(int pixel=0;pixel<a[target].Length;pixel++)
                for(int channel=0;channel<(target==4?1:4);channel++)
                {
                    float x=a[target][pixel][channel],y=b[target][pixel][channel];
                    Check(!float.IsNaN(x) && !float.IsInfinity(x) && !float.IsNaN(y) && !float.IsInfinity(y),"Nonfinite output");
                    double delta=Math.Abs(x-y);max=Math.Max(max,delta);mean+=delta;
                    if(target==0 && channel==0 && Math.Abs(x-Clear.r)>.001) changed++;
                }
                mean/=Size*Size*17;
                Report.AppendLine($"variant={variant} view={view} environment={environment} max={max:G6} mean={mean:G6} covered={changed}");
                Check(changed>100,"Vacuous fixture: water did not cover enough pixels");
                Check(max<.00001 && mean<.0000001,"Native/replacement MRT parity failed");
                var modern=Render(surface,variant,environment,0);
                for(int target=0;target<5;target++)for(int pixel=0;pixel<modern[target].Length;pixel++)
                for(int channel=0;channel<(target==4?1:4);channel++)
                {
                    float value=modern[target][pixel][channel];
                    Check(!float.IsNaN(value) && !float.IsInfinity(value),"Nonfinite phase-two output");
                    if(target==2 && channel<3 && Math.Abs(value-a[target][pixel][channel])>.005)changedNormals++;
                    if(target==4)Check(Math.Abs(value-a[target][pixel][channel])<.00001,"Normal changes altered geometry/snow depth output");
                }
                cases++;
            }
            Check(changedNormals>100,"Phase-two shader did not visibly change the normal field");
            // With flat input normals, both models must agree: changing normals
            // must not introduce an independent lighting/depth/reflection effect.
            var savedNormal=normal;var savedDetail=detail;
            normal=Texture((x,y)=>new Color(.5f,.5f,1,1));detail=normal;
            for(int variant=0;variant<8;variant++)
                Compare(Render(replacement,variant,0,0),Render(surface,variant,0,0),"Flat-input lighting contract");
            Object.DestroyImmediate(normal);normal=savedNormal;detail=savedDetail;
            var clock=new WaterSurfaceIntegration();
            FuckTheseFX.Plugin.WaterAnimationSpeed.Value=1;
            clock.AdvanceAnimation(25);
            surface.SetFloat("_FTFXWaterAnimationTime",clock.AnimationTime);
            // Live controls must change the actual normal output, not merely config labels.
            var defaultSurface=Render(surface,0,0,0);
            foreach(var controls in new[]{new Vector4(2,1,1,1),new Vector4(1,0,1,1),new Vector4(1,1,4,1)})
            {
                surface.SetVector("_FTFXWaterNormalControls",controls);
                var tuned=Render(surface,0,0,0);
                Check(tuned[2].Select((c,i)=>Math.Abs(c.r-defaultSurface[2][i].r)+Math.Abs(c.b-defaultSurface[2][i].b)).Max()>.000001,"Water control did not affect rendered normals: "+controls);
            }
            // A rate change must not itself move the image. Only subsequent elapsed
            // time advances it. Exercise the production clock with Unity _Time held
            // fixed, including each band alone so moving detail cannot hide frozen broad waves.
            surface.SetVector("_FTFXWaterNormalControls",new Vector4(1,1,1,0));
            FuckTheseFX.Plugin.WaterAnimationSpeed.Value=0;
            clock.AdvanceAnimation(30);surface.SetFloat("_FTFXWaterAnimationTime",clock.AnimationTime);
            Compare(defaultSurface,Render(surface,0,0,0),"Speed zero must preserve the current pattern");
            var stoppedA=Render(surface,0,0,0,false,false,1);
            var stoppedB=Render(surface,0,0,0,false,false,3);
            for(int pixel=0;pixel<stoppedA[2].Length;pixel++)for(int channel=0;channel<3;channel++)
                Check(Math.Abs(stoppedA[2][pixel][channel]-stoppedB[2][pixel][channel])<.00001,"Zero animation speed did not freeze surface normals");
            surface.SetVector("_FTFXWaterNormalControls",Vector4.one);
            Compare(defaultSurface,Render(surface,0,0,0),"Default controls restore normal model");
            FuckTheseFX.Plugin.WaterAnimationSpeed.Value=1;
            for(int band=0;band<3;band++)
            {
                var before=Render(surface,0,0,0,normalBand:band);
                clock.AdvanceAnimation(1);surface.SetFloat("_FTFXWaterAnimationTime",clock.AnimationTime);
                var after=Render(surface,0,0,0,normalBand:band);
                double delta=after[2].Select((c,i)=>Math.Abs(c.r-before[2][i].r)+Math.Abs(c.b-before[2][i].b)).Max();
                Check(delta>.0001,"Normal band failed to animate with advancing production clock: "+band);
                Report.AppendLine($"PASS: normal band {band} moves over one second with fixed Unity _Time; max G2 XZ delta={delta:G6}.");
            }
            Report.AppendLine("PASS: size/strength/detail controls change GPU normals; speed changes preserve phase; zero speed freezes in place; all three bands animate from the production elapsed-time clock.");
            ValidateBandFading(surface);
            var nativeDepth=Render(references[0],0,0,1);var replacementDepth=Render(surface,0,0,1);
            Compare(nativeDepth,replacementDepth,"Second pass color");
            for(int p=0;p<nativeDepth[0].Length;p++)
                Check(Math.Abs(nativeDepth[0][p].a-Clear.a)<.00001f,"Native depth pass must preserve alpha");
            for(int pass=0;pass<2;pass++)
            {
                var a=Render(references[0],0,0,pass,true);var b=Render(surface,0,0,pass,true);
                var noWater=Render(references[0],0,0,pass,true,true);
                for(int pixel=0;pixel<a[0].Length;pixel++)Check((a[0][pixel].r>.99f)==(b[0][pixel].r>.99f),"Phase-two depth occlusion changed");
                Check(noWater[0].Count(c=>c.r>.99f)-a[0].Count(c=>c.r>.99f)>100,"Water pass did not occlude the submerged depth probe");
            }
            for(int variant=0;variant<8;variant++)
            {
                var before=Render(surface,variant,0,0);
                surface.SetFloat("_FTFXWaterDiagnostic",1);
                var tinted=Render(surface,variant,0,0);
                var expected=(variant&4)!=0?new Color(2,0,2,1):new Color(.25f,1,.25f,1);
                Check(tinted[3].Count(c=>Math.Abs(c.r-expected.r)<.00001 && Math.Abs(c.g-expected.g)<.00001 && Math.Abs(c.b-expected.b)<.00001)>100,"Diagnostic emission not visible in variant "+variant);
                surface.SetFloat("_FTFXWaterDiagnostic",0);
                Compare(before,Render(surface,variant,0,0),"Diagnostic off restores surface");
            }
            Report.AppendLine("PASS: magenta diagnostic emission in all eight variants, default off, Native bypass, main/optic material selection and tint-off parity restoration.");
            Check(!ShaderUtil.ShaderHasError(source),"Shader variant compiler error");
            Report.AppendLine($"PASS: {cases} native/baseline five-MRT comparisons and phase-two finite output/normal changes; eight flat-normal lighting comparisons; dry/rain/snow/HDR, main/grazing/optic FOV, sun/night/thermal, SSR on/off; depth occlusion and snow depth unchanged. Unity ownership surrogate and diagnostic restore passed. Changed normal channels: {changedNormals}.");
            Report.AppendLine("Gamma / D3D11 / Unity "+Application.unityVersion+" / "+SystemInfo.graphicsDeviceName+". Live raid visuals, ordering and GPU timing remain unverified.");
            File.WriteAllText("validation.txt",Report.ToString());
            replacementBundle.Unload(false);
        }
        catch(Exception e) { File.WriteAllText("failure.txt",Report+"\n"+e); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static void Compare(Color[][] a,Color[][] b,string name)
    { for(int t=0;t<a.Length;t++)for(int p=0;p<a[t].Length;p++)for(int c=0;c<(t==4?1:4);c++) Check(Math.Abs(a[t][p][c]-b[t][p][c])<.00001f,name+" mismatch"); }
    private static Texture2D Texture(Func<int,int,Color> value, bool mip=true, int size=64)
    {
        var t=new Texture2D(size,size,TextureFormat.RGBAFloat,mip,true) {wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
        var pixels=new Color[size*size];for(int y=0;y<size;y++)for(int x=0;x<size;x++)pixels[y*size+x]=value(x,y);
        t.SetPixels(pixels);t.Apply();return t;
    }
    private static void ValidateBandFading(Material surface)
    {
        var savedNormal=normal;var savedDetail=detail;
        var position=camera.transform.position;var rotation=camera.transform.rotation;float fov=camera.fieldOfView;
        // Constant normals at every mip isolate explicit attenuation from texture
        // filtering. The production shader still uses actual camera derivatives,
        // bound MPB texture dimensions and the complete native G-buffer path.
        normal=Texture((x,y)=>new Color(.7f,.6f,1,1),size:1024);
        detail=Texture((x,y)=>new Color(.5f,.5f,1,1));
        camera.transform.position=new Vector3(0,20,3);
        camera.transform.LookAt(new Vector3(0,0,3),Vector3.forward);
        double Signal(int band,float fieldOfView)
        {
            camera.fieldOfView=fieldOfView;
            surface.SetVector("_FTFXWaterNormalControls",Vector4.one);
            var actual=Render(surface,0,0,0,normalBand:band);
            surface.SetVector("_FTFXWaterNormalControls",new Vector4(1,0,1,1));
            var flat=Render(surface,0,0,0,normalBand:band);
            surface.SetVector("_FTFXWaterNormalControls",Vector4.one);
            int p=(Size/2)*Size+Size/2;
            return Math.Abs(actual[2][p].r-flat[2][p].r)+Math.Abs(actual[2][p].b-flat[2][p].b);
        }
        double optic=Signal(1,25),middle=Signal(1,40),main=Signal(1,65),wide=Signal(1,95);
        Check(optic>.01 && optic>middle && middle>main && main>wide+.001 && wide<.00001,
            $"Medium footprint fade / optic recovery failed: {optic}, {middle}, {main}, {wide}");
        double broadOptic=Signal(0,25),broadMain=Signal(0,65),broadWide=Signal(0,120);
        Check(broadMain>.01 && Math.Abs(broadMain-broadOptic)<.001 && broadWide<broadMain,
            $"Broad band must remain while resolved, then fade: {broadOptic}, {broadMain}, {broadWide}");
        camera.fieldOfView=65;
        var smallFlatDetail=Render(surface,0,0,0);
        var originalFlat=detail;
        detail=Texture((x,y)=>new Color(.5f,.5f,1,1),size:1024);
        Compare(smallFlatDetail,Render(surface,0,0,0),"Fading flat detail must not boost the remaining normal bands");
        Report.AppendLine($"PASS: production medium fade at 25/40/65/95 degree FOV: {optic:G6}/{middle:G6}/{main:G6}/{wide:G6}; broad stays resolved at 25/65 and fades at 120; no surviving-band amplification.");
        Object.DestroyImmediate(normal);Object.DestroyImmediate(detail);Object.DestroyImmediate(originalFlat);
        normal=savedNormal;detail=savedDetail;
        camera.transform.SetPositionAndRotation(position,rotation);camera.fieldOfView=fov;
    }
    private static void Setup()
    {
        camera=new GameObject("Water fixture camera").AddComponent<Camera>();camera.enabled=false;camera.cullingMask=0;
        camera.nearClipPlane=.3f;camera.farClipPlane=200;camera.allowHDR=false;camera.allowMSAA=false;
        camera.targetTexture=new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        depthProbe=new Material(Shader.Find("Hidden/FTFX/WaterDepthProbe"));
        mesh=new Mesh {vertices=new[]{new Vector3(-10,0,-2),new Vector3(-10,0,18),new Vector3(10,0,18),new Vector3(10,0,-2)},triangles=new[]{0,1,2,0,2,3}};mesh.RecalculateBounds();
        normal=Texture((x,y)=>new Color(.5f+.14f*Mathf.Sin(x*.7f),.5f+.13f*Mathf.Cos(y*.6f),1,1));
        detail=Texture((x,y)=>new Color(.5f+.17f*Mathf.Cos(x*1.1f),.5f+.16f*Mathf.Sin(y*.9f),1,1));detail.mipMapBias=-2;
        foam=Texture((x,y)=>new Color((x%7)/7f,(y%9)/9f,.3f,1));
        ripple=Texture((x,y)=>new Color((x%13)/13f,(y%11)/11f,.5f+.25f*Mathf.Sin(x),.5f+.25f*Mathf.Cos(y)));
        background=Texture((x,y)=>new Color(.2f+.5f*x/64f,.1f+.4f*y/64f,.24f,.65f));
        backgroundNormal=Texture((x,y)=>new Color(.5f,1,.5f,1));
        depth=Texture((x,y)=>{float eye=6+40*y/63f;float raw=(1/eye-1/200f)/(1/.3f-1/200f);return new Color(raw,raw,raw,raw);},false);
        cube=new Cubemap(16,TextureFormat.RGBAFloat,true);for(int f=0;f<6;f++)cube.SetPixels(Enumerable.Repeat(new Color(.1f+f*.06f,.3f,.2f,1),256).ToArray(),(CubemapFace)f);cube.Apply();
    }
    private static Color[][] Render(Material material,int variant,int environment,int pass,bool probe=false,bool skipWater=false,float sampleTime=1.25f,int normalBand=-1)
    {
        var targets=new RenderTexture[5];var ids=new RenderTargetIdentifier[5];
        for(int i=0;i<5;i++){targets[i]=new RenderTexture(Size,Size,i==0?24:0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);targets[i].Create();ids[i]=targets[i];}
        var cb=new CommandBuffer {name="Water parity fixture"};
        string[] keywords={"Rain","WINTER_SNOW","UNITY_HDR_ON"};
        for(int k=0;k<3;k++) { if((variant&(1<<k))!=0){cb.EnableShaderKeyword(keywords[k]);material.EnableKeyword(keywords[k]);}else {cb.DisableShaderKeyword(keywords[k]);material.DisableKeyword(keywords[k]);} }
        for(int i=0;i<5;i++){cb.SetRenderTarget(targets[i]);cb.ClearRenderTarget(i==0,true,Clear);}
        cb.SetRenderTarget(ids,targets[0]);
        var block=new MaterialPropertyBlock();
        block.SetVector("_ValsNormA",new Vector4(7.9f,-5.3f,.071f,.367f));block.SetVector("_ValsNormB",new Vector4(-6.7f,4.7f,.0973f,.361f));
        block.SetVector("_ValsNormD",new Vector4(-2.3f,1.7f,.5f,.272f));block.SetVector("_ValsFoamA",new Vector4(2,-3,.12f,.4f));block.SetVector("_ValsFoamB",new Vector4(-3,1,.21f,.6f));
        if(normalBand>=0)
        {
            block.SetVector("_ValsNormA",new Vector4(7.9f,-5.3f,.071f,normalBand==0?.367f:0));
            block.SetVector("_ValsNormB",new Vector4(-6.7f,4.7f,.0973f,normalBand==1?.361f:0));
            block.SetVector("_ValsNormD",new Vector4(-2.3f,1.7f,.5f,normalBand==2?.272f:0));
        }
        block.SetTexture("_BumpMap",normal);block.SetTexture("_BumpMapDetails",detail);block.SetTexture("_FoamTex",foam);block.SetTexture("_RippleTex",ripple);
        block.SetFloat("_RippleScale",.8f);block.SetFloat("_RippleBumpness",.5f);block.SetFloat("_Bumpiness",.5f);
        block.SetVector("_DepthVals",new Vector4(2,.09f,.1f,.9f));block.SetVector("_BorderFadeDistVals",new Vector4(.02f,.2f,0,0));
        block.SetVector("_DepthColorDeep",new Vector4(.1f,.21f,.18f,1));block.SetVector("_DepthColorShallow",new Vector4(.3f,.4f,.25f,1));
        block.SetVector("_FoamVals",new Vector4(.1f,.5f,.7f,0));block.SetVector("_FoamColor",new Vector4(.7f,.7f,.65f,1));block.SetVector("_FresnelVals",new Vector4(.5f,3,0,0));
        cb.SetGlobalVector("_Time",new Vector4(sampleTime,sampleTime*20,sampleTime*40,sampleTime*60));
        cb.SetGlobalTexture("_CameraDepthTexture",depth);cb.SetGlobalTexture("_WaterForSSR_SavedG0",background);cb.SetGlobalTexture("_WaterForSSR_SavedG2",backgroundNormal);
        cb.SetGlobalTexture("_MyGlobalReflectionProbe",cube);cb.SetGlobalTexture("_RippleTex",ripple);
        cb.SetGlobalVector("_WaterRippleConfig",new Vector4(.6f,.8f,0,0));cb.SetGlobalVector("_WaterRippleCycle",new Vector4(.4f,.7f,.2f,0));
        cb.SetGlobalFloat("_ThermalVisionOn",environment==2?1:0);cb.SetGlobalFloat("SSRenabled",environment==1?0:1);cb.SetGlobalFloat("_ReflectionBottomShade",.4f);
        cb.SetGlobalVector("TOD_SunDirection",new Vector4(.3f,.8f,.4f,0));cb.SetGlobalVector("TOD_SunLightColor",environment==1?new Vector4(.01f,.02f,.04f,0):new Vector4(1,.8f,.6f,0));
        foreach(string name in new[]{"_SHAr","_SHAg","_SHAb"})cb.SetGlobalVector(name,new Vector4(.07f,.1f,.03f,environment==1?.02f:.3f));
        foreach(string name in new[]{"_SHBr","_SHBg","_SHBb","_SHC"})cb.SetGlobalVector(name,new Vector4(.01f,.02f,.01f,.01f));
        if(!skipWater)cb.DrawMesh(mesh,Matrix4x4.TRS(new Vector3(.2f,0,.4f),Quaternion.Euler(0,13,0),Vector3.one),material,0,pass,block);
        if(probe) {cb.SetRenderTarget(targets[0]);cb.DrawMesh(mesh,Matrix4x4.TRS(new Vector3(.2f,-.5f,.4f),Quaternion.Euler(0,13,0),Vector3.one),depthProbe);}
        camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque,cb);camera.Render();camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque,cb);cb.Dispose();
        var result=new Color[5][];var read=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);
        for(int i=0;i<5;i++){RenderTexture.active=targets[i];read.ReadPixels(new Rect(0,0,Size,Size),0,0);read.Apply();result[i]=read.GetPixels();}
        RenderTexture.active=null;Object.DestroyImmediate(read);foreach(var t in targets){t.Release();Object.DestroyImmediate(t);}return result;
    }
}
