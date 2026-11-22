using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Transcendence
{
    // =====================================================================
    //  Arayüz yamaları: karakter sekmesindeki yetenek çubuğu ve ipucu metni
    // =====================================================================

    /// <summary>
    /// Yetenek çubuğu: fillPercent = level / 20f  ->  level / 40f.
    /// Böylece çubuk 40. seviyede dolu olur (20. seviyede yarıya kadar dolar).
    /// DrawSkill'in iki overload'u var; asıl çizimi Rect alan yapıyor.
    /// </summary>
    [HarmonyPatch(typeof(SkillUI), nameof(SkillUI.DrawSkill),
        new[] { typeof(SkillRecord), typeof(Rect), typeof(SkillUI.SkillDrawMode), typeof(string) })]
    public static class Patch_SkillUI_DrawSkill
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => SkillMath.ReplaceFloatConstant(instructions, 20f, 40f, expectedCount: 1, "SkillUI.DrawSkill");
    }

    /// <summary>
    /// İpucu metni (private metot, bu yüzden adını string olarak veriyoruz). Üç sabit 20:
    ///   unclampedLevel &gt; 20  /  "LevelCappedAtMax".Translate(20)  /  GetLevel(false) == 20
    /// </summary>
    [HarmonyPatch(typeof(SkillUI), "GetSkillDescription")]
    public static class Patch_SkillUI_GetSkillDescription
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => SkillMath.ReplaceIntConstant(instructions, SkillMath.VanillaMaxLevel, SkillMath.NewMaxLevel,
                expectedCount: 3, "SkillUI.GetSkillDescription");
    }
}
