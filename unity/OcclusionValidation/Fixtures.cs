using BepInEx.Logging;
using UnityEngine;
namespace FuckTheseFX { internal static class Plugin { internal const string Guid="ftfx.fixture"; internal static ManualLogSource MyLog = new ManualLogSource("FTFX fixture"); } }
namespace EFT.Settings.Graphics { public enum ESSAOMode { Off,FastestPerformance,FastPerformance,HighQuality,HighestQuality,ColoredHighestQuality } }
namespace EFT.CameraControl
{
 public sealed class NativeAOFixture : MonoBehaviour { }
 public sealed class CameraManager
 {
  public Camera Camera;
  public Light _hbao, _ambientOcclusion;
  [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
  public void SetSSAO(EFT.Settings.Graphics.ESSAOMode mode) { _hbao.enabled=mode!=EFT.Settings.Graphics.ESSAOMode.Off; _ambientOcclusion.enabled=_hbao.enabled; }
 }
}
namespace EFT.UI { public class PreloaderUI { public void ShowRaidStartInfo() { } } }
public class AmbientLight
{
 public class CamSettings { public UnityEngine.Rendering.CommandBuffer CbAmbient = new UnityEngine.Rendering.CommandBuffer(); }
 private Material _screenAmbientMaterial;
 public Mesh Quad;
 public AmbientLight(Material material=null) { _screenAmbientMaterial=material; }
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 public void InitAmbientBuffer(CamSettings settings,Camera camera)
 {
  settings.CbAmbient.Clear();settings.CbAmbient.SetRenderTarget(UnityEngine.Rendering.BuiltinRenderTextureType.CameraTarget);
  settings.CbAmbient.DrawMesh(Quad,Matrix4x4.identity,_screenAmbientMaterial,0,0,null);
 }
}
