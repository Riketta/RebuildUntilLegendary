# Steam Workshop description

Paste the text below into the Workshop item's description field when publishing
(the BBCode renders on Steam, but not in-game - `About/About.xml` carries its own
plain-text description).

```
[h3]Rebuild Until LEGENDARY[/h3]
Keep rebuilding a building until it reaches the quality you want - and stop by itself. A great tool to improve construction skill and grind perfect quality buildings.

[h3]What it does[/h3]
[b]The rebuild loop[/b]
[list][*]Select any building with a quality stat - art, furniture, turrets, benches, anything from vanilla, DLCs or mods - and toggle "Rebuild until...".
[*]Pick the target quality (e.g. "Until Legendary or better") and who may build it: anyone (default) or one specific pawn, chosen from a picker with portraits and construction levels.
[*]Every finished result below the target quality is deconstructed the normal vanilla way (usual material refund) and a fresh blueprint is placed on the same spot with the same material, style, rotation and storage settings.
[*]As soon as the target quality (or better) is rolled, the toggle switches itself off with a success message.
[*]The in-progress blueprint or frame gets a "Stop rebuilding" button showing the target quality, builder and attempt count, and a running rebuild survives saving and loading.[/list]

[b]Single-builder training[/b]
[list][*]With a specific pawn chosen, only that pawn does the construction - both automatic work assignment and right-click orders. By default other colonists may still deliver materials to fill the blueprint faster (a mod option turns helper hauling off).
[*]Every attempt is a full vanilla construction job, so the chosen builder earns experience each time - and as their skill rises, quality rolls improve and the loop converges on masterwork and legendary by itself. Point it at wooden stools to train a rookie, or at a grand sculpture to grind it out.
[*]If the chosen pawn dies or otherwise leaves the colony, the restriction is lifted so the job can always finish.[/list]

[h3]Settings[/h3]
"Helpers haul materials" (on by default) lets other colonists deliver materials to a restricted blueprint while the chosen builder does all the construction and rolls every quality. Developer mode additionally offers debug logging (every step of every rebuild loop) and verbose logging (denied attempts and retries, throttled).

[h3]Things to keep in mind[/h3]
[list][*]Each attempt costs materials exactly like a manual deconstruct-rebuild cycle would.
[*]The mod never fights the player: canceling the blueprint, deconstructing or replacing the building yourself, or moving it away stops the loop cleanly with a message.
[*]A rebuilt storage building keeps its filter settings but leaves its storage group.
[*]Switching the toggle off stops the loop immediately; a blueprint already placed stays on the map as a normal blueprint.
[*]With helper hauling disabled, only the chosen pawn delivers materials too, which can make each attempt slow on big builds.[/list]

[h3]Compatibility[/h3]
Requires RimWorld 1.6 and [url=https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077]Harmony[/url]; all DLCs are optional.
Nothing is hardcoded - quality categories, builder candidates and building defs are read from the game at runtime, so modded content works unchanged. Safe to add or remove at any time: removing the mod mid-loop just leaves the current building or blueprint as it is.

Source code and details: [url]https://github.com/Riketta/RebuildUntilLegendary[/url]
```
