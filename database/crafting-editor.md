# Crafting & Mastery Editor

Open **Crafting & Mastery Editor** from the toolbox editor list. **Crafting Editor — Legacy** continues to edit `t_factory_item`; it does not control the new system.

The new editor uses the toolbox's configured content database and item locale. It requires the game's crafting migrations through **0017**. It does not apply migrations, create crafting tables, or change player progression.

## Deploying toolbox updates

The desktop `Start-ToolBoxNG.cmd` launcher runs `artifacts/app/LastChaos ToolBoxNG.exe`. Building into `bin/Release` alone does not update that application. Prepare a Release `win-x64` self-contained publish, close the toolbox normally, then deploy the changed published application files into `artifacts/app`. Verify their hashes against the publish output. Preserve the installed `Settings.ini`, `Resources` directory, logs and additional native dependencies; do not replace the runtime folder wholesale. Back up files being replaced. The crafting editor must appear as **Crafting & Mastery Editor**, alongside **Crafting Editor — Legacy**.

## Author a recipe

1. Select **New**, or duplicate a similar recipe. Pick Smithing, Alchemy or Cooking, the output item and quantity, craft duration in seconds, and display order.
2. Add ingredients by ID or the existing item picker. Item names, icons, Open item and Where used are available for each row.
3. In **Learning**, choose automatic availability or **Requires a teaching item**, then assign one or more enabled manual items. The **Teaching Items** tab can assign a single book to several recipes at once. Supported books are non-timed Once items with subtype 1, 2 or 4.
4. Set minimum skill and the skill at which gains stop. The editor previews the game's derived 100% / 60% / 20% / 0% bands. Intermediate thresholds are not stored.
5. Set mastery XP per craft, total XP required and up to eight permanent flat stat bonuses. The editor estimates crafts, duration and materials from zero mastery. Required XP of zero disables mastery and requires zero XP per craft and no bonuses.
6. Optional **Bonus outputs** preserve processing's random extras. This is one exclusive roll per craft, with up to eight outcomes and two decimal places of percentage precision. The unused percentage gives no extra item.
7. **Save all changes** validates and commits the entire draft in one transaction.

Use Ctrl/Shift selection followed by **Bulk edit** to change duration, skill endpoints, mastery XP or a mastery bonus across recipes. **Validate / Overview** lists errors, progression gaps by profession, and combined possible rewards.

Recipe IDs are generated and permanent after saving. **Retire / Enable** changes availability while preserving existing earned rewards. **Discard new** only removes an entry that has never been saved. Revert / Reload discards the whole draft after confirmation.

## Teaching items and Item Editor

The **Teaching Items** tab immediately lists existing manual items, including books with no recipe assigned yet. Search by item name or ID, or use the **Assigned / Unassigned** filters. Each row shows its recipe count and status; selecting it shows the recipes it teaches. Disabled, timed or invalid linked items remain visible so their definitions can be corrected. Counts follow the current draft, and navigating the list does not change any data. The ID field and item picker can also jump directly to a book.

**New recipe-book item** asks for an existing supported manual as a template and opens a copied draft in Item Editor. Save that item normally, deploy its item data, then reload the crafting editor and assign its new ID. Copying does not automatically attach the original manual's crafting recipes to the new item.

Item Editor includes a **Crafting & Mastery** reference panel at the bottom of its General panel. It lists saved recipe references and opens teaching assignments. Deleting an item used as an output, material, teaching manual or random bonus is refused, including retired recipe references. The deletion transaction checks again to cover a concurrent new reference.

Item definitions still use the normal item pipeline. New items or changes to item type, enabled state, stackability or stack limit must also reach the running game's item prototypes. Merely assigning an existing suitable item as a teaching book needs no item-data export. After editing item definitions, reload crafting before making further catalogue changes.

## Master's Ranks

Create, duplicate, reorder or retire ranks. Each has a stable identity, unique display order, name, total mastered-recipe threshold across all professions, emblem filename, and up to eight permanent bonuses. Cumulative rewards are shown for the selected rank.

**Move up/down** moves the rank identity and rewards while keeping thresholds attached to progression positions. Earned rank IDs remain earned. Directly editing the order/threshold is also possible; Save requires both to increase strictly across all ranks, including retired ones.

The emblem picker lists `.tex` files in the configured client's `Data/Interface` directory and previews the first frame. Raw and DXT1/DXT3/DXT5 version-4 textures are supported. Unsupported preview formats can still be assigned by valid filename; the game must support and have that texture. This editor does not deploy artwork.

Changing a bonus changes the bonus for characters who already earned that mastery/rank. Raising an XP/rank threshold does not revoke an earned achievement. Retiring prevents new unlocks and preserves earned bonuses. Historical mastered recipes still count toward rank progression.

## Save, reload and version control

Saves affect these eight **authored content** tables only:

- `t_crafting_recipe`, `t_crafting_ingredient`, `t_crafting_manual`, `t_crafting_bonus_output`
- `t_crafting_mastery`, `t_crafting_mastery_bonus`
- `t_crafting_master_rank`, `t_crafting_master_rank_bonus`

The editor locks and checks the current catalogue against its loaded snapshot before writing. If another editor changed it, Save refuses the overwrite and retains the draft. Revert / Reload then reapply the intended changes. Current item definitions are checked again within the save transaction. Partial SQL failure rolls back the entire save. All eight tables must be InnoDB; unreviewed custom triggers are refused.

For edits that use already deployed item definitions and artwork, close and reopen `/craft` to refresh in game. No rebuild or server restart is needed for crafting catalogue changes. New item prototypes still require the existing server item-loading and client `.lod` export/deployment workflow; new emblem textures require asset deployment.

After Save, **Export crafting seeds** asks for the game Git checkout and runs its existing `x64-server/db/export-seed.sh` for exactly these eight tables. This button supports the local Docker Compose database using the toolbox's root credentials and `ep4_data`. Git for Windows and Docker must be available. The exporter verifies that the Compose service is the same database the editor loaded, holds the catalogue stable while exporting, and stages files before replacing the seeds. Passwords are passed through environment variables, never command-line arguments. Other connection setups can still Save; use the normal exporter command outside the toolbox for those setups.

Review and commit the resulting `x64-server/db/seeds/ep4_data` diff. Export new or changed `t_item` content separately, and promote exported client data into the game's authored asset tree. This button never exports player XP, learned recipes, earned ranks, inventory or character stat points.

## Existing engine limits

The editor exposes authored data, not a new rules engine. Existing limits remain: 4,096 recipes, 64 ranks (both including retired entries), skill cap 50, ten materials, eight teaching manuals, eight random outputs, eight distinct bonus effects per definition, and craft durations of 0.001–600 seconds. Supported bonuses are the game's flat effects 0–23 and HP/MP regeneration 102/103. Individual amounts and the combined total in each underlying stat channel may not exceed 32,767; overlapping All/Physical effects and retired rewards are included.

The global skill cap, 100/60/20 chances, and one stat point per crafting skill level are still engine rules. Changing those requires coordinated game code work.

## Verification

See [CraftingVerification](../tests/CraftingVerification/README.md) for the isolated MariaDB suite and offline WinForms previews. No live game client is launched by that suite.

For a later gameplay check, create a test recipe using deployed items, assign a supported manual, save, reopen `/craft`, use the manual, and verify the recipe/materials/timing. Craft through mastery and a rank threshold, then reopen the editor and confirm the saved values. This implementation was verified with database fixtures and offline UI rendering; no gameplay session was driven.
