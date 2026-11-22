using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Transcendence
{
    /// <summary>
    /// Modun tüm "denge" formülleri tek yerde. Yamalar sadece buradaki fonksiyonları çağırır.
    /// </summary>
    public static class SkillMath
    {
        public const int VanillaMaxLevel = 20;
        public const int NewMaxLevel = 40;

        /// <summary>Vanilla'da 19 -> 20 için gereken XP (XpForLevelUpCurve'ün son noktası).</summary>
        public const float VanillaLastLevelXp = 30000f;

        /// <summary>20 üstünde her seviye bir öncekinden %10 daha pahalı.</summary>
        public const float XpGrowthPerLevel = 1.10f;

        /// <summary>Vanilla'da 20. seviyedeki unutma (her 200 tick'te kaybedilen XP).</summary>
        public const float VanillaLevel20Decay = 12f;

        static float XpDifficulty => TranscendenceMod.Settings?.xpDifficulty ?? TranscendenceSettings.DefaultXpDifficulty;
        public static float EffectStrength => TranscendenceMod.Settings?.effectStrength ?? TranscendenceSettings.DefaultEffectStrength;

        /// <summary>
        /// 20 ve üstü bir seviyeden bir sonrakine geçmek için gereken XP.
        /// 20->21: 33.000, 30->31: ~85.600, 39->40: ~201.800 (1x zorlukta). Toplam ~1,89 milyon.
        /// Karşılaştırma: vanilla'da 0->20 toplamı 265 bin.
        /// </summary>
        public static float XpToLevelUpFrom(int startingLevel)
        {
            return VanillaLastLevelXp * Mathf.Pow(XpGrowthPerLevel, startingLevel - (VanillaMaxLevel - 1)) * XpDifficulty;
        }

        /// <summary>
        /// Oyunda kullanılan "etkin" seviye. Yetenek bonusları (gen, trait, hediff) seni
        /// 20'nin üstüne TAŞIYAMAZ; 20 üstü seviyeler sadece XP ile kazanılır.
        /// Örnekler: öğrenilen 18 + gen bonusu 8 = 20 | öğrenilen 25 + 8 = 25 | öğrenilen 25 - 4 = 21
        /// </summary>
        public static int EffectiveLevel(int learnedLevel, int aptitude)
        {
            int cap = Mathf.Clamp(learnedLevel, VanillaMaxLevel, NewMaxLevel);
            return Mathf.Clamp(learnedLevel + aptitude, 0, cap);
        }

        /// <summary>
        /// 20 üstü seviyeler için genel "uzatma" formülü:
        /// 20. seviyedeki değerden başla, vanilla'nın son seviyelerdeki eğimini EffectStrength ile çarparak devam et.
        /// </summary>
        public static float Extrapolate(float valueAt20, float vanillaSlopePerLevel, int level)
        {
            return valueAt20 + vanillaSlopePerLevel * EffectStrength * (level - VanillaMaxLevel);
        }

        /// <summary>
        /// Transpiler yardımcısı: bir metottaki belirli bir tamsayı sabitini (ör. 20) başka bir değerle (ör. 40) değiştirir.
        /// Beklenen sayıda değişiklik yapılamazsa uyarı yazar (oyun güncellemesi metodu değiştirmiş olabilir).
        /// </summary>
        public static IEnumerable<CodeInstruction> ReplaceIntConstant(IEnumerable<CodeInstruction> instructions,
            int from, int to, int expectedCount, string methodName)
        {
            int count = 0;
            foreach (var code in instructions)
            {
                if (code.LoadsConstant(from))
                {
                    code.opcode = OpCodes.Ldc_I4;
                    code.operand = to;
                    count++;
                }
                yield return code;
            }
            if (count != expectedCount)
            {
                Log.Warning($"[Transcendence] {methodName}: expected {expectedCount} replacements of {from}, made {count}. The game code may have changed.");
            }
        }

        /// <summary>ReplaceIntConstant'ın float sürümü (ör. 20f -> 40f).</summary>
        public static IEnumerable<CodeInstruction> ReplaceFloatConstant(IEnumerable<CodeInstruction> instructions,
            float from, float to, int expectedCount, string methodName)
        {
            int count = 0;
            foreach (var code in instructions)
            {
                if (code.opcode == OpCodes.Ldc_R4 && code.operand is float f && Math.Abs(f - from) < 0.0001f)
                {
                    code.operand = to;
                    count++;
                }
                yield return code;
            }
            if (count != expectedCount)
            {
                Log.Warning($"[Transcendence] {methodName}: expected {expectedCount} replacements of {from}f, made {count}. The game code may have changed.");
            }
        }
    }
}
