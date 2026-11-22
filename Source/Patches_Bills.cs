using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Transcendence
{
    // =====================================================================
    //  İş emirleri (Bill) — "izinli beceri aralığı"
    //
    //  Vanilla'da her bill'in allowedSkillRange alanı varsayılan olarak 0~20.
    //  Bill.PawnAllowedToStartAnew, seviyesi bu aralığın dışında kalan pawn'ı reddediyor:
    //  "Yapamıyor: ... İzinli beceri seviyesinin (20) üstünde".
    //  Bu tüm tezgâhları etkiler (Mines modu dahil, çünkü o da vanilla bill kullanıyor).
    // =====================================================================

    /// <summary>
    /// Yeni bill'ler 0~40 ile başlasın. Alanın başlangıç değeri (field initializer) yapıcı metotların
    /// içine derlendiği için Bill'in tüm yapıcılarına Postfix uyguluyoruz. Alt sınıflar
    /// (Bill_Production, Bill_Medical...) bu yapıcıları çağırdığı için onlar da kapsanır.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_Bill_Constructors
    {
        static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredConstructors(typeof(Bill));

        static void Postfix(Bill __instance)
        {
            if (__instance.allowedSkillRange.max == SkillMath.VanillaMaxLevel)
            {
                __instance.allowedSkillRange.max = SkillMath.NewMaxLevel;
            }
        }
    }

    /// <summary>
    /// Kayıtlı oyunlardaki eski bill'ler "0~20" olarak kaydedilmiş durumda (vanilla'da 20 = sınırsız demekti).
    /// Yüklerken bunları bir kereliğine 0~40'a çeviriyoruz ve kayda bir işaret yazıyoruz;
    /// böylece oyuncu sonradan bilerek 20'ye çekerse bir sonraki yüklemede tekrar 40 yapılmaz.
    /// Mod kaldırılırsa vanilla bu fazladan XML düğümünü yok sayar.
    /// </summary>
    [HarmonyPatch(typeof(Bill), nameof(Bill.ExposeData))]
    public static class Patch_Bill_ExposeData
    {
        // Modun eski adından (Skill Cap 40) kalma. DEĞİŞTİRME: kayıtlı oyunlarda bu adla duruyor;
        // değişirse eski kayıtlardaki bill'ler (oyuncunun bilerek 20'ye çektikleri dahil) tekrar 40'a çevrilir.
        const string MigratedLabel = "skillCap40RangeMigrated";

        static void Postfix(Bill __instance)
        {
            bool migrated = true; // kaydederken her zaman "true" yazılır
            Scribe_Values.Look(ref migrated, MigratedLabel, defaultValue: false);

            if (Scribe.mode == LoadSaveMode.LoadingVars && !migrated
                && __instance.allowedSkillRange.max == SkillMath.VanillaMaxLevel)
            {
                __instance.allowedSkillRange.max = SkillMath.NewMaxLevel;
            }
        }
    }

    /// <summary>
    /// Bill ayar penceresi: listing.IntRange(ref bill.allowedSkillRange, 0, 20) -> 0, 40.
    /// Pencerede birçok 20 sabiti olabileceği için sadece IntRange çağrısından hemen önceki sabiti değiştiriyoruz.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_BillConfig), nameof(Dialog_BillConfig.DoWindowContents))]
    public static class Patch_Dialog_BillConfig_SkillSlider
    {
        static readonly MethodInfo intRange = AccessTools.Method(typeof(Listing_Standard), nameof(Listing_Standard.IntRange),
            new[] { typeof(IntRange).MakeByRefType(), typeof(int), typeof(int) });

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int count = 0;
            for (int i = 1; i < codes.Count; i++)
            {
                if (codes[i].Calls(intRange) && codes[i - 1].LoadsConstant(SkillMath.VanillaMaxLevel))
                {
                    codes[i - 1].opcode = OpCodes.Ldc_I4;
                    codes[i - 1].operand = SkillMath.NewMaxLevel;
                    count++;
                }
            }
            if (count != 1)
            {
                Log.Warning($"[Transcendence] Dialog_BillConfig: expected 1 skill range slider, patched {count}.");
            }
            return codes;
        }
    }
}
