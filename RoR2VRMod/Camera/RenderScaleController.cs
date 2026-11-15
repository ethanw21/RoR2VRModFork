using RoR2;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace VRMod
{
    /// <summary>
    /// "VR render scale" ([Performance] in the config, also on the VR settings tab): multiplies the
    /// eye texture size the OpenXR runtime asks for. Pixel count is the largest GPU cost in VR and
    /// the mod had no control over it at all - the game rendered at whatever the runtime requested.
    ///
    /// The scale is applied through the display subsystem (and XRSettings.eyeTextureResolutionScale
    /// if that did not follow). Unity reallocates the eye textures whenever it changes, which is a
    /// visible hitch, so it is a setting applied once, not something adjusted per frame. Unity's
    /// cheap per-frame alternative, XRSettings.renderViewportScale, is documented as unsupported
    /// with deferred rendering, which is what this game uses.
    /// </summary>
    internal static class RenderScaleController
    {
        internal const float MinScale = 0.5f;
        internal const float MaxScale = 1.5f;

        private static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        private static XRDisplaySubsystem appliedDisplay;
        private static float appliedScale = 1f;

        private static float settleAt;
        private static float nextCheck;

        internal static void Init()
        {
            RoR2Application.onUpdate += Update;
        }

        /// <summary>The slider fires this on every step while it is dragged. Applying each step
        /// would rebuild the eye textures over and over, so wait until the value has rested.</summary>
        internal static void OnSettingChanged(object sender, EventArgs e)
        {
            settleAt = Time.unscaledTime + 0.75f;
            nextCheck = 0f;
        }

        private static void Update()
        {
            float now = Time.unscaledTime;
            if (now < nextCheck || now < settleAt) return;
            nextCheck = now + 1f;

            XRDisplaySubsystem display = RunningDisplay();
            if (display == null) return;

            // A display that has just started (first launch, or the runtime restarted the session)
            // is at the runtime's own resolution again.
            if (!ReferenceEquals(display, appliedDisplay))
            {
                appliedDisplay = display;
                appliedScale = 1f;
                VRMod.StaticLogger.LogInfo($"[VR render scale] Runtime eye texture: {XRSettings.eyeTextureWidth} x {XRSettings.eyeTextureHeight} per eye at scale 1.");
            }

            float wanted = Mathf.Clamp(ModConfig.RenderScale.Value, MinScale, MaxScale);
            if (Mathf.Abs(wanted - appliedScale) < 0.005f) return;

            // Recorded before the attempt: if the runtime refuses the value, do not retry every
            // second (each attempt can rebuild the eye textures).
            appliedScale = wanted;

            try
            {
                display.scaleOfAllRenderTargets = wanted;

                if (Mathf.Abs(XRSettings.eyeTextureResolutionScale - wanted) > 0.005f)
                    XRSettings.eyeTextureResolutionScale = wanted;

                VRMod.StaticLogger.LogInfo($"[VR render scale] Eye texture scale set to {wanted:F2}. The new size shows in the next '[VR perf]' line when 'Log frame times' is on.");
            }
            catch (Exception e)
            {
                VRMod.StaticLogger.LogWarning($"[VR render scale] Could not set the eye texture scale to {wanted:F2}: {e.Message}");
            }
        }

        private static XRDisplaySubsystem RunningDisplay()
        {
            SubsystemManager.GetInstances(displays);

            for (int i = 0; i < displays.Count; i++)
            {
                if (displays[i] != null && displays[i].running) return displays[i];
            }

            return null;
        }
    }
}
