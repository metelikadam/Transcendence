<img width="1280" height="720" alt="download" src="https://github.com/user-attachments/assets/01140831-6c2f-4284-849c-9a911d35a80f" />
# Transcendence: Skills Beyond 20

A RimWorld 1.7 mod that raises the skill cap from 20 to 40.

- **Levels 0-20** behave exactly like vanilla.
- **Levels 21-40 (Transcendent)** can only be earned through XP. Each level costs 10% more than the last (about 1.9 million XP from 20 to 40 by default), but every level gives a bigger bonus (2x a late vanilla level by default) to all skill-based stats and to crafted item quality.
- Work bills allow skill levels up to 40.
- Mod Settings: XP difficulty (0.1x-5x) and effect strength (1x-5x).

## Compatibility
| Mod | Handling |
|---|---|
| Vanilla Skills Expanded | Passion forget rates apply above 20. Expertise slots are counted per skill: one at 15, then one every 5 levels (max 6). |
| Mad Skills+ | Mad Skills+ handles decay, so there is no double decay. |
| Vanilla Expanded Framework | Gene forget multipliers apply above 20. |
| Mines (and any workbench) | Bill skill range raised to 0-40, with existing saves migrated once. |

Requires [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077).

## Building
Requirements: .NET 10 SDK and RimWorld 1.7 with the Harmony Workshop mod.

1. Edit `RimWorldDir` and `HarmonyDir` in `Source/Transcendence.csproj` if your install paths differ.
2. Run `dotnet build -c Release` in `Source/`.

The DLL is written to `1.7/Assemblies/`, and the mod is copied to `<RimWorld>/Mods/Transcendence` automatically.

## Layout
```
About/          About.xml (packageId: metelikadam.Transcendence)
1.7/Assemblies/ compiled mod DLL
Languages/      English and Turkish keyed strings
Source/         C# source and project
  TranscendenceMod.cs     entry point, Harmony bootstrap, settings
  SkillMath.cs            balance formulas and transpiler helpers
  Patches_SkillRecord.cs  cap, XP curve, decay, level names
  Patches_Effects.cs      skill-based stats and crafting quality above 20
  Patches_UI.cs           skill bar and tooltip
  Patches_Bills.cs        work bill skill range
  Compat_VSE.cs           Vanilla Skills Expanded / VEF compatibility
```

## License
[MIT](LICENSE). RimWorld and its assemblies belong to Ludeon Studios and are not included in this repository.
