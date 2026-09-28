# Stackmaster 1.3.5 live test checklist

Rollback if any regression blocks play: **remove 1.3.5 and return to Stackmaster 1.2.0.** Until 1.3.5 passes, avoid Quick Grab on 1.2.0 as well: the source audit found the same ordinary detached-source path in that release.

## Critical source-debit and conservation checks

- [ ] Put one ordinary recipe's exact materials in one nearby eligible chest, record chest and player counts, then Alt-click it once.
  - Expected: the player gains exactly one recipe set, the chest loses exactly the same materials once, and the combined count is unchanged.
- [ ] Repeat the ordinary Alt-click until the chest no longer contains a complete set.
  - Expected: every successful click debits the real chest; the first incomplete click moves nothing and creates only the ordinary piece reservation behavior.
- [ ] Repeat the exact-count test with a vanilla or modded piece whose visible name contains `Stack` or `Pile`.
  - Expected: the player gains exactly one complete recipe set, source chests lose it exactly once, and no reservation/card/orange allocation/additive target/sort priority/placement decrement is created.
- [ ] Split one complete recipe across two or more nearby chests and Alt-click once.
  - Expected: each chest loses its planned share exactly, the player gains the complete set once, and the combined total across all chests plus player is unchanged.
- [ ] Test a multi-ingredient ordinary recipe with ingredients split across multiple stacks/chests.
  - Expected: every ingredient and chest is debited exactly once; no ingredient is duplicated, skipped, or overdrawn.
- [ ] Open the debited chest after the transfer, then leave/rejoin or otherwise force the chest to reload if convenient.
  - Expected: the reduced counts persist; no stock reappears.
- [ ] Run Quick Stack after a successful Quick Grab.
  - Expected: depositing the gathered items cannot increase the total amount across player and storage.

## Failure and rollback checks

- [ ] Leave one ingredient one unit short for an ordinary recipe and for a Stack/Pile recipe.
  - Expected: no source or player quantity changes. Ordinary pieces may add their approved reservation; Stack/Pile pieces create no reservation.
- [ ] Fill the player inventory so the complete recipe cannot fit, then Alt-click.
  - Expected: nothing moves and source counts remain exact.
- [ ] Approach the carry-weight limit so the complete recipe would exceed it, then Alt-click.
  - Expected: nothing moves and source counts remain exact.
- [ ] If practical in multiplayer, have another player open or take ownership of a required chest during preparation.
  - Expected: the transfer fails closed or rolls back fully; no partial player credit and no partial chest debit survives.

## Hammer reservation-row checks

The dedicated-server verification assemblies do not contain the rendered client HUD hierarchy, so these position, clipping, and click-close checks require the live Valheim client.

- [ ] Create at least one ordinary reservation and open the current tabbed Hammer menu.
  - Expected: the reservation row is visible directly above the full Categories / Materials / Recent / Favorites tab strip.
- [ ] Repeat with a narrow resolution or larger UI scale if practical.
  - Expected: the row remains above the full tab strip, stays inside the safe area, is not clipped by the BuildUi panel, and does not overlap the tabs.
- [ ] Click a reservation card body.
  - Expected: that exact piece is selected through the normal Valheim selection path, the Hammer menu closes, and the reservation row hides in the same click.
- [ ] Click the card's separate `-` / final-count `X` control.
  - Expected: exactly one reservation is removed without selecting the piece or moving materials.

## Regression checks

- [ ] Confirm ordinary reservation creation, orange attribution, reserved-first sorting, successful-placement decrement, and touched-chest sorting still behave as in the approved 1.3.3/1.3.4 design.
- [ ] Confirm normal unmodified recipe clicks, building, crafting, chest use, inventory sorting, and Quick Stack remain unchanged.
