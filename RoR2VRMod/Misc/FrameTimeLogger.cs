using RoR2;
using System;
using UnityEngine;
using UnityEngine.XR;

namespace VRMod
{
    /// <summary>
    /// "Log frame times" ([Performance] in the config, also on the VR settings tab). While it is on,
    /// one line goes to the BepInEx log every 5 seconds: average and worst frame time, how many
    /// frames ran well over the headset's frame budget, and how many garbage collections happened.
    /// It exists so a change can be judged by numbers taken before and after it, on the same
    /// stage, instead of by feel. Costs nothing while off.
    /// </summary>
    internal static class FrameTimeLogger
    {
        private const float ReportInterval = 5f;

        private static int frames;
        private static float windowStart;
        private static double frameTimeSum;
        private static float worstFrame;
        private static int slowFrames;
        private static int collectionsAtStart;
        private static float slowThreshold;
        private static bool loggedCollector;

        internal static void Init()
        {
            RoR2Application.onUpdate += Update;
        }

        private static void Update()
        {
            if (!ModConfig.PerformanceLog.Value)
            {
                frames = 0;
                return;
            }

            float now = Time.unscaledTime;
            float frameTime = Time.unscaledDeltaTime;

            if (frames == 0)
            {
                windowStart = now;
                frameTimeSum = 0.0;
                worstFrame = 0f;
                slowFrames = 0;
                collectionsAtStart = GC.CollectionCount(0);
                slowThreshold = FrameBudget() * 1.5f;

                if (!loggedCollector)
                {
                    loggedCollector = true;
                    VRMod.StaticLogger.LogInfo($"[VR perf] Frame time logging on. Headset refresh rate {1f / FrameBudget():F0} Hz, incremental garbage collector: {UnityEngine.Scripting.GarbageCollector.isIncremental}.");
                }

                // The first frame of a window only starts the clock.
                frames = 1;
                return;
            }

            frames++;
            frameTimeSum += frameTime;
            if (frameTime > worstFrame) worstFrame = frameTime;
            if (frameTime > slowThreshold) slowFrames++;

            float elapsed = now - windowStart;
            if (elapsed < ReportInterval) return;

            int measured = frames - 1;
            int collections = GC.CollectionCount(0) - collectionsAtStart;
            long heapMB = GC.GetTotalMemory(false) / (1024L * 1024L);
            string scene = SceneCatalog.mostRecentSceneDef ? SceneCatalog.mostRecentSceneDef.cachedName : "?";

            VRMod.StaticLogger.LogInfo(
                $"[VR perf] {scene}: {measured} frames in {elapsed:F1} s, avg {frameTimeSum / measured * 1000.0:F1} ms, worst {worstFrame * 1000f:F1} ms, " +
                $"{slowFrames} over {slowThreshold * 1000f:F1} ms, {collections} GC, managed heap {heapMB} MB, " +
                $"eye texture {XRSettings.eyeTextureWidth} x {XRSettings.eyeTextureHeight}.");

            frames = 0;
        }

        /// <summary>Seconds per frame at the headset's refresh rate (90 Hz when it is not reported).</summary>
        private static float FrameBudget()
        {
            float hz = 0f;
            try { hz = XRDevice.refreshRate; } catch { }
            if (float.IsNaN(hz) || hz < 30f || hz > 240f) hz = 90f;
            return 1f / hz;
        }
    }
}
