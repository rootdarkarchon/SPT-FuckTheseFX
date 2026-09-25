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
public class AmbientLight { public class CamSettings { } private Material _screenAmbientMaterial; public void InitAmbientBuffer(CamSettings settings,Camera camera) { } }
