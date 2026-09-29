# Stackmaster 1.4.0 live test checklist

This is a local test candidate, not a production-approved release. If any regression blocks play or any item count is wrong, roll back to **Stackmaster 1.3.5** and fully restart Valheim.

## Fuel Quick Grab

- [ ] **Container precedence:** Aim at a vanilla chest and press `Left Alt + E`. Expected: ordinary Quick Stack runs; no fuel-specific result appears.
- [ ] **Ordinary interaction and hint:** Aim at each supported device. Expected: vanilla `E` text and normal interaction/loading remain unchanged, with a second localized `[Alt + E] Get <prioritized fuel>` line shown only for recognized fuel targets.
- [ ] **Hovered child/switch:** Aim directly at the live fuel-input switch on a Smelter/Kiln and press `Left Alt + E`. Expected: Stackmaster resolves the owning device, shows the fuel hint, and withdraws the selected fuel instead of reporting an invalid target.
- [ ] **Fireplace family:** Test a refillable campfire/hearth/bonfire with fuel in an eligible home chest. Expected: `Left Alt + E` pulls fuel but does not add fuel to or otherwise operate the fireplace.
- [ ] **Smelter:** Test a Smelter with coal in an eligible home chest. Expected: coal is withdrawn into the backpack; ore/output behavior is unchanged and the press does not load coal.
- [ ] **Charcoal Kiln priority:** Test Wood, Fine Wood, and Core Wood in eligible chests, then remove higher-priority types one at a time. Expected: each press pulls exactly one type in the order Wood → Fine Wood → Core Wood; types never mix.
- [ ] **Cooking station:** Test a fuel-enabled cooking station with its runtime fuel item available. Expected: the correct fuel enters the backpack; no food or fuel is loaded by the same press.
- [ ] **Shield Generator priority:** Test Bone Fragments and Charred Bones in the device's live displayed order, then remove the first type. Expected: each press follows that exact runtime order, pulls only one type, and does not load the generator.
- [ ] **Battering Ram priority:** Test Wood, Fine Wood, Core Wood, and Ashwood with higher priorities removed one by one. Expected: the first stocked type in that order is pulled and the ram is not operated/loaded.
- [ ] **Partial and cap behavior:** Put less than one maximum stack in storage, then more than one maximum stack. Expected: partial stock is accepted; abundant stock transfers no more than one canonical full stack.
- [ ] **Capacity and weight:** Repeat with too little slot or carry capacity. Expected: no chest item is lost, duplicated, or partially stranded; a truthful failure message appears.
- [ ] **Home-only scope:** Put fuel in (a) the same connected Workbench mesh, (b) a nearby but disconnected mesh, and (c) a nearby-radius chest while outside any mesh. Expected: only (a) is eligible; (b) and (c) remain untouched.
- [ ] **Player stock excluded:** Carry some matching fuel and leave eligible home chests empty. Expected: the action reports no stored fuel and does not treat carried stock as a source.
- [ ] **Ownership/busy chest:** Have another player open a required chest during the action. Expected: the transfer cancels safely with no item-count change or stuck chest.

## Whole-home crafting stations

- [ ] **Connected Workbench home:** Place one Forge/Stonecutter/Artisan Table inside a connected Workbench mesh, move to another point in that same mesh outside the station's vanilla radius, and select a gated Hammer piece. Expected: the piece is build-eligible when its normal material/DLC requirements are met.
- [ ] **Hammer station display:** While the previous piece is selected outside the station's vanilla radius but inside the same home, inspect its Hammer requirement row. Expected: the exact station icon stays available and does not flash red or show `None`.
- [ ] **Every vanilla gate sampled:** Check representative Workbench-, Stonecutter-, Forge-, Black Forge-, Artisan Table-, and Galdr Table-gated Hammer pieces. Expected: each uses the exact required station type; one station never substitutes for another.
- [ ] **Build point stays in the same home:** Stand inside the home and aim the placement ghost just outside that connected Workbench mesh. Expected: extended station eligibility is denied at the boundary; moving the ghost back inside the same mesh restores it.
- [ ] **Vanilla-first behavior:** Stand inside a station's normal radius. Expected: behavior is identical with the whole-home option on or off.
- [ ] **Toggle independence:** Test all four combinations of **Allow building from storage** and **Enable whole-home crafting stations**. Expected: station coverage and chest-backed materials change independently; station-only mode still requires player-held materials exactly as vanilla does.
- [ ] **Disconnected homes:** Place the required station in a nearby but separate Workbench mesh. Expected: it grants no build eligibility in the player's mesh.
- [ ] **No station bridging:** Place Forge/Stonecutter zones between two Workbench meshes. Expected: non-Workbench stations never connect the homes.
- [ ] **Outside-home fallback:** Stand outside every Workbench mesh but near the configured storage radius and a station. Expected: whole-home coverage does not activate; only vanilla station radius applies.
- [ ] **Loaded/active requirement:** Unload, destroy, disable, or move the only qualifying station. Expected: extended eligibility disappears without stale access or a crash.
- [ ] **Free-build/DLC/material rules:** Check no-cost/free-build, an unavailable DLC-gated piece if possible, sufficient materials, and insufficient materials. Expected: only station eligibility changes; all other vanilla checks remain unchanged.
- [ ] **Same-home removal/repair:** Dismantle and repair a station-gated piece while outside the required station's vanilla radius but inside the same connected home. Expected: both succeed under the same exact station identity/topology rule that allowed placement.
- [ ] **Removal fail-closed cases:** Repeat with the option off, a disconnected/wrong/unloaded station, or a piece outside the player's mesh. Expected: Valheim still shows the ordinary missing-station denial; repair and all other removal checks remain unchanged.

## Rapid chest-backed crafting

- [ ] **Same recipe, same chest:** Put enough materials for at least four copies of one recipe in one eligible chest, then start the next craft immediately each time the prior bar completes. Expected: all four clicks start and finish; every output appears once; the chest loses exactly four recipe costs; there is no alternating failed click or stuck in-use chest.
- [ ] **Different recipes, same chest:** Alternate quickly between two recipes whose materials are in the same eligible chest. Expected: each selected recipe starts once, receives the correct output, and consumes only its exact cost; cleanup from the prior recipe never cancels the next.
- [ ] **Same and different recipes, different chests:** Repeat rapid crafting with one recipe drawing across multiple chests, then alternate recipes that draw from different chests. Expected: every craft succeeds once and only the selected minimum source set is debited exactly.
- [ ] **Cancel/failure/close boundaries:** During separate chest-backed attempts, cancel the bar, force an ordinary validation failure by changing required stock, and close the inventory before completion; immediately start a valid craft after each. Expected: the interrupted craft grants no output and leaks no debit, reservation, ownership, or in-use state; the next valid craft succeeds on its first click.
- [ ] **Disconnect boundary:** Begin a chest-backed craft, disconnect/log out, reconnect, and craft again. Expected: the interrupted operation cannot affect the new session; the new craft succeeds once with exact item conservation and no stuck chest or Stackmaster disable.

## Regression and multiplayer

- [ ] Quick Stack, chest auto-sort, protected targets, Quick Grab Materials, reservation cards, reservation decrement, building from storage, crafting/upgrading from storage, and exact requirement totals still behave as in 1.3.5.
- [ ] With a modded host and guest, repeat one fuel transfer and one whole-home station build. Expected: one exact debit/credit, correct eligibility, no blocked chest, and no disagreement after reconnect.
- [ ] With a dedicated server and an unmodded peer if available, repeat the compatibility checks. Expected: no required server install, no world corruption, and no new behavior on the unmodded client.
- [ ] **World-switch reservation UI:** In world A, create a reservation and confirm both the inventory row and Hammer row appear. Log out, enter world B, open inventory and Hammer, then log out and reconnect to world A. Expected: both rows are recreated for the active scene, no `MissingReferenceException` appears, and Stackmaster remains active without restarting Valheim.
- [ ] Disable/re-enable or unload/reload Stackmaster after an interrupted action. Expected: no stale ownership, reservation, input suppression, station eligibility, or duplicate action.

If an item is lost/duplicated, a device operates on the chord, a separate home grants station access, or Stackmaster disables after a world change, stop testing, return to **Stackmaster 1.3.5**, and fully restart Valheim.
