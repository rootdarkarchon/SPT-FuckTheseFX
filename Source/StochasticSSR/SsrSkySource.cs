using System;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace FuckTheseFX.Source.StochasticSSR
{
    // Optional shader-global contract; no reference to CloudSix's assembly or renderer.
    internal sealed class SsrSkySource
    {
        private Type config;
        private FieldInfo customSky,publish,temporal;
        private int assemblyCount=-1;

        internal Texture Read()
        {
            var texture=Shader.GetGlobalTexture("_SsrSkyReflMap");
            if(texture==null || texture.dimension!=UnityEngine.Rendering.TextureDimension.Tex2D ||
               texture.width<2 || texture.height<2 || (texture is RenderTexture rt && !rt.IsCreated()))return null;
            try {
                // CloudSix 4.0 leaves its last map bound when publication is disabled.
                // Read the live entries instead of changing the producer's globals/resources.
                if(config==null) {
                    var assemblies=AppDomain.CurrentDomain.GetAssemblies();
                    if(assemblies.Length!=assemblyCount) {
                        assemblyCount=assemblies.Length;
                        foreach(var assembly in assemblies) {
                            config=assembly.GetType("CloudSix.Source.CloudConfig",false);
                            if(config==null)continue;
                            const BindingFlags flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
                            customSky=config.GetField("CustomSky",flags);publish=config.GetField("PublishSkyReflections",flags);
                            temporal=config.GetField("TemporalMode",flags);break;
                        }
                    }
                }
                if(config!=null && !Publishing(customSky?.GetValue(null) as ConfigEntryBase,
                    publish?.GetValue(null) as ConfigEntryBase,temporal?.GetValue(null) as ConfigEntryBase))return null;
                return texture;
            } catch { return null; } // An incompatible optional provider must preserve TOD reflections.
        }

        internal static bool Publishing(ConfigEntryBase sky,ConfigEntryBase publish,ConfigEntryBase mode)=>
            sky?.BoxedValue is bool skyOn && skyOn && publish?.BoxedValue is bool publishOn && publishOn &&
            mode?.BoxedValue?.ToString()=="SkyDome";
    }
}
