using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using FuckTheseFX.Source.Sharpen;
using FuckTheseFX.Source.Vintage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Exercise the actual integration files with real Unity scene/component APIs.
// Edit-mode fixture dispatches sceneLoaded/preCull callbacks explicitly: no rendering required.
public static class FilterSceneValidation
{
    private static int checks;

    public static void Run()
    {
        string result = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "result.txt");
        try
        {
            Validate(typeof(VintageFilterIntegration), typeof(CC_Vintage), FuckTheseFX.Plugin.VintageEnabled);
            Validate(typeof(SharpenFilterIntegration), typeof(CC_Sharpen), FuckTheseFX.Plugin.SharpenEnabled);
            File.WriteAllText(result, "PASS: " + checks + " checks; actual Vintage/Sharpen integration source, real Unity scenes/components, startup/config, additive inactive effects, ownership, reentrant scene callback, camera cache/fallback, disposal, invalid/unloaded scenes and scene-handler no-global-scan reachability. Callbacks explicitly dispatched in Unity Editor Mono; live EFT not tested.\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(result, "FAIL: " + error);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void Validate(Type integrationType, Type effectType, FuckTheseFX.TestSetting setting)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Scene original = SceneManager.GetActiveScene();
        Behaviour initial = Effect(original, effectType, true);
        Behaviour preDisabled = Effect(original, effectType, false);
        EditorSceneManager.SaveScene(original, "Assets/Original.unity");
        setting.Value = false;
        object integration = Activator.CreateInstance(integrationType, true);
        Call(integration, "Initialize");
        Check(!initial.enabled && !preDisabled.enabled, "startup false suppresses only enabled components");
        AssertNoGlobalScan(integrationType);

        Scene added = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        Behaviour addedOne = Effect(added, effectType, true);
        Behaviour addedTwo = (Behaviour)addedOne.gameObject.AddComponent(effectType);
        var inactiveRoot = new GameObject("inactive root");
        SceneManager.MoveGameObjectToScene(inactiveRoot, added);
        var inactiveChild = new GameObject("inactive child");
        inactiveChild.transform.SetParent(inactiveRoot.transform);
        Behaviour inactive = (Behaviour)inactiveChild.AddComponent(effectType);
        inactiveRoot.SetActive(false);
        Behaviour addedPreDisabled = Effect(added, effectType, false);
        initial.enabled = true;
        SceneEvent(integration, added);
        Check(initial.enabled, "additive callback does not revisit earlier scene");
        Check(!addedOne.enabled && !addedTwo.enabled && !inactive.enabled && !addedPreDisabled.enabled,
            "additive traversal includes multiple effects and inactive descendants");

        setting.Value = true;
        Check(initial.enabled && addedOne.enabled && addedTwo.enabled && inactive.enabled,
            "true restores owned effects across scenes");
        Check(!preDisabled.enabled && !addedPreDisabled.enabled, "true preserves pre-disabled ownership");
        setting.Value = false;
        Check(!initial.enabled && !addedOne.enabled && !inactive.enabled, "false config globally reapplies");

        // Restore branch must still restore all remembered effects, even for an invalid scene.
        setting.SetWithoutEvent(true);
        SceneEvent(integration, default(Scene));
        Check(initial.enabled && addedOne.enabled && !preDisabled.enabled, "enabled scene callback restores ownership");
        setting.Value = false;

        var cameraObject = new GameObject("later camera");
        SceneManager.MoveGameObjectToScene(cameraObject, original);
        Camera camera = cameraObject.AddComponent<Camera>();
        Behaviour later = (Behaviour)cameraObject.AddComponent(effectType);
        Call(integration, "OnCameraPreCull", camera);
        Check(!later.enabled, "late camera pre-cull fallback");
        Behaviour newlyAttached = (Behaviour)cameraObject.AddComponent(effectType);
        SceneManager.MoveGameObjectToScene(cameraObject, added);
        SceneEvent(integration, original);
        Check(newlyAttached.enabled, "scene callback does not scan moved camera in another scene");
        Call(integration, "OnCameraPreCull", camera);
        Check(!newlyAttached.enabled, "scene callback invalidates cache for moved camera fallback");

        // Disabling an effect can synchronously invoke another scene callback.
        EditorSceneManager.SaveScene(added, "Assets/Added.unity");
        Scene nested = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        Behaviour nestedEffect = Effect(nested, effectType, true);
        Behaviour trigger = Effect(added, effectType, true);
        Behaviour afterTrigger = Effect(added, effectType, true);
        FilterDisableCallback.Callback = () => SceneEvent(integration, nested);
        SceneEvent(integration, added);
        FilterDisableCallback.Callback = null;
        Check(!trigger.enabled && !afterTrigger.enabled && !nestedEffect.enabled, "method-local traversal survives reentrant callbacks");

        SceneEvent(integration, default(Scene));
        EditorSceneManager.CloseScene(nested, true);
        SceneEvent(integration, nested);
        Check(!initial.enabled, "invalid/unloaded callbacks are safe");
        ((IDisposable)integration).Dispose();
        Check(initial.enabled && addedOne.enabled && later.enabled && newlyAttached.enabled,
            "dispose restores surviving owned effects");
        Check(!preDisabled.enabled && !addedPreDisabled.enabled, "dispose preserves pre-disabled effects");
        setting.Value = true;
        setting.Value = false;
        Check(initial.enabled && addedOne.enabled, "dispose unsubscribes configuration callback");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // True at initialization leaves native component states untouched.
        Behaviour trueInitial = Effect(SceneManager.GetActiveScene(), effectType, true);
        setting.Value = true;
        integration = Activator.CreateInstance(integrationType, true);
        Call(integration, "Initialize");
        Check(trueInitial.enabled, "startup true keeps native enabled state");
        ((IDisposable)integration).Dispose();
    }

    private static Behaviour Effect(Scene scene, Type type, bool enabled)
    {
        var gameObject = new GameObject(type.Name);
        SceneManager.MoveGameObjectToScene(gameObject, scene);
        var effect = (Behaviour)gameObject.AddComponent(type);
        effect.enabled = enabled;
        return effect;
    }

    private static void SceneEvent(object target, Scene scene) => Call(target, "OnSceneLoaded", scene, LoadSceneMode.Additive);

    private static void Call(object target, string name, params object[] arguments)
    {
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
    }

    private static void Check(bool success, string description)
    {
        if (!success) throw new Exception(description);
        checks++;
    }

    // Walk actual managed call instructions reachable from the scene handler, including
    // compiler-generated lambdas on the same integration type. Reject global enumeration.
    private static void AssertNoGlobalScan(Type type)
    {
        var opcodes = new Dictionary<short, OpCode>();
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.FieldType == typeof(OpCode)) { var opcode = (OpCode)field.GetValue(null); opcodes[opcode.Value] = opcode; }
        var pending = new Queue<MethodBase>();
        var seen = new HashSet<MethodBase>();
        pending.Enqueue(type.GetMethod("OnSceneLoaded", BindingFlags.Instance | BindingFlags.NonPublic));
        while (pending.Count > 0)
        {
            MethodBase method = pending.Dequeue();
            if (!seen.Add(method)) continue;
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) continue;
            for (int i = 0; i < il.Length;)
            {
                short key = il[i++];
                if (key == 0xfe) key = (short)(0xfe00 | il[i++]);
                OpCode opcode = opcodes[key];
                if (opcode.OperandType == OperandType.InlineMethod)
                {
                    MethodBase called = method.Module.ResolveMethod(BitConverter.ToInt32(il, i));
                    Check(!(called.DeclaringType == typeof(Resources) && called.Name.StartsWith("FindObjectsOfType")), "scene callback reaches global scan");
                    if (called.DeclaringType == type || called.DeclaringType.DeclaringType == type) pending.Enqueue(called);
                }
                switch (opcode.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: i++; break;
                    case OperandType.InlineVar: i += 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: i += 8; break;
                    case OperandType.InlineSwitch: i += 4 + 4 * BitConverter.ToInt32(il, i); break;
                    default: i += 4; break;
                }
            }
        }
    }
}

[ExecuteAlways]
public class CC_Vintage : MonoBehaviour { private void OnDisable() => FilterDisableCallback.Invoke(); }
[ExecuteAlways]
public class CC_Sharpen : MonoBehaviour { private void OnDisable() => FilterDisableCallback.Invoke(); }
public class FilterDisableCallback : MonoBehaviour
{
    public static Action Callback;
    public static void Invoke() { Action callback = Callback; Callback = null; callback?.Invoke(); }
}

namespace FuckTheseFX
{
    internal static class Plugin
    {
        internal static readonly TestSetting VintageEnabled = new TestSetting();
        internal static readonly TestSetting SharpenEnabled = new TestSetting();
    }
    internal sealed class TestSetting
    {
        private bool value;
        internal bool Value { get => value; set { this.value = value; SettingChanged?.Invoke(this, EventArgs.Empty); } }
        internal event EventHandler SettingChanged;
        internal void SetWithoutEvent(bool state) => value = state;
    }
}
