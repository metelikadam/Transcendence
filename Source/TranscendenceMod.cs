using HarmonyLib;
using UnityEngine;
using Verse;

namespace Transcendence
{
    /// <summary>
    /// Mod giriş noktası. "Mod" alt sınıfı çok erken yüklenir (Def'ler henüz hazır değil),
    /// ama ayarlar menüsü için gereklidir. Harmony yamalarını da burada uyguluyoruz:
    /// yamalar sadece tiplere/metotlara dokunur, Def'lere ihtiyaç duymaz.
    /// </summary>
    public class TranscendenceMod : Mod
    {
        public const string HarmonyId = "metelikadam.Transcendence";

        public static TranscendenceSettings Settings;

        public TranscendenceMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<TranscendenceSettings>();

            // Harmony 3 (RimWorld 1.7): parametresiz PatchAll() kullanımdan kalktı, assembly veriyoruz.
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(typeof(TranscendenceMod).Assembly);

            Log.Message($"[Transcendence] Loaded. Skill cap is now {SkillMath.NewMaxLevel}.");
        }

        public override string SettingsCategory() => "Transcendence";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);

            list.Label("Transcendence_XpDifficulty".Translate(Settings.xpDifficulty.ToString("0.00")),
                tooltip: "Transcendence_XpDifficultyDesc".Translate());
            Settings.xpDifficulty = list.Slider(Settings.xpDifficulty, 0.1f, 5f);

            list.Gap();

            list.Label("Transcendence_EffectStrength".Translate(Settings.effectStrength.ToString("0.00")),
                tooltip: "Transcendence_EffectStrengthDesc".Translate());
            Settings.effectStrength = list.Slider(Settings.effectStrength, 1f, 5f);

            list.Gap();
            if (list.ButtonText("Transcendence_Reset".Translate()))
            {
                Settings.Reset();
            }

            list.End();
            base.DoSettingsWindowContents(inRect);
        }
    }

    public class TranscendenceSettings : ModSettings
    {
        public const float DefaultXpDifficulty = 1f;
        public const float DefaultEffectStrength = 2f;

        /// <summary>21-40 arası seviyeler için gereken XP çarpanı.</summary>
        public float xpDifficulty = DefaultXpDifficulty;

        /// <summary>20 üstü her seviyenin, vanilla'nın son seviyelerine göre kaç kat etki verdiği.</summary>
        public float effectStrength = DefaultEffectStrength;

        public void Reset()
        {
            xpDifficulty = DefaultXpDifficulty;
            effectStrength = DefaultEffectStrength;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref xpDifficulty, "xpDifficulty", DefaultXpDifficulty);
            Scribe_Values.Look(ref effectStrength, "effectStrength", DefaultEffectStrength);
            base.ExposeData();
        }
    }
}
