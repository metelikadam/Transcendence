using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Transcendence
{
    // =====================================================================
    //  Vanilla Skills Expanded (vanillaexpanded.skills) uyumluluğu
    //
    //  VSE'ye derleme zamanında referans VERMİYORUZ (VSE.dll yoksa mod çökmesin diye).
    //  Bunun yerine çalışma zamanında tipleri isimle buluyoruz (AccessTools.TypeByName).
    //  VSE yüklü değilse bu dosyadaki her şey sessizce devre dışı kalır.
    //
    //  VSE ile zaten uyumlu olan şeyler (kontrol edildi, ek kod gerekmedi):
    //   - VSE de Learn / DrawSkill / GetSkillDescription'ı transpile ediyor, ama bizim
    //     değiştirdiğimiz "20" sabitlerinden farklı komutlara dokunuyor. Hangi sırayla çalışırlarsa çalışsınlar sonuç aynı.
    //   - VSE'nin kalite bonusu vanilla'nın kalite fonksiyonunu çağırıyor. O da bizim 20+ yamamızdan geçiyor.
    //   - VSE'nin yeni hız istatistikleri (VSE_ArtSpeed vb.) vanilla istatistikleri çarpıyor, böylece 20+ etkisini otomatik alıyor.
    //   - Uzmanlık (expertise) bonusları ayrı bir sistem; uzmanlık seviyesi 20'de kalıyor.
    // =====================================================================

    internal static class VSECompat
    {
        public const string PackageId = "vanillaexpanded.skills";

        public static readonly bool Active = ModsConfig.IsActive(PackageId);

        // VSE.Passions.PassionManager.ForgetRateFactor(this SkillRecord) -> tutku türüne göre unutma çarpanı
        // (Kritik tutku 0x, Doğal 3x, İlgisizlik 1.25x, normal tutkular 1x).
        static readonly Func<SkillRecord, float> forgetRateFactor = CreateForgetRateDelegate();

        static Func<SkillRecord, float> CreateForgetRateDelegate()
        {
            if (!Active)
            {
                return null;
            }
            MethodInfo method = AccessTools.Method("VSE.Passions.PassionManager:ForgetRateFactor", new[] { typeof(SkillRecord) });
            if (method == null)
            {
                Log.Warning("[Transcendence] Vanilla Skills Expanded is active but PassionManager.ForgetRateFactor was not found; passion forget rates will not apply above level 20.");
                return null;
            }
            return (Func<SkillRecord, float>)method.CreateDelegate(typeof(Func<SkillRecord, float>));
        }

        /// <summary>VSE yoksa 1 döner (vanilla davranışı).</summary>
        public static float ForgetRateFactor(SkillRecord skill)
            => forgetRateFactor != null ? forgetRateFactor(skill) : 1f;

        // VEF.Genes.VanillaExpandedFramework_SkillRecord_Interval_Transpiler_Patch.GetMultiplier(Pawn):
        // genlerden gelen unutma çarpanı. VSE, VEF'e bağımlı olduğu için ikisi birlikte yüklüdür.
        static readonly Func<Pawn, float> geneForgetMultiplier = CreateGeneDelegate();

        static Func<Pawn, float> CreateGeneDelegate()
        {
            Type type = AccessTools.TypeByName("VEF.Genes.VanillaExpandedFramework_SkillRecord_Interval_Transpiler_Patch");
            MethodInfo method = type != null ? AccessTools.Method(type, "GetMultiplier", new[] { typeof(Pawn) }) : null;
            return method != null ? (Func<Pawn, float>)method.CreateDelegate(typeof(Func<Pawn, float>)) : null;
        }

        /// <summary>VEF yoksa 1 döner.</summary>
        public static float GeneForgetMultiplier(Pawn pawn)
            => geneForgetMultiplier != null && pawn != null ? geneForgetMultiplier(pawn) : 1f;
    }

    /// <summary>
    /// SORUN: VSE'nin uzmanlık sistemi kendi seviye atlama XP'sini SkillRecord.XpRequiredToLevelUpFrom()'dan alıyor.
    /// Biz bu fonksiyonu 20 ve üzeri için değiştirdik. Uzmanlık 20'de sınırlı olduğu için bu değer sadece
    /// "max seviyedeki XP" sınırını etkiliyordu: ipucunda 30000 yerine 33000 görünürdü.
    /// ÇÖZÜM: Uzmanlık 20. seviyedeyken vanilla değerine (30000) geri çekiyoruz.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_VSE_ExpertiseRecord_MaxLevelXp
    {
        static Type expertiseType;
        static AccessTools.FieldRef<object, int> level;
        static AccessTools.FieldRef<object, float> xpRequired;
        static AccessTools.FieldRef<object, float> xpSinceLastLevel;

        // Prepare false dönerse Harmony bu yamayı tamamen atlar (VSE yüklü değilse).
        static bool Prepare()
        {
            if (!VSECompat.Active)
            {
                return false;
            }
            expertiseType ??= AccessTools.TypeByName("VSE.ExpertiseRecord");
            if (expertiseType == null)
            {
                return false;
            }
            level ??= AccessTools.FieldRefAccess<int>(expertiseType, "level");
            xpRequired ??= AccessTools.FieldRefAccess<float>(expertiseType, "XpRequiredForLevelUp");
            xpSinceLastLevel ??= AccessTools.FieldRefAccess<float>(expertiseType, "XpSinceLastLevel");
            return true;
        }

        // Birden fazla metoda aynı Postfix: XP gereksinimini yeniden hesaplayan iki yer.
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(expertiseType, "Learn");
            yield return AccessTools.Method(expertiseType, "ExposeData");
        }

        static void Postfix(object __instance)
        {
            if (level(__instance) < SkillMath.VanillaMaxLevel)
            {
                return;
            }
            xpRequired(__instance) = SkillMath.VanillaLastLevelXp;
            xpSinceLastLevel(__instance) = Mathf.Min(xpSinceLastLevel(__instance), SkillMath.VanillaLastLevelXp - 1f);
        }
    }

    // =====================================================================
    //  Yetenek başına uzmanlık hakları
    //
    //  VSE: kolonist başına toplam "MaxExpertise" (varsayılan 1) uzmanlık.
    //  Bizim mod: her yetenek kendi haklarını açar:
    //     ilk hak  = VSE'nin "uzmanlık için minimum seviye" ayarı (varsayılan 15)
    //     sonra    = her 5 seviyede bir +1   -> 15, 20, 25, 30, 35, 40 = en fazla 6 hak
    //  Haklar sadece o yeteneğin uzmanlıklarında kullanılabilir.
    //
    //  VSE'nin kodu, dosyaları ve kayıtlı ayarları DEĞİŞMEZ. Değişiklik sadece bu mod yüklüyken,
    //  Harmony yamasıyla ve bellekte geçerli olur.
    // =====================================================================

    public static class ExpertiseSlots
    {
        public const int LevelsPerExtraSlot = 5;

        /// <summary>Bir yetenek seviyesinin açtığı uzmanlık hakkı sayısı.</summary>
        public static int SlotsFor(int skillLevel, int firstSlotLevel)
        {
            if (skillLevel < firstSlotLevel)
            {
                return 0;
            }
            return 1 + (skillLevel - firstSlotLevel) / LevelsPerExtraSlot;
        }

        /// <summary>Bir sonraki hakkın açılacağı seviye (ulaşılamıyorsa -1).</summary>
        public static int NextSlotLevel(int usedSlots, int firstSlotLevel)
        {
            int level = firstSlotLevel + usedSlots * LevelsPerExtraSlot;
            return level <= SkillMath.NewMaxLevel ? level : -1;
        }
    }

    /// <summary>
    /// VSE.Expertise.ExpertiseDef.CanApplyOn(Pawn, out string reason) yaması.
    ///
    /// Prefix  : VSE'nin toplam sınırını (MaxExpertise) ve "aynı yetenekte tek uzmanlık" ayarını
    ///           SADECE bu çağrı süresince bellekte devre dışı bırakır.
    /// Postfix : VSE'nin diğer kontrolleri geçtiyse (tutku var, seviye yeterli) yetenek başına hak kuralını uygular.
    /// Finalizer: hata olsa bile VSE ayarlarını eski değerlerine geri yükler.
    /// Böylece VSE'nin kendi mantığını kopyalamadan, sadece "kaç tane" kuralını değiştirmiş oluyoruz.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_VSE_ExpertiseDef_CanApplyOn
    {
        static FieldInfo settingsField;      // VSE.SkillsMod.Settings (static)
        static FieldInfo maxExpertiseField;  // SkillsModSettings.MaxExpertise
        static FieldInfo allowOverlapField;  // SkillsModSettings.AllowExpertiseOverlap
        static FieldInfo minLevelField;      // SkillsModSettings.LevelToGetExpertise
        static FieldInfo defSkillField;      // ExpertiseDef.skill
        static FieldInfo recordDefField;     // ExpertiseRecord.def
        static PropertyInfo allExpertiseProp;// ExpertiseTracker.AllExpertise
        static MethodInfo expertiseOfPawn;   // ExpertiseTrackers.Expertise(this Pawn)

        public struct SavedSettings
        {
            public object settings;
            public int maxExpertise;
            public bool allowOverlap;
        }

        static bool Prepare()
        {
            if (!VSECompat.Active)
            {
                return false;
            }
            Type defType = AccessTools.TypeByName("VSE.Expertise.ExpertiseDef");
            Type settingsType = AccessTools.TypeByName("VSE.SkillsModSettings");
            Type recordType = AccessTools.TypeByName("VSE.ExpertiseRecord");
            Type trackerType = AccessTools.TypeByName("VSE.ExpertiseTracker");
            Type trackersType = AccessTools.TypeByName("VSE.ExpertiseTrackers");
            if (defType == null || settingsType == null || recordType == null || trackerType == null || trackersType == null)
            {
                Log.Warning("[Transcendence] Vanilla Skills Expanded types not found; per-skill expertise slots disabled.");
                return false;
            }
            settingsField = AccessTools.Field("VSE.SkillsMod:Settings");
            maxExpertiseField = AccessTools.Field(settingsType, "MaxExpertise");
            allowOverlapField = AccessTools.Field(settingsType, "AllowExpertiseOverlap");
            minLevelField = AccessTools.Field(settingsType, "LevelToGetExpertise");
            defSkillField = AccessTools.Field(defType, "skill");
            recordDefField = AccessTools.Field(recordType, "def");
            allExpertiseProp = AccessTools.Property(trackerType, "AllExpertise");
            expertiseOfPawn = AccessTools.Method(trackersType, "Expertise", new[] { typeof(Pawn) });

            bool ok = settingsField != null && maxExpertiseField != null && allowOverlapField != null && minLevelField != null
                      && defSkillField != null && recordDefField != null && allExpertiseProp != null && expertiseOfPawn != null;
            if (!ok)
            {
                Log.Warning("[Transcendence] Vanilla Skills Expanded members not found; per-skill expertise slots disabled.");
            }
            return ok;
        }

        static MethodBase TargetMethod() => AccessTools.Method("VSE.Expertise.ExpertiseDef:CanApplyOn");

        static void Prefix(out SavedSettings __state)
        {
            __state = default;
            object settings = settingsField.GetValue(null);
            if (settings == null)
            {
                return;
            }
            __state.settings = settings;
            __state.maxExpertise = (int)maxExpertiseField.GetValue(settings);
            __state.allowOverlap = (bool)allowOverlapField.GetValue(settings);

            maxExpertiseField.SetValue(settings, int.MaxValue); // toplam sınır yok
            allowOverlapField.SetValue(settings, true);          // aynı yetenekte birden fazla uzmanlık serbest
        }

        static void Postfix(object __instance, Pawn pawn, ref bool __result, ref string reason, SavedSettings __state)
        {
            if (!__result || __state.settings == null)
            {
                return; // VSE kendi sebebiyle reddetti (tutku yok, seviye düşük...) -> olduğu gibi bırak
            }
            var skill = (SkillDef)defSkillField.GetValue(__instance);
            SkillRecord record = pawn?.skills?.GetSkill(skill);
            if (record == null)
            {
                return;
            }
            int firstSlotLevel = (int)minLevelField.GetValue(__state.settings);
            int slots = ExpertiseSlots.SlotsFor(record.Level, firstSlotLevel);
            int used = CountExpertiseIn(pawn, skill);
            if (used < slots)
            {
                return; // boş hak var
            }

            __result = false;
            int next = ExpertiseSlots.NextSlotLevel(used, firstSlotLevel);
            reason = next > 0
                ? "Transcendence_ExpertiseNextAt".Translate(used, slots, next)
                : "Transcendence_ExpertiseAllUsed".Translate(used, slots);
        }

        static void Finalizer(SavedSettings __state)
        {
            if (__state.settings == null)
            {
                return;
            }
            maxExpertiseField.SetValue(__state.settings, __state.maxExpertise);
            allowOverlapField.SetValue(__state.settings, __state.allowOverlap);
        }

        static int CountExpertiseIn(Pawn pawn, SkillDef skill)
        {
            object tracker = expertiseOfPawn.Invoke(null, new object[] { pawn });
            if (tracker == null || allExpertiseProp.GetValue(tracker) is not System.Collections.IList list)
            {
                return 0;
            }
            int count = 0;
            foreach (object rec in list)
            {
                object def = recordDefField.GetValue(rec);
                if (def != null && defSkillField.GetValue(def) == skill)
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>
    /// Uzmanlık panelinin altındaki açıklama "Sadece {0} uzmanlık seçebilirsin" diyor; artık doğru değil.
    /// VSE'nin çeviri anahtarını değiştirmek yerine (bu VSE'yi etkilerdi) sadece bu ekranda
    /// kendi anahtarımızı kullandırıyoruz: ldstr "VSE.ExpertiseDesc" -> "Transcendence_ExpertiseDesc".
    /// </summary>
    [HarmonyPatch]
    public static class Patch_VSE_ExpertisePanel_Description
    {
        static bool Prepare() => VSECompat.Active && AccessTools.TypeByName("VSE.ExpertiseUIUtility") != null;

        static MethodBase TargetMethod() => AccessTools.Method("VSE.ExpertiseUIUtility:DoExpertisePanel");

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var code in instructions)
            {
                if (code.opcode == System.Reflection.Emit.OpCodes.Ldstr && code.operand is "VSE.ExpertiseDesc")
                {
                    code.operand = "Transcendence_ExpertiseDesc";
                    count++;
                }
                yield return code;
            }
            if (count != 1)
            {
                Log.Warning($"[Transcendence] VSE DoExpertisePanel: expected 1 description key, found {count}.");
            }
        }
    }
}
