# Stackmaster 1.4.4 supported-distance-limit live test checklist

Local test candidate only. It is not release-approved. It adds one product rule on top of 1.4.3: inside a connected Workbench mesh, chests more than 100 metres from the player contribute no resources.

## Setup

- [ ] Remove or disable 1.4.3, install only `JStack424-Stackmaster-1.4.4.zip`, and fully restart Valheim. **Expected:** the log says `Stackmaster 1.4.4 loading`; no compatibility-gate error appears.
- [ ] Use a long connected Workbench mesh with a chest full of wood near the far end, the same layout as the 1.4.3 boundary test. Keep the BepInEx log for the whole sequence.

## The boundary sequence

- [ ] Stand well inside 100 m of the chest and place a wood piece that requires chest wood. **Expected:** placement works normally and the chest loses exactly the vanilla cost.
- [ ] Walk beyond 100 m from the chest (the 125–140 m stretch from the 1.4.3 test is ideal) and open the build menu for the same piece. **Expected:** the displayed available wood drops to only what you carry or have in chests within 100 m — the far chest contributes zero.
- [ ] From that same far position, try to place the piece. **Expected:** it refuses immediately (no three-second “checking storage” wait) with “Stackmaster: you're too far away from the required resources (over 100 m); building was cancelled without consuming anything.” Nothing is consumed.
- [ ] Walk back inside 100 m and place again. **Expected:** placement works again without restarting or re-opening anything, and the chest loses exactly the vanilla cost.
- [ ] Put the required wood in two chests — one within 100 m, one beyond — then check the build-menu total from a spot where both are in the mesh. **Expected:** the total counts only the near chest (plus your inventory).
- [ ] Optional: repeat one attempt with Quick Grab Materials (`Left Alt` + click the piece) from beyond 100 m. **Expected:** the reservation is still saved if the piece supports reservations, but no materials move, and the message says you're too far away from the required resources.
- [ ] Compare materials before and after every attempt. **Expected:** successful actions deduct exactly the vanilla cost; every refused attempt deducts nothing. Any loss, duplication, or free output is a stop condition.

## Evidence to return

- Complete `BepInEx/LogOutput.log` covering the sequence.
- Approximate distances for the far refusal and the return into range.
- Which attempts succeeded or failed, and whether material deductions were exact.

## Rollback

- **Rollback version:** Stackmaster **1.3.5**, followed by a **full Valheim restart**.
