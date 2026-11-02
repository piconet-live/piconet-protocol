# protocol/packs

The stat packs a server of ours can be started with (`--stat-pack FILE`; `handbook/stat-packs.md`): each a pack
file, text, the changed values only. To write one by hand, or to make one from a mod: [stat-pack-guide.md](stat-pack-guide.md).

| File | What it is |
|---|---|
| `gx-unleashed.fzstats` | **GX Unleashed**, by **AKC12**: a rebalance of the machines and the custom parts, 1,320 values, and the three rule numbers its main module changes (boost energy, speed cap, dash plate: `handbook/stat-pack-constants.md`). Included with permission (AKC12; Twilight for the main module's code). Made by the launcher's import from a copy of the mod's car data file and main module, which are not in the repository; the file's head says how |
| `fzero-gx-plus.fzstats` | **F-Zero GX+**, by **Emarcee**: a rebalance of the machines and the custom parts, 2,062 values (one machine's negative), the dash plate's boost its main module changes (the same as GX Unleashed's) and its one change to the machine setting (DriftFastLoss 1). Included with permission (Emarcee). Made by the launcher's import from a copy of the mod's car data file and main module, which are not in the repository; the file's head says how, and what of the mod the pack does not carry (names and menu text) |

A pack here is somebody's work: it is added only with its author's permission, with the author named in its
`author` line and in the comment at its head, and in the patch notes. A changed game file (`cardata.lz`) is never
committed: the asset scan refuses it, and the import writes the differences only. Every pack here is read by the
tests as a server reads it (`Online.Tests`, `RepositoryPacksTests`; `Protocol.Tests`, `StatPackConstantsTests` reads GX Unleashed's), so a pack the game would refuse fails a commit's
landing, not a deployment.

No pack is the unmodified game: nothing in this repository makes a pack a default. A server runs a pack only when it
is told to (`--stat-pack FILE` or `FZGX_STAT_PACK`); the service's deployment tells its stacks to, and the launcher
chooses one for a fresh Practice, each in its own repository.
