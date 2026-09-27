using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Research
{
    public enum SsrProfilingMode { NativeMarkersOnly, PerPass }

    // Uses the research plugin's existing query session, generation checks, scope catalog and CSV export.
    // Never initializes a second native timing backend or submits queries from managed wall-clock timers.
    internal sealed class ResearchBridge
    {
        private readonly FieldInfo active, recording, camera, settings, window;
        private readonly MethodInfo begin, end;
        private readonly Func<SsrProfilingMode> mode;
        private readonly Action<string> error;
        private readonly ConditionalWeakTable<object, Session> sessions = new ConditionalWeakTable<object, Session>();
        private bool failed;
        private sealed class Session { internal SsrProfilingMode Mode; internal int LastWindow = int.MinValue; }

        internal ResearchBridge(Type deepType, Func<SsrProfilingMode> mode, Action<string> error)
        {
            this.mode = mode; this.error = error;
            active = Field(deepType, "active"); recording = Field(deepType, "recording");
            camera = Field(deepType, "camera"); settings = Field(deepType, "Settings"); window = Field(deepType, "Window");
            begin = AccessTools.Method(deepType, "BeginRecord", new[] { typeof(CommandBuffer), typeof(string), typeof(object) })
                ?? throw new MissingMethodException(deepType.FullName, "BeginRecord");
            end = AccessTools.Method(deepType, "EndRecord", new[] { begin.ReturnType })
                ?? throw new MissingMethodException(deepType.FullName, "EndRecord");
        }
        private static FieldInfo Field(Type t, string name) => AccessTools.Field(t, name) ?? throw new MissingFieldException(t.FullName, name);
        private static object Property(object value, string name) => AccessTools.Property(value.GetType(), name)?.GetValue(value);

        internal ISsrStageSink Select(object renderer, object context)
        {
            if (failed) return null;
            try
            {
                var deep = active.GetValue(null);
                if (deep == null || !(bool)recording.GetValue(deep)) return null;
                if (!ReferenceEquals(camera.GetValue(deep), Property(context, "camera"))) return null;
                var session = sessions.GetValue(deep, _ => new Session { Mode = mode() });
                int w = (int)window.GetValue(deep);
                if (session.LastWindow != w)
                {
                    var text = (StringBuilder)settings.GetValue(deep);
                    text.AppendLine("FTFX SSR profiling 0.1.0 window=" + w + " mode=" + session.Mode +
                        " internal=" + Property(context, "width") + "x" + Property(context, "height") +
                        " (mode is fixed for this capture; changes apply to the next capture)");
                    var effect = Property(renderer, "settings");
                    if (effect != null)
                        foreach (string name in new[] { "preset", "resolution", "maximumIterationCount", "thickness", "maximumMarchDistance", "distanceFade", "vignette", "angleRejection", "cutOffThreshold" })
                        {
                            var parameter = AccessTools.Field(effect.GetType(), name)?.GetValue(effect);
                            var value = parameter == null ? null : AccessTools.Field(parameter.GetType(), "value")?.GetValue(parameter);
                            text.AppendLine("SSR " + name + "=" + Convert.ToString(value, CultureInfo.InvariantCulture));
                        }
                    session.LastWindow = w;
                }
                return session.Mode == SsrProfilingMode.PerPass
                    ? new Sink(this, deep, renderer, (CommandBuffer)Property(context, "command")) : null;
            }
            catch (Exception e) { Fail(e); return null; }
        }

        private void Fail(Exception e)
        {
            if (failed) return;
            failed = true;
            error("SSR profiling failed; discard this capture's SSR breakdown. Native rendering continues. " + e);
        }
        private sealed class Sink : ISsrStageSink
        {
            private readonly ResearchBridge bridge;
            private readonly object deep, renderer;
            private readonly CommandBuffer commands;
            private object stamp;
            internal Sink(ResearchBridge bridge, object deep, object renderer, CommandBuffer commands)
            { this.bridge = bridge; this.deep = deep; this.renderer = renderer; this.commands = commands; }
            public void Begin(string name)
            {
                if (bridge.failed) return;
                try
                {
                    // Allocated by Render before the raymarch; captures actual square SSR dimensions.
                    var resolve = AccessTools.Field(renderer.GetType(), "m_Resolve")?.GetValue(renderer);
                    stamp = bridge.begin.Invoke(deep, new[] { commands, name, resolve });
                }
                catch (Exception e) { bridge.Fail(e); }
            }
            public void End()
            {
                if (stamp == null) return;
                try { bridge.end.Invoke(deep, new[] { stamp }); }
                catch (Exception e) { bridge.Fail(e); }
                finally { stamp = null; }
            }
        }
    }
}
