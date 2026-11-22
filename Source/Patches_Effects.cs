using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Transcendence
{
    // =====================================================================
    //  Etki yamaları: 20 üstü seviyeler istatistiklere ve kaliteye nasıl yansır
    //
    //  Oyundaki yetenek etkili istatistiklerin hepsi (iş hızı, isabet, ameliyat
    //  başarısı, madencilik verimi...) StatDef XML'lerinde üç SkillNeed sınıfından
    //  biriyle tanımlı. Bu üç sınıfı yamalayınca hepsi birden (DLC'ler ve diğer
    //  modların StatDef'leri dahil) 20 üstünü destekler hale geliyor.
    // =====================================================================

    /// <summary>
    /// SkillNeed_BaseBonus: değer = baseValue + bonusPerLevel * seviye (41 stat bunu kullanıyor).
    /// 20 üstünde: her seviye bonusPerLevel * EffectStrength kadar ekler.
    /// Örnek (iş hızı, bonus 0.1, güç 2x): 20. seviye 2.5x, 30. seviye 4.5x, 40. seviye 6.5x.
    /// </summary>
    [HarmonyPatch(typeof(SkillNeed_BaseBonus), nameof(SkillNeed_BaseBonus.ValueFor))]
    public static class Patch_SkillNeed_BaseBonus
    {
        // Private alanlara hızlı erişim (reflection'dan çok daha hızlı).
        static readonly AccessTools.FieldRef<SkillNeed_BaseBonus, float> baseValue =
            AccessTools.FieldRefAccess<SkillNeed_BaseBonus, float>("baseValue");
        static readonly AccessTools.FieldRef<SkillNeed_BaseBonus, float> bonusPerLevel =
            AccessTools.FieldRefAccess<SkillNeed_BaseBonus, float>("bonusPerLevel");

        static void Postfix(SkillNeed_BaseBonus __instance, Pawn pawn, ref float __result)
        {
            int level = Helpers.LevelOf(pawn, __instance.skill);
            if (level <= SkillMath.VanillaMaxLevel)
            {
                return;
            }
            float bonus = bonusPerLevel(__instance);
            float at20 = baseValue(__instance) + bonus * SkillMath.VanillaMaxLevel;
            __result = SkillMath.Extrapolate(at20, bonus, level);
        }
    }

    /// <summary>
    /// SkillNeed_Direct: XML'de 0-20 için 21 elemanlı bir değer listesi (ör. ameliyat başarısı, madencilik verimi).
    /// Vanilla, liste dışındaki seviyelerde son değeri kullanır. Biz 15-20 arasındaki ortalama artışı devam ettiriyoruz.
    /// Not: StatDef'in kendi maxValue sınırı yine geçerlidir (ör. madencilik verimi en fazla %125).
    /// </summary>
    [HarmonyPatch(typeof(SkillNeed_Direct), nameof(SkillNeed_Direct.ValueFor))]
    public static class Patch_SkillNeed_Direct
    {
        static void Postfix(SkillNeed_Direct __instance, Pawn pawn, ref float __result)
        {
            List<float> values = __instance.valuesPerLevel;
            if (values == null || values.Count <= SkillMath.VanillaMaxLevel)
            {
                return;
            }
            int level = Helpers.LevelOf(pawn, __instance.skill);
            if (level <= SkillMath.VanillaMaxLevel)
            {
                return;
            }
            float at20 = values[SkillMath.VanillaMaxLevel];
            float slope = (at20 - values[SkillMath.VanillaMaxLevel - 5]) / 5f;
            __result = SkillMath.Extrapolate(at20, slope, level);
        }
    }

    /// <summary>
    /// SkillNeed_Curve: bir eğriyle tanımlı (ör. Hacking). Eğrinin son noktasından sonra son bölümün eğimiyle devam ediyoruz.
    /// </summary>
    [HarmonyPatch(typeof(SkillNeed_Curve), nameof(SkillNeed_Curve.ValueFor))]
    public static class Patch_SkillNeed_Curve
    {
        static void Postfix(SkillNeed_Curve __instance, Pawn pawn, ref float __result)
        {
            SimpleCurve curve = __instance.curve;
            if (curve == null || curve.PointsCount < 2)
            {
                return;
            }
            int level = Helpers.LevelOf(pawn, __instance.skill);
            if (level <= SkillMath.VanillaMaxLevel)
            {
                return;
            }
            CurvePoint last = curve[curve.PointsCount - 1];
            CurvePoint prev = curve[curve.PointsCount - 2];
            if (last.x > SkillMath.VanillaMaxLevel || last.x <= prev.x)
            {
                return; // beklenmedik eğri biçimi; vanilla sonucunu koru
            }
            float slope = (last.y - prev.y) / (last.x - prev.x);
            __result = last.y + slope * SkillMath.EffectStrength * (level - last.x);
        }
    }

    /// <summary>
    /// Üretim kalitesi. Vanilla 0-20 için bir switch kullanır ve 20'nin üstünde HİÇ bonus vermez
    /// (21. seviyeli bir zanaatkar berbat kalitede eşya üretirdi!). Bu yüzden 20 üstünü biz hesaplıyoruz.
    /// Vanilla'da son seviyeler başına +0.1 ortalama kalite; biz +0.1 * EffectStrength veriyoruz.
    /// Kalite yine en fazla Masterwork olabilir; Legendary için hâlâ ilham (inspiration) veya ideoloji rolü gerekir.
    /// </summary>
    [HarmonyPatch(typeof(QualityUtility), nameof(QualityUtility.GenerateQualityCreatedByPawn),
        new[] { typeof(int), typeof(bool) })]
    public static class Patch_QualityUtility_GenerateQualityCreatedByPawn
    {
        const float VanillaMeanAt20 = 4.2f;
        const float VanillaSlope = 0.1f;

        static bool Prefix(int relevantSkillLevel, bool inspired, ref QualityCategory __result)
        {
            if (relevantSkillLevel <= SkillMath.VanillaMaxLevel)
            {
                return true; // vanilla çalışsın
            }

            float mean = SkillMath.Extrapolate(VanillaMeanAt20, VanillaSlope, Mathf.Min(relevantSkillLevel, SkillMath.NewMaxLevel));

            // Aşağısı vanilla'nın birebir kopyası, sadece "mean" farklı.
            int value = (int)Rand.GaussianAsymmetric(mean, 0.6f, 0.8f);
            value = Mathf.Clamp(value, 0, 5);
            if (value == 5 && Rand.Value < 0.5f)
            {
                value = (int)Rand.GaussianAsymmetric(mean, 0.6f, 0.95f);
                value = Mathf.Clamp(value, 0, 5);
            }
            var quality = (QualityCategory)value;
            if (inspired)
            {
                quality = (QualityCategory)Mathf.Min((int)quality + 2, 6);
            }
            __result = quality;
            return false;
        }
    }

    internal static class Helpers
    {
        public static int LevelOf(Pawn pawn, SkillDef skill)
        {
            if (pawn?.skills == null || skill == null)
            {
                return 0;
            }
            return pawn.skills.GetSkill(skill)?.Level ?? 0;
        }
    }
}
