# Rebuild Until LEGENDARY

Keep rebuilding a building until it reaches the quality you want - then stop by itself.

Requires [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077).

## What it does

Select a building with quality (a sculpture, a table, a turret, a smelter - anything that
can roll a quality, from vanilla, DLCs or mods) and toggle **Rebuild until...**:

1. Pick the target quality, e.g. *Until Legendary or better*.
2. Pick who may build it: **Anyone (default)** or one specific pawn.

From then on the mod runs the loop: every finished result below the target quality is
deconstructed and a fresh blueprint appears on the same spot with the same material,
style and rotation. As soon as the target quality (or better) is rolled, the toggle
switches itself off and you get a success message.

With a specific pawn chosen, only that pawn can do the construction - other colonists
leave the blueprint alone, both with automatic work and right-click orders. By default
other colonists may still help deliver materials to fill the blueprint faster (a mod
option can turn helper hauling off). If the chosen pawn dies, the restriction is lifted
so the job can finish.

The rebuild survives saving and loading, and the mod never fights the player: canceling
the blueprint, deconstructing or replacing the building, or moving it away stops the
loop cleanly with a message.

## Train builders, grind quality

The loop is also a great training tool. Every attempt is a full vanilla construction
job, so the builder earns experience every single time - and higher construction skill
rolls better quality. Point the loop at something cheap like a wooden stool to level up
a rookie, or let it grind away at that grand sculpture once your crafter is ready: the
same loop trains the pawn and hands you masterwork and legendary buildings on the way.

Each attempt costs materials exactly like a manual deconstruct-rebuild cycle would.
