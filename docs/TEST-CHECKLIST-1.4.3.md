# Stackmaster 1.4.3 diagnostic live test checklist

Local diagnostic candidate only. It is not release-approved and intentionally changes no gameplay behavior from 1.4.2.

## Setup

- [ ] Remove or disable 1.4.2, install only `JStack424-Stackmaster-1.4.3.zip`, and fully restart Valheim. **Expected:** the log says `Stackmaster 1.4.3 loading`; no compatibility-gate error appears.
- [ ] Use the same long connected Workbench mesh and the same distant source chest from the 1.4.2 test. Keep the BepInEx log for the whole sequence. **Expected:** ordinary HUD/gameplay behavior is unchanged; local lines prefixed `[distance-diagnostic]` and `[ownership-diagnostic]` appear only around source tracking and meaningful transitions.

## One short conclusive sequence

- [ ] While still inside the reliable range, select/place one piece whose exact material cost requires the distant chest. **Expected:** this primes the bounded tracker with that chest's `ZDOID`; one `phase=selected` ownership line records distance, captured/current owner and revisions, local session, `ZNetView`/`Container` ownership flags, reference agreement, scope, and lifecycle.
- [ ] Walk slowly across the repeatable read-only cutoff near 170 m while keeping a requirement display visible, pause just beyond it, then walk back. **Expected:** only transition lines appear (not one line per HUD frame). At the outward boundary the known chest reports either (a) `containerLoaded=false` with `managerZdoPresent=true`, proving scene-Container unload while client replication remains, or (b) `managerZdoPresent=false`, proving the chest's ZDO also left the client manager. `included=false` and source units should drop at the same transition, then reverse on return.
- [ ] Move to the unreliable placement band around 100–153 m. Make one click, wait for its result, immediately repeat once, wait 0.5–1.0 seconds, then click once more. **Expected:** every ownership state change is logged with elapsed milliseconds. The final line distinguishes ownerless assignment timeout, remote-owner RPC timeout/rejection, local acquisition, ownership-view convergence/disagreement, revision mismatch, or previous late-acquisition cleanup/retry-lease state.
- [ ] Compare materials before and after each click. **Expected:** every successful placement deducts exactly the vanilla cost; every failed attempt deducts nothing. Any loss, duplication, or free output is a stop condition.
- [ ] Confirm the log stays bounded while standing still for several seconds. **Expected:** unchanged HUD/ownership states do not repeat; read-only observation is throttled to at most twice per second and emits only transitions.

## Evidence to return

- Complete `BepInEx/LogOutput.log` covering the sequence.
- Approximate distances for the outward/inward read-only transition and each placement click.
- Which clicks succeeded or failed, and whether material deductions were exact.

## Rollback

- **Rollback version:** Stackmaster **1.3.5**, followed by a **full Valheim restart**.
