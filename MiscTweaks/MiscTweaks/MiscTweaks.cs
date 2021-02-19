using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

using BepInEx;
using BepInEx.Configuration;

using HarmonyLib;

using Receiver2;

using fmod = FMOD.Studio;
using log = BepInEx.Logging;
using unity = UnityEngine;

namespace MiscTweaks
{
    [BepInPlugin("SmilingLizard.plugins.miscTweaks", "MiscTweaks", "1.3")]
    public class MiscTweaks : BaseUnityPlugin
    {
        #region helpers

        private static log.ManualLogSource StaticLogger => log::Logger.CreateLogSource("s_MiscTweak");

        private static CodeInstruction PropGetter(Type type, string propName)
        {
            return CodeInstruction.Call(type,
                AccessTools.PropertyGetter(type, propName).Name);
        }

        private static void LogInstructions(CodeMatcher matcher, int start, int amount, string title)
        {
            StaticLogger.LogInfo(title);
            List<CodeInstruction> list = matcher.InstructionsInRange(
                Math.Max(start, 0),
                Math.Min(amount, matcher.Instructions().Count));
            for (int i = 0; i < list.Count; i++)
            {
                StaticLogger.LogInfo($"[{start + i:0000}] {list[i]}");
            }
        }

        private static void LogInstructions(IEnumerable<CodeInstruction> ilCode, string title)
        {
            StaticLogger.LogInfo(title);
            CodeInstruction[] il = ilCode.ToArray();
            for (int i = 0; i < il.Length; i++)
            {
                StaticLogger.LogInfo($"[{i:0000}] {il[i]}");
            }
        }

        #endregion helpers

        #region cfg

        private static Dictionary<SecF, ConfigEntry<float>> floatCFG;
        private static Dictionary<SecC, ConfigEntry<unity::Color>> colorCFG;

        private enum SecF : int
        {
            beepSFX,
            alarmSFX,
            damageSFX,
            Passive,
            Alert,
            Aggressive,
            Standby,

            Count
        }

        private enum SecC : int
        {
            Passive,
            Alert,
            Aggressive,
            Standby,

            Count
        }

        #endregion cfg

        #region defaults

        private const float
            volumeDefault = 1f,
            volumeMin = 0f,
            volumeMax = 3f,
            passiveDefaultIntensity = 3f,
            alertDefaultIntensity = 3f,
            aggressiveDefaultIntensity = 3f,
            standbyDefaultIntensity = 0f,
            intensityMin = 0f,
            intensityMax = 10f;

        private static readonly unity::Color passivDefaultColor = new unity::Color(0f, 0f, 1f);
        private static readonly unity::Color alertDefaultColor = new unity::Color(1f, 1f, 0f);
        private static readonly unity::Color aggressiveDefaultColor = new unity::Color(1f, 0f, 0f);
        private static readonly unity::Color standbyDefaultColor = new unity::Color(0f, 0f, 1f);

        #endregion defaults

        #region setup

        public void Awake()
        {
            const string
                colors = "Killdrone Light Colors",
                passiveColor = "Passive Light Color",
                passiveIntensity = "Passive Light Intensity",
                alertColor = "Alert Light Color",
                alertIntensity = "Alert Light Intensity",
                aggressiveColor = "Agressive Light Color",
                aggressiveIntensity = "Agressive Light Intensity",
                standbyColor = "Standby Light Color",
                standbyIntenisty = "Standby Light Intensity",
                intensityDesc = "The brightness of the light. 0 for invisible.",
                colorDesc = "The color of the camera light. Only turrets update immediately; others update when switching state.",

                sounds = "Killdrone Sounds",
                beepVolume = "Alert Beep Volume",
                beepDesc = "The volume of the target detection and target lost beep tones.",
                alarmVolume = "Alarm Volume",
                alarmDesc = "The volume of the alarm. Only takes effect on game restart.",
                dmgVolume = "Damage SFX Volume",
                dmgDesc = "Multiplier for volume of sounds made when killdrone parts are damaged. (currently only affects turrets)";

            floatCFG = new Dictionary<SecF, ConfigEntry<float>>((int)SecF.Count)
            {
                [SecF.beepSFX] = this.Config.Bind(sounds, beepVolume, volumeDefault,
                new ConfigDescription(beepDesc,
                new AcceptableValueRange<float>(volumeMin, volumeMax))),

                [SecF.alarmSFX] = this.Config.Bind(sounds, alarmVolume, volumeDefault,
                new ConfigDescription(alarmDesc,
                new AcceptableValueRange<float>(volumeMin, volumeMax))),

                //[SecF.damageSFX] = this.Config.Bind(sounds, dmgVolume, volumeDefault,
                //new ConfigDescription(dmgDesc,
                //new AcceptableValueRange<float>(volumeMin, volumeMax))),

                [SecF.Passive] = this.Config.Bind(colors, passiveIntensity, passiveDefaultIntensity,
                new ConfigDescription(intensityDesc,
                new AcceptableValueRange<float>(intensityMin, intensityMax))),

                [SecF.Alert] = this.Config.Bind(colors, alertIntensity, alertDefaultIntensity,
                new ConfigDescription(intensityDesc,
                new AcceptableValueRange<float>(intensityMin, intensityMax))),

                [SecF.Aggressive] = this.Config.Bind(colors, aggressiveIntensity, aggressiveDefaultIntensity,
                new ConfigDescription(intensityDesc,
                new AcceptableValueRange<float>(intensityMin, intensityMax))),

                [SecF.Standby] = this.Config.Bind(colors, standbyIntenisty, standbyDefaultIntensity,
                new ConfigDescription(intensityDesc,
                new AcceptableValueRange<float>(intensityMin, intensityMax)))
            };

            colorCFG = new Dictionary<SecC, ConfigEntry<unity::Color>>((int)SecC.Count)
            {
                [SecC.Passive] = this.Config.Bind(colors, passiveColor, passivDefaultColor, colorDesc),

                [SecC.Alert] = this.Config.Bind(colors, alertColor, alertDefaultColor, colorDesc),

                [SecC.Aggressive] = this.Config.Bind(colors, aggressiveColor, aggressiveDefaultColor, colorDesc),

                [SecC.Standby] = this.Config.Bind(colors, standbyColor, standbyDefaultColor, colorDesc)
            };

            _ = Harmony.CreateAndPatchAll(typeof(MiscTweaks));
        }

        #endregion setup

        #region modVolume

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(TurretScript), "UpdateSensor")]      // turret wakeup
        [HarmonyPatch(typeof(TurretScript), "UpdateCameraAlive")] // turret alert/unalert
        [HarmonyPatch(typeof(ShockDrone), "IdleUpdate")]  // shock   alert
        [HarmonyPatch(typeof(ShockDrone), "AlertUpdate")] // shock unalert
        [HarmonyPatch(typeof(SecurityCamera), "IdleUpdate")]  // cam  alert
        [HarmonyPatch(typeof(SecurityCamera), "AlertUpdate")] // cam unalert
        public static IEnumerable<CodeInstruction> ModTurretBeeps(IEnumerable<CodeInstruction> ilCode)
        {
            return ModifyAllOneShotVolume3DCalls(ilCode, () => floatCFG[SecF.beepSFX].Value);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(SecurityCamera), "Start")] // cam alarm
        public static IEnumerable<CodeInstruction> ModCameraAlarm(IEnumerable<CodeInstruction> ilCode)
        {
            CodeMatcher m = new CodeMatcher(ilCode)
                .MatchForward(false, new CodeMatch(
                    CodeInstruction.Call(typeof(fmod::EventInstance), nameof(fmod::EventInstance.setVolume))))
                .Advance(-1);
            return m
                .SetInstruction(Transpilers.EmitDelegate<Func<float>>(() => floatCFG[SecF.alarmSFX].Value))
                .Instructions();
        }

        // does NOT seem to work because screw you
        //[HarmonyTranspiler]
        //[HarmonyPatch(typeof(TurretScript), nameof(TurretScript.Damage))] // turret dmgSFX
        //public static IEnumerable<CodeInstruction> ModDmgSFX(IEnumerable<CodeInstruction> ilCode)
        //{
        //    return ModifyAllOneShotVolume3DCalls(ilCode, () =>
        //    {
        //        float value = floatCFG[SecF.damageSFX].Value;
        //        StaticLogger.LogInfo($"playing sound at {value}");
        //        return value;
        //    });
        //}

        public static IEnumerable<CodeInstruction> ModifyAllOneShotVolume3DCalls(
            IEnumerable<CodeInstruction> ilCode, Func<float> replacementValue)
        {
            CodeMatch oneShotCall = new CodeMatch(
                CodeInstruction.Call(typeof(AudioManager), nameof(AudioManager.PlayOneShot3D)));

            CodeMatcher m = new CodeMatcher(ilCode)
                .End()
                .MatchBack(false, oneShotCall)
                .Advance(-2);

            while (m.Pos >= 0)
            {
                _ = m.SetInstruction(Transpilers.EmitDelegate(replacementValue))
                    .MatchBack(false, oneShotCall)
                    .Advance(-2);
            }

            return m.Instructions();
        }

        #endregion modVolume

        #region modColor

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(LightPart), "SetLightMode")] // shockDrone and securityCam
        public static IEnumerable<CodeInstruction> ModifyLight(IEnumerable<CodeInstruction> ilCode)
        {
            CodeMatch setLightColor = new CodeMatch(CodeInstruction.Call(typeof(LightPart), "SetLightColor"));

            return new CodeMatcher(ilCode)
                .Start() // this is redundant, I seem to have missed it
                .End()
                //standby
                .MatchBack(true, setLightColor)
                .Advance(-3)
                .RemoveInstruction()
                .SetInstructionAndAdvance(
                    Transpilers.EmitDelegate<Func<unity::Color>>(() => colorCFG[SecC.Standby].Value))
                .SetInstruction(
                    Transpilers.EmitDelegate<Func<float>>(() => floatCFG[SecF.Standby].Value))
                //aggressive
                .MatchBack(true, setLightColor)
                .Advance(-3)
                .RemoveInstruction()
                .SetInstructionAndAdvance(
                    Transpilers.EmitDelegate<Func<unity::Color>>(() => colorCFG[SecC.Aggressive].Value))
                .SetInstruction(
                    Transpilers.EmitDelegate<Func<float>>(() => floatCFG[SecF.Aggressive].Value))
                // alert
                .MatchBack(true, setLightColor)
                .Advance(-3)
                .RemoveInstruction()
                .SetInstructionAndAdvance(
                    Transpilers.EmitDelegate<Func<unity::Color>>(() => colorCFG[SecC.Alert].Value))
                .SetInstruction(
                    Transpilers.EmitDelegate<Func<float>>(() => floatCFG[SecF.Alert].Value))
                //passive
                .MatchBack(true, setLightColor)
                .Advance(-3)
                .RemoveInstruction()
                .SetInstructionAndAdvance(
                    Transpilers.EmitDelegate<Func<unity::Color>>(() => colorCFG[SecC.Passive].Value))
                .SetInstruction(
                    Transpilers.EmitDelegate<Func<float>>(() => floatCFG[SecF.Passive].Value))
                .Instructions();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LightPart), "Start")]
        public static void InitLightShock(LightPart __instance) // shocker/cam
        {
            AccessTools.FieldRef<LightPart, unity::Color> light_color =
                AccessTools.FieldRefAccess<LightPart, unity::Color>("light_color");

            if (light_color(__instance) == passivDefaultColor)
            {
                light_color(__instance) = colorCFG[SecC.Passive].Value;
            }
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(TurretScript), "UpdateLight")]
        public static IEnumerable<CodeInstruction> ModifyTurretLight(IEnumerable<CodeInstruction> ilCode)
        {
            return new CodeMatcher(ilCode)
                .Start()
                // passive color
                .MatchForward(false,
                    new CodeMatch(PropGetter(typeof(unity::Color), nameof(unity::Color.blue))))
                .SetInstruction(Transpilers.EmitDelegate<Func<unity::Color>>(() => colorCFG[SecC.Passive].Value))
                // aggressive color
                .MatchForward(false,
                    new CodeMatch(PropGetter(typeof(unity::Color), nameof(unity::Color.red))))
                .SetInstruction(Transpilers.EmitDelegate<Func<unity::Color>>(() => colorCFG[SecC.Aggressive].Value))
                // alert color
                .MatchForward(false,
                    new CodeMatch(PropGetter(typeof(unity::Color), nameof(unity::Color.yellow))))
                .SetInstruction(Transpilers.EmitDelegate<Func<unity::Color>>(() => colorCFG[SecC.Alert].Value))
                // standby color missing
                // standby intensity; now that I look at it again, this one probably causes weird behaviour
                .MatchForward(false, new CodeMatch(
                    new CodeInstruction(OpCodes.Ldc_R4, 0f)))
                .SetInstruction(Transpilers.EmitDelegate<Func<float>>(() => floatCFG[SecF.Standby].Value))
                // wakeup start intensity
                .MatchForward(false, new CodeMatch(
                    new CodeInstruction(OpCodes.Ldc_R4, 0.4f)))
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldarg_0))
                .Insert(Transpilers.EmitDelegate<Func<TurretScript, float>>(SelectCorrectIntensity))
                // wakeup end intensity
                .MatchForward(false, new CodeMatch(
                    new CodeInstruction(OpCodes.Ldc_R4, 1.25f)))
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldarg_0))
                .Insert(Transpilers.EmitDelegate<Func<TurretScript, float>>(SelectCorrectIntensity))
                // other intensity
                .MatchForward(false, new CodeMatch(
                    new CodeInstruction(OpCodes.Ldc_R4, 1.25f)))
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldarg_0))
                .Insert(Transpilers.EmitDelegate<Func<TurretScript, float>>(SelectCorrectIntensity))
                .Instructions();
        }

        public static float SelectCorrectIntensity(TurretScript turret)
        {
            AIState ai_state = AccessTools.FieldRefAccess<TurretScript, AIState>(turret, "ai_state");

            if (ai_state == AIState.Idle)
            {
                return floatCFG[SecF.Passive].Value;
            }

            if (ai_state == AIState.Aiming)
            {
                return AccessTools.FieldRefAccess<TurretScript, bool>(turret, "trigger_down")
                    ? floatCFG[SecF.Aggressive].Value
                    : floatCFG[SecF.Alert].Value;
            }

            //return AccessTools.FieldRefAccess<TurretScript, float>(turret, nameof());
            return turret.gun_pivot_camera_light.intensity;
        }

        #endregion modColor
    }
}
