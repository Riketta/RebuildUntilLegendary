# Rebuild Until LEGENDARY
![Preview](About/Preview.png)

Keep rebuilding a building until it reaches the quality you want - then stop by itself.
Toggle it on, pick the target quality and optionally a single builder; the mod deconstructs
every "not good enough" result and places a fresh blueprint on the same spot until the
target quality is rolled.

Requires [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077).

## How it works

1. Select a quality-capable building (art, furniture, turrets, benches - anything with a
   quality stat, vanilla, DLC or modded) and click the new **Rebuild until...** toggle.
2. Pick the target quality (e.g. *Until Legendary or better*).
3. Pick who may build it: **Anyone (default)** or one specific pawn - the picker lists
   colonists with portraits and their construction level, like the gene extractor's pawn
   selection.
4. That's it. The loop:
   - the finished building is below the target quality -> it is deconstructed (normal
     vanilla deconstruction, with the usual material refund) and a fresh blueprint is
     placed on the same spot with the same material, style, rotation and storage settings;
   - the finished building is at or above the target quality -> the loop stops and the
     toggle unchecks itself with a success message.

While the loop runs you can watch and control it without a finished building: the
in-progress blueprint or frame gets a **Stop rebuilding** button showing the target
quality, builder and attempt count.

Everything is stored per building and survives saving/loading, so a rebuild in progress
continues after loading a save.

## A training and quality-grinding tool

The loop doubles as a practice range. Every attempt is a normal construction job, which
means:

- **Skill training** - the builder earns construction experience for the frame work on
  every attempt, so a dedicated builder levels up while the loop runs;
- **Quality grinding** - vanilla quality rolls scale with the builder's skill, so as the
  pawn improves, each new attempt rolls better quality on average and the loop converges
  toward masterwork and legendary results by itself.

Pick something cheap like wooden stools to train a rookie, or aim the loop at a grand
sculpture once the builder is ready. Choosing a single builder in the picker keeps the
training focused: only that pawn works the blueprint, so the experience goes exactly
where you want it.

## Builder restriction

With a specific pawn chosen, nobody else can do construction work on that rebuild:
building the frame and finishing it are limited to the chosen pawn - both automatic
work assignment and right-click orders. The restriction is lifted automatically if the
pawn dies, so the job can still be finished by anyone.

A mod option (**Helpers haul materials**, on by default) lets other colonists deliver
materials to the restricted blueprint so it fills faster; the chosen builder still does
all the actual construction and rolls every quality.

Quality still matters: if the chosen pawn cannot build the thing (skills, ideology,
backstory), vanilla rules apply and the loop simply waits.

## Safety valves

The mod never fights the player. It stops by itself (with a message) when:

- the blueprint or frame is canceled;
- the building is deconstructed by the player (only the mod's own below-target
  re-rolls keep the loop going);
- the building is replaced with a new blueprint - upgrades and Replace Stuff-style
  material swaps end the loop instead of claiming the replacement;
- the spot is taken over by a different building;
- the building is minified or otherwise moved away;
- the building disappears without a trace.

## Settings

In the mod options (developer mode only):

- **Debug logging** - every step of every rebuild loop: activation, destruction with its
  mode, blueprint placement with def/stuff/rotation, quality rolls, completion.
- **Verbose logging** - additionally logs high-frequency events such as every denied
  build attempt (throttled) and every placement retry.

## Known limitations

- Each attempt costs materials like a normal deconstruct-rebuild cycle would.
- Ideology-specific styles are carried over, but a rebuilt storage building leaves its
  storage group and rejoins with the same filter settings applied.
- Switching the toggle off stops the loop immediately; a blueprint that was already
  placed stays on the map as a normal blueprint (cancel it with the cancel designator
  if unwanted).
- With **Helpers haul materials** disabled, only the chosen pawn delivers materials
  too, which can make each attempt slow on big builds.

## For developers

Source is in `Source/RebuildUntilLegendary`. Build in Release:

```
dotnet build -c Release Source/RebuildUntilLegendary/RebuildUntilLegendary.csproj
```

The project reads the game path from the `RimWorldDir` property (default
`E:\SteamLibrary\steamapps\common\RimWorld`).

Harmony patches, each wrapped so one failure never breaks the others:

- `Thing.Destroy` (prefix) - tracks destruction of tracked blueprints/frames/buildings;
- `ThingWithComps.GetGizmos` (postfix) - adds the toggle to quality-capable buildings;
- `WorkGiver_ConstructDeliverResourcesToBlueprints.HasJobOnThing/JobOnThing`,
  `WorkGiver_ConstructDeliverResourcesToFrames.HasJobOnThing/JobOnThing`,
  `WorkGiver_ConstructFinishFrames.JobOnThing` (postfixes) - enforce the single-builder
  restriction.

Nothing is hardcoded: quality categories come from `QualityUtility.AllQualityCategories`,
builder candidates are computed from whatever pawns the game currently has, and buildings
are matched by def + cell as the game defines them, so DLC/mod content works unchanged.

## License

MIT - see `LICENSE.txt`.
