using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.StochasticSSR
{
    // Opt-in, command-ordered one-frame readback. No synchronous GPU wait.
    internal sealed class ContinuationCapture : IDisposable
    {
        private bool armed=true,disposed,pending;
        private int lastMode;
        private readonly List<Action> retired=new List<Action>();
        internal bool Pending=>pending;
        // A resize or disposal may detach targets while this frame is being
        // read. Keep their GPU allocation alive until every callback completes.
        internal void ReleaseWhenComplete(Action release) {if(pending)retired.Add(release);else release();}
        internal void Rearm(bool enabled) { if(!enabled)armed=true; }
        internal void Rearm(bool continuation,bool traversal) {
            int mode=(continuation?1:0)|(traversal?2:0);
            if(mode!=lastMode)armed=true;
            lastMode=mode;Rearm(mode!=0);
        }
        internal bool Wants(bool enabled)=>enabled && armed && !pending && !disposed;
        internal void Request(CommandBuffer cb,Dictionary<string,RenderTexture> targets,Options o,int cameraWidth,int cameraHeight,string replayMetadata="",Func<string> lateMetadata=null)
        {
            armed=false;pending=true;
            var data=new Dictionary<string,Color[]>();var sizes=new Dictionary<string,Vector2Int>();
            int remaining=targets.Count;bool failed=false;
            string metadata="Schema=6\nRangeSemantics=CameraToReceiverRadial\nExcludedReceiver=miss_0 (shared with material/depth exclusion)\nRecordGeometryXY=integer camera source cell\n"+replayMetadata+Environment.NewLine+"Camera="+cameraWidth+"x"+cameraHeight+" frame="+Time.frameCount+Environment.NewLine+
                string.Join(Environment.NewLine,typeof(Options).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Select(f=>f.Name+"="+Convert.ToString(f.GetValue(o),CultureInfo.InvariantCulture)));
            foreach(var item in targets) {
                string name=item.Key;sizes[name]=new Vector2Int(item.Value.width,item.Value.height);
                cb.RequestAsyncReadback(item.Value,0,TextureFormat.RGBAFloat,r=> {
                    remaining--;
                    try {
                        if(disposed)return;
                        if(r.hasError)failed=true;
                        else {var pixels=r.GetData<Color>();var copy=new Color[pixels.Length];pixels.CopyTo(copy);data[name]=copy;}
                        if(remaining!=0)return;
                        if(failed) {Plugin.MyLog.LogWarning("SSR continuation capture failed GPU readback; toggle capture off/on to retry.");return;}
                        string folder=Path.Combine(BepInEx.Paths.BepInExRootPath,"FTFX-SSR-Captures",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture));
                        Save(folder,data,sizes,metadata+(lateMetadata!=null?lateMetadata():""));
                        Plugin.MyLog.LogInfo("SSR continuation diagnostics saved: "+folder);
                    } catch(Exception e) {failed=true;Plugin.MyLog.LogWarning("SSR continuation capture: "+e);}
                    finally {if(remaining==0){pending=false;foreach(var release in retired)release();retired.Clear();}}
                });
            }
        }
        internal static void Save(string folder,Dictionary<string,Color[]> data,Dictionary<string,Vector2Int> sizes,string metadata)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"settings.txt"),metadata);
            Color[] info=data["trace-cost"],records=data["trace-record"],stop=data["first-rejection"],guide=data["trace-guide"],resolved=data["resolved"],filtered=data["filtered"];
            if(data.TryGetValue("traversal-work",out var work) && data.TryGetValue("traversal-topology",out var topology))
                SaveTraversal(folder,work,topology,records,info,sizes["traversal-work"].x);
            var histogram=new SortedDictionary<string,int>();var rows=new StringBuilder("x,y,flags,attempted_samples,skipped_pixels,max_depth_separation,kind,reason_or_fraction,raw_confidence,resolved_confidence,filtered_confidence,first_rejected_u,first_rejected_v,ray_depth,scene_depth\n");
            int recovered=0,contributing=0,limited=0;
            for(int i=0;i<info.Length;i++) {
                int flags=(int)info[i].a;if((flags&8)!=0)limited++;
                if((flags&4)!=0) {
                    recovered++;
                    if(guide[i].r>0)contributing++;
                    foreach(var key in new[]{"samples="+info[i].r.ToString("0",CultureInfo.InvariantCulture),"pixels_ceil="+Math.Ceiling(info[i].g).ToString(CultureInfo.InvariantCulture),"depth_metres_ceil="+Math.Ceiling(info[i].b).ToString(CultureInfo.InvariantCulture),"cause="+(flags&3)}) {
                        histogram.TryGetValue(key,out int count);histogram[key]=count+1;
                    }
                }
                if(flags==0)continue;
                float[] fields={i%sizes["trace-cost"].x,i/sizes["trace-cost"].x,flags,info[i].r,info[i].g,info[i].b,records[i].a,records[i].b,guide[i].r,resolved[i].a,filtered[i].a,stop[i].r,stop[i].g,stop[i].b,stop[i].a};
                rows.AppendLine(string.Join(",",fields.Select(v=>v.ToString("R",CultureInfo.InvariantCulture))));
            }
            File.WriteAllText(Path.Combine(folder,"continued-rays.csv"),rows.ToString());
            if(data.TryGetValue("depth-hit",out var depthHit)) {
                var hitRows=new StringBuilder("x,y,flags,original_depth_confidence,normal_confidence,edge_confidence,raw_confidence,resolved_confidence,filtered_confidence,source_cell_x,source_cell_y\n");
                int bracketed=0,thicknessOnly=0,crossCell=0;double depthLoss=0;
                for(int i=0;i<depthHit.Length;i++) {
                    int flags=(int)depthHit[i].r;if(flags==0 || records[i].a!=1)continue;
                    if((flags&1)!=0)bracketed++;if((flags&2)!=0)thicknessOnly++;if((flags&4)!=0)crossCell++;
                    depthLoss+=1-depthHit[i].g;
                    float[] fields={i%sizes["depth-hit"].x,i/sizes["depth-hit"].x,flags,depthHit[i].g,depthHit[i].b,depthHit[i].a,guide[i].r,resolved[i].a,filtered[i].a,records[i].r,records[i].g};
                    hitRows.AppendLine(string.Join(",",fields.Select(v=>v.ToString("R",CultureInfo.InvariantCulture))));
                }
                File.WriteAllText(Path.Combine(folder,"depth-hits.csv"),hitRows.ToString());
                File.WriteAllLines(Path.Combine(folder,"depth-hit-summary.csv"),new[]{"metric,value","bracketed,"+bracketed,"thickness_only,"+thicknessOnly,"baseline_uv_crossed_cell,"+crossCell,"original_depth_confidence_deficit,"+depthLoss.ToString("R",CultureInfo.InvariantCulture)});
            }
            File.WriteAllLines(Path.Combine(folder,"recovered-histograms.csv"),new[]{"metric,count","recovered,"+recovered,"recovered_positive_confidence,"+contributing,"limit_exhausted,"+limited,"sky_after_continuation,"+records.Select((v,i)=>v.a==2 && ((int)info[i].a&64)!=0).Count(v=>v),"direct_sky,"+records.Select((v,i)=>v.a==2 && ((int)info[i].a&64)==0).Count(v=>v)}.Concat(histogram.Select(v=>v.Key+","+v.Value)));


            if(data.TryGetValue("scene-source",out var sceneInput)) {
                var checks=new List<string>{"input,pixels,identical_to_scene"};
                foreach(string name in new[]{"gbuffer0","gbuffer1","gbuffer2","motion"}) {
                    if(!data.TryGetValue(name,out var input))continue;
                    bool identical=input.Length==sceneInput.Length && Enumerable.Range(0,input.Length).All(i=>input[i].Equals(sceneInput[i]));
                    checks.Add(name+","+input.Length+","+identical);
                }
                File.WriteAllLines(Path.Combine(folder,"capture-input-validation.csv"),checks);
            }
            foreach(var item in data) {
                var size=sizes[item.Key];var texture=new Texture2D(size.x,size.y,TextureFormat.RGBAFloat,false,true);
                try {texture.SetPixels(item.Value);texture.Apply();File.WriteAllBytes(Path.Combine(folder,item.Key+".exr"),texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat|Texture2D.EXRFlags.CompressZIP));}
                finally {UnityEngine.Object.DestroyImmediate(texture);}
            }
            File.WriteAllText(Path.Combine(folder,"COMPLETE.txt"),"Schema 6: camera-to-receiver radial eligibility; reason 7 retired. One command-ordered frame. Optional traversal-work/topology replace sample-direction-pdf/depth-hit; see traversal-README.txt. Geometry record XY are exact integer camera source cells; sky XY are UV. Flags: 1 depth, 2 facing, 4 recovered, 8 continuation limit, 64 completed-continuation sky. Coverage-stage RGB: raw, measured resolved/temporal, final reconstructed coverage. gap-fill-coverage RGB: pre-reconstruction measured coverage, additional display coverage (filtering only; no rejected-source fill), remaining fallback. filtered contains the NRD estimate; rejected-source colour filling is disabled; no custom temporal histories are retained. depth-hit RGBA: accepted-hit flags (1 bracketed, 2 thickness-only, 4 tested-cell/source-UV mismatch), original depth confidence, normal confidence, edge confidence. Reflection RGB is premultiplied by coverage; EXRs are linear data. Trace depth is camera eye metres at camera extent (ceil-reduced hierarchy). sample-direction-pdf XYZ is the original view ray; W is the GGX VNDF PDF, zero for perfect mirrors. Baked behavior and live options are recorded in settings.txt. No rolling temporal metadata or display-estimate history is recorded.");
        }
        private static string Termination(Color record)=>record.a==1?"geometry":record.a==2?"sky":"miss_"+((int)record.b).ToString(CultureInfo.InvariantCulture);
        internal static void SaveTraversal(string folder,Color[] work,Color[] topology,Color[] records,Color[] continuation,int width)
        {
            long total=work.Sum(v=>(long)v.r),continuationWork=work.Sum(v=>(long)v.g);
            var groups=new SortedDictionary<string,List<int>>();var continuationSums=new Dictionary<string,long>();
            var histogram=new SortedDictionary<string,int>();var levelRays=new long[14];
            using(var writer=new StreamWriter(Path.Combine(folder,"traversal-rays.csv"))) {
                writer.WriteLine("x,y,termination,eligible,iterations,after_first_continuation,descents,mip0_visits,visited_mip_mask,repeated_fine_cell_visits,rejection_attempts,continued_samples,continued_pixels,max_depth_separation,flags");
                for(int i=0;i<work.Length;i++) {
                    string reason=Termination(records[i]);int count=(int)work[i].r;
                    if(!groups.TryGetValue(reason,out var counts)){counts=new List<int>();groups.Add(reason,counts);continuationSums.Add(reason,0);}
                    counts.Add(count);continuationSums[reason]+=(long)work[i].g;
                    string key=reason+","+count;histogram.TryGetValue(key,out int n);histogram[key]=n+1;
                    int mask=(int)topology[i].g;for(int level=0;level<14;level++)if((mask&(1<<level))!=0)levelRays[level]++;
                    float[] fields={i%width,i/width,work[i].a,work[i].r,work[i].g,work[i].b,topology[i].r,topology[i].g,topology[i].b,topology[i].a,continuation[i].r,continuation[i].g,continuation[i].b,continuation[i].a};
                    writer.WriteLine(fields[0]+","+fields[1]+","+reason+","+string.Join(",",fields.Skip(2).Select(v=>v.ToString("R",CultureInfo.InvariantCulture))));
                }
            }
            var summary=new List<string>{"termination,rays,ray_share,iterations,work_share,mean_iterations,median_iterations,p95_iterations,max_iterations,after_first_continuation_iterations"};
            foreach(var group in groups) {
                group.Value.Sort();long iterations=group.Value.Sum(v=>(long)v);int n=group.Value.Count;
                double[] values={n,(double)n/work.Length,iterations,total>0?(double)iterations/total:0,(double)iterations/n,
                    n%2==0?(group.Value[n/2-1]+group.Value[n/2])*.5:group.Value[n/2],group.Value[(int)Math.Ceiling(n*.95)-1],group.Value[n-1],continuationSums[group.Key]};
                summary.Add(group.Key+","+string.Join(",",values.Select(v=>v.ToString("R",CultureInfo.InvariantCulture))));
            }
            File.WriteAllLines(Path.Combine(folder,"traversal-summary.csv"),summary);
            File.WriteAllLines(Path.Combine(folder,"traversal-histogram.csv"),new[]{"termination,iterations,rays"}.Concat(histogram.Select(v=>v.Key+","+v.Value)));
            File.WriteAllLines(Path.Combine(folder,"traversal-mips.csv"),new[]{"mip,rays_visiting"}.Concat(levelRays.Select((v,i)=>i+","+v)));
            File.WriteAllText(Path.Combine(folder,"traversal-README.txt"),"Work RGBA: executed loop bodies, loop bodies after an earlier continuation rejection, descents, valid eligible receiver. Topology RGBA: mip-zero visits, visited-mip bit mask, repeated consecutive fine-cell visits, rejection attempts. Counts include misses and budget exhaustion; excluded/invalid receivers retain zero work. Repeated fine-cell visits are a cell-revisit proxy, not wave occupancy. Mip masks report visits, not per-level iteration counts. Join final trace-record/trace-cost for recovered geometry, completed sky and stop reasons. Diagnostic shader replaces ray/PDF and depth-hit outputs, increases register pressure, and must not certify speedups. Total iterations="+total+"; after-first-continuation="+continuationWork+".\n");
        }
        public void Dispose(){disposed=true;}
    }
}


