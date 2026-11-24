using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Transcendence
{
    // =====================================================================
    //  Pawn düzenleyicileri: EdB Prepare Carefully, Character Editor
    //
    //  Kural:
    //   - Oyuncunun düzenleyicide ayarladığı pawn'lar 0-40 arası seviye alabilir.
    //   - Oyunun ürettiği pawn'lar (akıncılar, katılımcılar, tüccarlar, rastgele başlangıç pawn'ları)
    //     en fazla 20 ile başlar. 21-40 normalde sadece XP ile kazanılır.
    //
    //  Nasıl:
    //   SkillRecord.Level setter'ı vanilla'da 0-20'ye kırpar ve biz bunu bilerek değiştirmedik.
    //   Düzenleyicilerin seviye yazan metotlarını bir "düzenleyici bağlamı" ile sarıyoruz;
    //   setter sadece bu bağlamdayken 40'a kadar izin veriyor. PawnGenerator içinde bağlam her zaman kapalı.
    //   Hedef metotlar isimle bulunur; mod yüklü değilse ilgili yama sessizce atlanır.
    // =====================================================================

    public static class SkillEditorContext
    {
        [ThreadStatic] static int editorDepth;
        [ThreadStatic] static int generationDepth;

        /// <summary>Şu anda bir düzenleyici seviye yazıyor ve pawn üretimi içinde değiliz.</summary>
        public static bool AllowAboveVanillaCap => editorDepth > 0 && generationDepth == 0;

        public static void EnterEditor() => editorDepth++;
        public static void ExitEditor() => editorDepth = Math.Max(0, editorDepth - 1);
        public static void EnterGeneration() => generationDepth++;
        public static void ExitGeneration() => generationDepth = Math.Max(0, generationDepth - 1);
    }

    /// <summary>Düzenleyici bağlamındayken Level setter 0-40 arası kabul eder; diğer her yerde vanilla (0-20).</summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Level), MethodType.Setter)]
    public static class Patch_SkillRecord_LevelSetter
    {
        static bool Prefix(SkillRecord __instance, int value)
        {
            if (!SkillEditorContext.AllowAboveVanillaCap)
            {
                return true; // vanilla: Clamp(value, 0, 20)
            }
            __instance.levelInt = Mathf.Clamp(value, 0, SkillMath.NewMaxLevel);
            return false;
        }
    }

    /// <summary>Pawn üretimi sırasında düzenleyici bağlamı her zaman kapalı: oyunun ürettiği pawn 20'yi geçemez.</summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    public static class Patch_PawnGenerator_GeneratePawn
    {
        static void Prefix() => SkillEditorContext.EnterGeneration();
        static void Finalizer() => SkillEditorContext.ExitGeneration();
    }

    /// <summary>
    /// Düzenleyicilerin seviye yazan metotları. Prefix bağlama girer; Finalizer, hata olsa bile bağlamdan çıkar.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_PawnEditors_SkillWriters
    {
        static readonly string[] Targets =
        {
            // EdB Prepare Carefully
            "EdB.PrepareCarefully.ManagerPawns:SetSkillLevel",
            "EdB.PrepareCarefully.ManagerPawns:ClearSkillLevels",
            "EdB.PrepareCarefully.ManagerPawns:ResetSkillLevels",
            "EdB.PrepareCarefully.ManagerPawns:RecalculateSkillLevelsFromSkillGains",
            "EdB.PrepareCarefully.PawnCustomizer:ApplySkillCustomizationsToPawn", // ön ayar yükleme ve oyunu başlatma
            // Character Editor
            "CharacterEditor.SkillTool:AddSkill",
        };

        static List<MethodBase> found;

        static bool Prepare()
        {
            found ??= Targets.Select(FindMethod).Where(m => m != null).ToList();
            return found.Count > 0;
        }

        static IEnumerable<MethodBase> TargetMethods() => found;

        static void Prefix() => SkillEditorContext.EnterEditor();
        static void Finalizer() => SkillEditorContext.ExitEditor();

        internal static MethodBase FindMethod(string typeColonMethod)
        {
            string[] parts = typeColonMethod.Split(':');
            Type type = AccessTools.TypeByName(parts[0]);
            return type == null ? null : AccessTools.Method(type, parts[1]);
        }
    }

    // ---------------------------------------------------------------------
    //  Prepare Carefully arayüzü
    // ---------------------------------------------------------------------

    /// <summary>
    /// PanelSkills.DrawSkill: çubuk doluluğu ve tıklama hesabı 20'ye göre (4 adet 20f) -> 40f.
    /// Çubuğun sonuna tıklamak artık 40 verir.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_PrepareCarefully_SkillBar
    {
        static MethodBase target;

        static bool Prepare()
        {
            target ??= Patch_PawnEditors_SkillWriters.FindMethod("EdB.PrepareCarefully.PanelSkills:DrawSkill");
            return target != null;
        }

        static MethodBase TargetMethod() => target;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => SkillMath.ReplaceFloatConstant(instructions, 20f, 40f, expectedCount: 4, "PrepareCarefully PanelSkills.DrawSkill");
    }

    // ---------------------------------------------------------------------
    //  Character Editor arayüzü (seviyeyi doğrudan levelInt'e yazıyor)
    // ---------------------------------------------------------------------

    /// <summary>Sayı kutusu: NumericTextField(..., 0, 9999) -> (..., 0, 40).</summary>
    [HarmonyPatch]
    public static class Patch_CharacterEditor_SkillInputMax
    {
        static MethodBase target;

        static bool Prepare()
        {
            target ??= Patch_PawnEditors_SkillWriters.FindMethod("CharacterEditor.CEditor+EditorUI+BlockBio:DrawSkills");
            return target != null;
        }

        static MethodBase TargetMethod() => target;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => SkillMath.ReplaceIntConstant(instructions, 9999, SkillMath.NewMaxLevel, expectedCount: 1, "CharacterEditor DrawSkills");
    }

    /// <summary>"+" düğmesi: record.levelInt++ için üst sınır yok -> en fazla 40.</summary>
    [HarmonyPatch]
    public static class Patch_CharacterEditor_AddSkillLevel
    {
        static MethodBase target;

        static bool Prepare()
        {
            target ??= Patch_PawnEditors_SkillWriters.FindMethod("CharacterEditor.CEditor+EditorUI+BlockBio:AAddSkillLevel");
            return target != null;
        }

        static MethodBase TargetMethod() => target;

        static void Postfix(SkillRecord record)
        {
            if (record != null && record.levelInt > SkillMath.NewMaxLevel)
            {
                record.levelInt = SkillMath.NewMaxLevel;
            }
        }
    }

    /// <summary>
    /// Character Editor, bonus çubuklarını ölçeklemek için SkillRecord.MaxLevel sabitini reflection ile okuyor (20).
    /// const değiştirilemez, bu yüzden okuyan yardımcıyı yamalıyoruz: SkillRecord için "MaxLevel" -> 40.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CharacterEditor_MaxLevelConst
    {
        static MethodBase target;

        static bool Prepare()
        {
            target ??= Patch_PawnEditors_SkillWriters.FindMethod("CharacterEditor.Reflect:GetMemberConstValue");
            return target != null;
        }

        static MethodBase TargetMethod() => target;

        static void Postfix(object obj, string name, ref object __result)
        {
            if (obj is SkillRecord && name == "MaxLevel")
            {
                __result = SkillMath.NewMaxLevel;
            }
        }
    }
}
