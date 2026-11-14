using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VRMod
{
    public class ConfortVignette : MonoBehaviour
    {
        internal static ConfortVignette instance { get; private set; }

        private static Material vignette;

        private const float INITIAL_RADIUS = 0.7f;
        private const float TARGET_RADIUS = 0.4f;
        private const float RADIUS_CHANGE_PER_SECOND = 3f;

        private static float currentRadius = INITIAL_RADIUS;
        private static float currentTargetRadius = INITIAL_RADIUS;

        private static bool activated;
        private static bool wasActivated;

        private List<EntityStateMachine> stateMachines;

        private bool foundStateMachine;

        // The full-screen pass lives on its own component so it can be switched off while the
        // vignette is fully open (see Update), without touching this component's own "enabled"
        // flag, which is what the VR settings toggle drives.
        private VignetteBlit blit;

        private static HashSet<Type> vignetteAbilities = new HashSet<Type>()
        {
            typeof(EntityStates.Commando.DodgeState),
            typeof(EntityStates.Commando.SlideState),
            typeof(EntityStates.Huntress.BlinkState),
            typeof(EntityStates.Huntress.MiniBlinkState),
            typeof(EntityStates.Toolbot.ToolbotDash),
            typeof(EntityStates.Mage.FlyUpState),
            typeof(EntityStates.Merc.Assaulter2),
            typeof(EntityStates.Merc.EvisDash),
            typeof(EntityStates.Merc.FocusedAssaultDash),
            typeof(EntityStates.Merc.EvisDash),
            typeof(EntityStates.Loader.SwingChargedFist),
            typeof(EntityStates.Loader.SwingZapFist),
            typeof(EntityStates.Loader.GroundSlam),
            typeof(EntityStates.Croco.Leap),
            typeof(EntityStates.Croco.ChainableLeap)
        };

        private void Awake()
        {
            instance = this;

            if (!vignette)
                vignette = VRMod.VRAssetBundle.LoadAsset<Material>("VignetteMaterial");

            blit = gameObject.AddComponent<VignetteBlit>();
            blit.enabled = false;
        }

        private void OnEnable()
        {
            if (!foundStateMachine)
                RoR2Application.onUpdate += FindStateMachine;
        }

        private void OnDisable()
        {
            if (!foundStateMachine)
                RoR2Application.onUpdate -= FindStateMachine;

            if (blit)
                blit.enabled = false;
        }

        private void OnDestroy()
        {
            if (blit)
                Destroy(blit);
        }

        /// <summary>
        /// Adds to the list of states that activates the confort vignette. Recommended for mobility skills.
        /// </summary>
        /// <param name="stateType">The state type that will use the vignette.</param>
        public static void AddVignetteState(Type stateType)
        {
            if (!stateType.IsSubclassOf(typeof(EntityStates.EntityState)))
            {
                VRMod.StaticLogger.LogError("The type " + stateType.ToString() + " doesn't inherit from EntityState and cannot be added as a vignette state.");
                return;
            }

            if (vignetteAbilities.Contains(stateType))
            {
                VRMod.StaticLogger.LogError("The state type " + stateType.ToString() + " is already set as a vignette state and cannot be added twice.");
                return;
            }

            vignetteAbilities.Add(stateType);
        }

        private void FindStateMachine()
        {
            CharacterBody body = LocalUserManager.GetFirstLocalUser().cachedBody;

            if (body)
            {
                stateMachines = body.GetComponents<EntityStateMachine>().ToList();
                foundStateMachine = true;
                RoR2Application.onUpdate -= FindStateMachine;
            }
        }

        // Performance: this used to be an OnRenderImage on the UI camera, i.e. a full-screen copy of
        // each eye on EVERY frame of a run, although the vignette is fully open almost all of the
        // time. The state check now runs once per frame here, and the full-screen pass is only
        // enabled while the vignette is actually closed in. (It also means both eyes get the same
        // radius; before, the radius advanced once per eye pass.)
        private void Update()
        {
            if (foundStateMachine)
            {
                stateMachines.RemoveAll(sm => sm == null);

                activated = stateMachines != null && stateMachines.Count > 0 && stateMachines.Exists(sm => sm.state != null && vignetteAbilities.Contains(sm.state.GetType()));

                if (activated != wasActivated)
                {
                    wasActivated = activated;
                    currentTargetRadius = activated ? TARGET_RADIUS : INITIAL_RADIUS;
                }

                if (currentRadius != currentTargetRadius)
                {
                    float difference = Mathf.Abs(currentTargetRadius - currentRadius);
                    float unclampedChange = Time.deltaTime * (currentRadius < currentTargetRadius ? RADIUS_CHANGE_PER_SECOND : -RADIUS_CHANGE_PER_SECOND);
                    float radiusToChange = Mathf.Clamp(unclampedChange, -difference, difference);

                    currentRadius += radiusToChange;

                    vignette.SetFloat("_VRadius", currentRadius);
                }
            }

            bool visible = activated || currentRadius < INITIAL_RADIUS - 0.0001f;

            if (blit && blit.enabled != visible)
                blit.enabled = visible;
        }

        /// <summary>The vignette's full-screen pass. Enabled only while the vignette is visible.</summary>
        private class VignetteBlit : MonoBehaviour
        {
            private void OnRenderImage(RenderTexture source, RenderTexture destination)
            {
                Graphics.Blit(source, destination, vignette);
            }
        }
    }
}
