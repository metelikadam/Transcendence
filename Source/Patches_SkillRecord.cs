using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Transcendence
{
    // =====================================================================
    //  SkillRecord yamaları: seviye sınırı, XP eğrisi, unutma, seviye adı
    // =====================================================================

    /// <summary>
    /// Learn() içindeki üç adet sabit "20" değerini 40 yapar:
    ///   if (levelInt == 20 &amp;&amp; ...)   -> max seviyede XP birikmesini durdurur
    ///   if (levelInt &gt;= 20) levelInt = 20;   (iki sabit)
    /// SkillRecord.MaxLevel bir "const" olduğu için derleyici değeri koda gömmüştür;
    /// alanı değiştiremeyiz, IL'deki sabitleri tek tek değiştirmemiz gerekir (Transpiler).
    /// Not: "levelInt == 14" (usta olma hikayesi) dokunulmadan kalır.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
    public static class Patch_SkillRecord_Learn
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => SkillMath.ReplaceIntConstant(instructions, SkillMath.VanillaMaxLevel, SkillMath.NewMaxLevel,
                expectedCount: 3, "SkillRecord.Learn");
    }

    /// <summary>
    /// 20 ve üzeri seviyeler için XP gereksinimi. 0-19 arası vanilla eğrisine hiç dokunulmaz.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.XpRequiredToLevelUpFrom))]
    public static class Patch_SkillRecord_XpRequiredToLevelUpFrom
    {
        static void Postfix(int startingLevel, ref float __result)
        {
            if (startingLevel >= SkillMath.VanillaMaxLevel)
            {
                __result = SkillMath.XpToLevelUpFrom(startingLevel);
            }
        }
    }

    /// <summary>
    /// GetLevel: oyun mekaniklerinin kullandığı seviye. Vanilla 0-20 arası kırpar; biz 40'a kadar izin veriyoruz.
    /// Prefix "false" döndürerek orijinali atlıyor. Metot çok kısa olduğu için bu güvenli.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.GetLevel))]
    public static class Patch_SkillRecord_GetLevel
    {
        static bool Prefix(SkillRecord __instance, bool includeAptitudes, ref int __result)
        {
            if (__instance.TotallyDisabled)
            {
                __result = 0;
                return false;
            }
            __result = SkillMath.EffectiveLevel(__instance.levelInt, includeAptitudes ? __instance.Aptitude : 0);
            return false;
        }
    }

    /// <summary>GetLevelForUI: arayüzde gösterilen seviye (GetLevel ile aynı mantık).</summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.GetLevelForUI))]
    public static class Patch_SkillRecord_GetLevelForUI
    {
        static bool Prefix(SkillRecord __instance, bool includeAptitudes, ref int __result)
        {
            if (__instance.PermanentlyDisabled)
            {
                __result = 0;
                return false;
            }
            __result = SkillMath.EffectiveLevel(__instance.levelInt, includeAptitudes ? __instance.Aptitude : 0);
            return false;
        }
    }

    /// <summary>
    /// Interval(): her 200 tick'te yüksek seviyeli yetenekler XP kaybeder (unutma).
    /// Vanilla'daki switch 20'nin üstünü bilmediği için hiçbir şey yapmaz; burada
    /// 20. seviyedeki unutmayı (-12) 21-40 için de uyguluyoruz. Unutmayı daha da artırmıyoruz,
    /// çünkü zorluk zaten XP eğrisinden geliyor.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Interval))]
    public static class Patch_SkillRecord_Interval
    {
        // ___pawn: Harmony'nin özel alan erişimi. Üç alt çizgi + alan adı = SkillRecord.pawn (private).
        // __runOriginal: orijinal Interval çalıştı mı? Başka bir mod (ör. Mad Skills+) bir Prefix ile
        // orijinali atladıysa unutmayı o mod üstlenmiştir. Postfix'ler yine de çalıştığı için burada
        // kontrol etmezsek iki kat unutma olur ya da o modun "unutma yok" kararı (uyku, tutma süresi) bozulur.
        static void Postfix(SkillRecord __instance, Pawn ___pawn, bool __runOriginal)
        {
            if (!__runOriginal || __instance.levelInt <= SkillMath.VanillaMaxLevel)
            {
                return;
            }
            var traits = ___pawn?.story?.traits;
            if (ModsConfig.AnomalyActive && traits != null && traits.HasTrait(TraitDefOf.PerfectMemory))
            {
                return;
            }
            float memoryFactor = (traits != null && traits.HasTrait(TraitDefOf.GreatMemory)) ? 0.5f : 1f;

            // Vanilla Skills Expanded: tutku türüne göre unutma çarpanı (VSE yoksa 1).
            // VSE bunu vanilla switch'ine transpiler ile ekliyor; bizim 20+ unutmamıza da aynısını uyguluyoruz.
            // Vanilla Expanded Framework: genlerden gelen unutma çarpanı (VEF yoksa 1). VEF de bunu vanilla switch'ine ekliyor.
            float forgetFactor = VSECompat.ForgetRateFactor(__instance) * VSECompat.GeneForgetMultiplier(___pawn);
            if (forgetFactor <= 0f)
            {
                return; // ör. VSE "Kritik tutku": hiç unutmaz
            }
            __instance.Learn(-SkillMath.VanillaLevel20Decay * memoryFactor * forgetFactor);
        }
    }

    /// <summary>Seviye adı: vanilla 0-20 için "Skill0".."Skill20" anahtarları var, 21+ için kendi metnimiz.</summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.LevelDescriptor), MethodType.Getter)]
    public static class Patch_SkillRecord_LevelDescriptor
    {
        static void Postfix(SkillRecord __instance, ref string __result)
        {
            int level = __instance.GetLevelForUI();
            if (level > SkillMath.VanillaMaxLevel)
            {
                __result = "Transcendence_Beyond".Translate(level - SkillMath.VanillaMaxLevel);
            }
        }
    }
}
