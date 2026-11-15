using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VRMod
{
    internal class MeleeSkill : MonoBehaviour
    {
        [SerializeField]
        private Transform weaponTip;

        [SerializeField]
        public float speedThreshold;

        [SerializeField]
        private string[] activatedSkills;

        [SerializeField]
        private float activationTime;

        internal static bool[] skillStates = new bool[4];

        private static float[] timers = new float[4];

        private static CharacterBody body;

        private static List<MeleeSkill> instanceList = new List<MeleeSkill>();

        private Vector3[] tipPath = new Vector3[3];

        private bool firstFrame = true;

        private void OnEnable()
        {
            if (instanceList.Count() <= 0)
            {
                RoR2Application.onFixedUpdate += UpdateTimer;
            }

            instanceList.Add(this);
        }

        private void OnDisable()
        {
            instanceList.Remove(this);

            if (instanceList.Count() <= 0)
            {
                RoR2Application.onFixedUpdate -= UpdateTimer;
            }
        }

        private static void UpdateTimer()
        {
            for (int i = 0; i < 4; i++)
            {
                if (timers[i] > 0)
                {
                    timers[i] -= Time.fixedDeltaTime;
                }

                skillStates[i] = timers[i] > 0;
            }
        }

        private void FixedUpdate()
        {
            if (!VRCameraWrapper.instance) return;

            if (!body)
            {
                if ((body = LocalUserManager.GetFirstLocalUser().cachedBody) == null) return;
            }

            Vector3 newPoint = VRCameraWrapper.instance.transform.InverseTransformPoint(weaponTip.transform.position);

            if (newPoint != tipPath[0])
            {
                tipPath[2] = tipPath[1];
                tipPath[1] = tipPath[0];
                tipPath[0] = newPoint;

                if (firstFrame)
                {
                    firstFrame = false;
                    tipPath[2] = tipPath[1] = tipPath[0];
                }
            }

            bool swinging = (Time.fixedDeltaTime * speedThreshold) < (Vector3.Distance(tipPath[0], tipPath[2]) / 2);

            // Performance: this is a physics-tick callback (60 times a second, per melee hand). The
            // skill matching below only ever does something while the hand is swinging, and it used
            // to run regardless - rebuilding several lists with LINQ on every tick. Nothing to do
            // unless swinging, and when swinging the skills are matched with plain loops.
            if (!swinging) return;

            foreach (GenericSkill skill in body.skillLocator.allSkills)
            {
                if (!skill || !skill.skillDef) continue;

                string defName = skill.skillDef.skillName;

                if (System.Array.IndexOf(activatedSkills, skill.skillName) < 0 && System.Array.IndexOf(activatedSkills, defName) < 0) continue;

                if (defName != null && defName.Contains("Lunar") && defName.Contains("Replacement")) continue;

                SkillSlot slot = body.skillLocator.FindSkillSlot(skill);

                if (slot != SkillSlot.None)
                    timers[(int)slot] = activationTime;
            }
        }
    }
}
