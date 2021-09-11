using AmplifyOcclusion;
using HarmonyLib;
using NeosModLoader;
using System;
using System.Reflection;
using UnityEngine;

namespace SsaoDisable
{
    public class SsaoDisable : NeosMod
    {
        public override string Name => "SsaoDisable";
        public override string Author => "runtime";
        public override string Version => "1.0.2";
        public override string Link => "https://github.com/zkxs/SsaoDisable";

        private static bool _first_trigger = false;
        private static bool render_texture_safe = false;

        public override void OnEngineInit()
        {
            // disable future SSAO by patching the setup method
            Harmony harmony = new Harmony("net.michaelripley.SsaoDisable");
            MethodInfo originalMethod = AccessTools.DeclaredMethod(typeof(CameraInitializer), nameof(CameraInitializer.SetPostProcessing), new Type[] { typeof(Camera), typeof(bool), typeof(bool), typeof(bool) });
            if (originalMethod == null)
            {
                Error("Could not find CameraInitializer.SetPostProcessing(Camera, bool, bool, bool)");
                return;
            }
            MethodInfo replacementMethod = AccessTools.DeclaredMethod(typeof(SsaoDisable), nameof(SetPostProcessingPostfix));

            MethodInfo buggedMethod = AccessTools.DeclaredMethod(typeof(AmplifyOcclusionCommon), nameof(AmplifyOcclusionCommon.SafeReleaseRT));
            if (buggedMethod == null)
            {
                Error("Could not find AmplifyOcclusionCommon.SafeReleaseRT(ref RenderTexture)");
                return;
            }
            MethodInfo buggedMethodPatch = AccessTools.DeclaredMethod(typeof(SsaoDisable), nameof(SsaoDisable.SafeReleaseRenderTextureBugfixPrefix));

            harmony.Patch(originalMethod, postfix: new HarmonyMethod(replacementMethod));
            harmony.Patch(buggedMethod, prefix: new HarmonyMethod(buggedMethodPatch));

            Msg("Hooks installed successfully");

            // disable prexisting SSAO by searching for all matching Unity components
            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
            int count = 0;
            foreach (Camera camera in cameras)
            {
                try
                {
                    var ssao = camera.GetComponent<AmplifyOcclusionEffect>();
                    if (ssao != null && ssao.enabled)
                    {
                        ssao.enabled = false;
                        count += 1;
                    }
                }
                catch(Exception e)
                {
                    Warn($"failed to disable a prexisting SSAO: {e}");
                }
            }
            Msg($"disabled {count} prexisting SSAOs");
            render_texture_safe = true;
        }

        private static void SetPostProcessingPostfix(Camera c, bool enabled, bool motionBlur, bool screenspaceReflections)
        {
            try
            {
                AmplifyOcclusionEffect ssao = c.GetComponent<AmplifyOcclusionEffect>();
                if (ssao != null)
                {
                    ssao.enabled = false;

                    if (!_first_trigger)
                    {
                        _first_trigger = true;
                        Msg("Hook triggered! Everything worked!");
                    }
                }
            }
            catch (Exception e)
            {
                Warn($"failed to disable a new SSAO: {e}");
            }
        }

        private static bool SafeReleaseRenderTextureBugfixPrefix(ref RenderTexture rt)
        {
            if (rt == null)
            {
                return false;
            }

            // short circuiting is important here, as even thinking about looking at RenderTexture.active too early will crash unity
            if (render_texture_safe && RenderTexture.active == rt)
            {
                // nulling this might be crashy, but it might be worse to not null it?
                RenderTexture.active = null; 
            }

            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            rt = null;

            return false;
        }
    }
}
