# Professions Editor

Open **Professions Editor** from the toolbox editor list. **Crafting Editor — Legacy** continues to edit `t_factory_item`; it does not control the new system.

The new editor uses the toolbox's configured content database and item locale. It requires the game's migrations through **0043**. It does not apply migrations, create tables, or change player progression.

## Deploying toolbox updates

The desktop `Start-ToolBoxNG.cmd` launcher runs `artifacts/app/LastChaos ToolBoxNG.exe`. Building into `bin/Release` alone does not update that application. Prepare a Release `win-x64` self-contained publish, close the toolbox normally, then deploy the changed published application files into `artifacts/app`. Verify their hashes against the publish output. Preserve the installed `Settings.ini`, `Resources` directory, logs and additional native dependencies; do not replace the runtime folder wholesale. Back up files being replaced. The editor must appear as **Professions Editor**, alongside **Crafting Editor — Legacy**.

## Navigation and common workflow

The left navigation groups Crafting (Smithing, Alchemy, Cooking), Gathering
(Mining, Herbalism, Fishing, Wave Absorption), Resources & mastery and Artisan
Ranks. Overview explains the workflow and apply steps. Each gathering profession
has searchable lists and detail sections for its own rules, sources, rewards and
tools. Display units are seconds, metres and percentages; stored millisecond and
basis-point conversions are handled by the editor. Weighted loot shows its real
percentage of the enabled outcomes.

1. Choose a profession and the relevant source/rules section.
2. Edit the draft. New/duplicate, retire/enable and child-order controls operate
   only on the draft. Item/NPC/zone selectors show names alongside permanent IDs.
3. Use Validate to check all related definitions and actual runtime limits.
4. Save all changes commits the related authored content in one transaction.
5. Follow the apply guide, then export and commit the authored content diff.

The common toolbar keeps Save, Revert/Reload, Validate and Export separate.
Browsing and previews never write to the database. Changing a source reward can
create its required resource mastery definition; the Resource mastery button
opens XP targets and tier rewards. New item/NPC prototypes still use their own
editors and normal deployment process.

## Author a recipe

1. Select **New**, or duplicate a similar recipe. Pick Smithing, Alchemy or Cooking, the output item and quantity, craft duration in seconds, and display order.
2. Add ingredients by ID or the existing item picker. Item names, icons, Open item and Where used are available for each row.
3. In **Learning**, choose automatic availability or **Requires a teaching item**, then assign one or more enabled manual items. The **Teaching Items** tab can assign a single book to several recipes at once. Supported books are non-timed Once items with subtype 1, 2 or 4.
4. Set minimum skill and the skill at which gains stop. The editor previews the game's derived 100% / 60% / 20% / 0% bands. Intermediate thresholds are not stored.
5. Set mastery XP per craft and cumulative Bronze, Silver and Gold XP targets. Each successive tier must cost more additional XP than its predecessor. Assign up to eight permanent bonuses per tier; later tiers add their rewards to earlier ones. Zero XP per craft pauses progression without removing the required targets or earned rewards.
6. Optional **Bonus outputs** preserve processing's random extras. This is one exclusive roll per craft, with up to eight outcomes and two decimal places of percentage precision. The unused percentage gives no extra item.
7. **Save all changes** validates and commits the entire draft in one transaction.

Use Ctrl/Shift selection followed by **Bulk edit** to change duration, skill endpoints, mastery XP or a mastery bonus across recipes. **Validate / Overview** lists errors, progression gaps by profession, and combined possible rewards.

Recipe IDs are generated and permanent after saving. **Retire / Enable** changes availability while preserving existing earned rewards. **Discard new** only removes an entry that has never been saved. Revert / Reload discards the whole draft after confirmation.

## Teaching items and Item Editor

The **Teaching Items** tab immediately lists existing manual items, including books with no recipe assigned yet. Search by item name or ID, or use the **Assigned / Unassigned** filters. Each row shows its recipe count and status; selecting it shows the recipes it teaches. Disabled, timed or invalid linked items remain visible so their definitions can be corrected. Counts follow the current draft, and navigating the list does not change any data. The ID field and item picker can also jump directly to a book.

**New recipe-book item** asks for an existing supported manual as a template and opens a copied draft in Item Editor. Save that item normally, deploy its item data, then reload the crafting editor and assign its new ID. Copying does not automatically attach the original manual's crafting recipes to the new item.

Item Editor includes a **Crafting & Mastery** reference panel at the bottom of its General panel. It lists saved recipe, gathering mastery, source loot and tool references and opens teaching assignments. Deleting an item used as an output, material, teaching manual, random bonus, gathering mastery resource, node/herb/fishing loot, gathering tool, herb visual tool or required rod is refused, including retired recipe references. The deletion transaction checks again to cover a concurrent new reference.

Item definitions still use the normal item pipeline. New items or changes to item type, enabled state, stackability or stack limit must also reach the running game's item prototypes. Merely assigning an existing suitable item as a teaching book needs no item-data export. After editing item definitions, reload crafting before making further catalogue changes.

## Artisan Ranks

Create, duplicate, reorder or retire ranks. Each has a stable identity, unique display order, name, mastery-point threshold across crafting and gathering, emblem filename, and up to eight permanent bonuses. Every Bronze/Silver/Gold tier contributes one point (Gold is three total). Historical earned tiers still count. Cumulative rewards are shown for the selected Artisan Rank. In game, `/artisan` opens its independent window; Character → Special Skills also provides a shortcut that can be placed on the action bar.

**Move up/down** moves the rank identity and rewards while keeping thresholds attached to progression positions. Earned rank IDs remain earned. Directly editing the order/threshold is also possible; Save requires both to increase strictly across all ranks, including retired ones.

The emblem picker lists `.tex` files in the configured client's `Data/Interface` directory and previews the first frame. Raw and DXT1/DXT3/DXT5 version-4 textures are supported. Unsupported preview formats can still be assigned by valid filename; the game must support and have that texture. This editor does not deploy artwork.

Changing a bonus changes the bonus for characters who already earned that mastery/rank. Raising an XP/rank threshold does not revoke an earned achievement. Retiring prevents new unlocks and preserves earned bonuses. Historical crafting and gathering tiers still count toward rank progression.

## Gathering mastery

Open **Resources & mastery**, then search by resource name or item ID and filter Mining, Herbalism, Fishing or Wave Absorption. Create resource mastery for an existing valid item or select a source's Resource mastery action. Each resource exposes XP per awarded item, all three cumulative targets and its per-tier rewards. Bonuses add together; use the Earned at column to select Bronze, Silver or Gold. Each tier contributes one Artisan Rank point. The item/profession identity is permanent after publication. Availability derives from enabled sources; retire those sources while retaining mastery definitions and earned rewards. Sources and loot are authored on the corresponding gathering pages.

## Mining and Wave Absorption

Skill rules expose the cap, skill-chance floor/decline, yield scaling and passive
speed/skill-rate settings. Sources bind one NPC prototype to a profession and
configure required skill, base output, round duration and skill chance. Mining's
reference hits and Wave's reference score mean the performance needed for the
base payout, not a completion cap. Passive settings expose session duration,
base tick interval and quantity. Carried tools have required skill and yield bonus.

Wave sources also expose count-in, Perfect/Good windows and points, input cooldown,
network grace, initial/final spacing and approach time, jitter and hold settings.
Balance/timing preview evaluates the current draft at a chosen skill, score and
tool bonus; Wave schedules can be rerolled without starting the game.

Node loot is one weighted outcome. It applies to Wave active/passive and Mining
passive; active Mining uses its single base output. It is not an independent
extra-gem roll. Model profiles are imported from a matching LOD0 mesh bundle using
the game checkout's `tools/gathering/ImportSurfaceProfile.py`. Import generates
and validates draft geometry; normal Save publishes the profile. Deploy matching
model assets separately. Reserved/inactive settings are identified rather than
presented as effective gameplay controls.

## Herbalism

Populations expose zone, NPC, minimum and initial counts, replacement-delay range,
background growth interval, required skill, base skill chance, harvest duration
and the visual gathering tool. The visual tool is not an inventory requirement.
Each population owns candidate sites and one weighted harvest table.

Edit on map opens a draft-only map and synchronized coordinate grid. Right-click
adds a site, drag moves it, the wheel zooms, and middle-drag pans. Exact X/Z,
Y-layer, heading and enabled state remain editable in the grid. Candidate capacity
must support minimum/initial counts. Initial count applies at server startup;
it does not reset live plants when reloading.

The server checks actual terrain/walkability and peace areas. A map marker alone
does not prove accessibility. These candidate sites remain separate from static
`t_npc_regen` rows.

## Fishing

Skill and rod selects the cap, chance floor/decline and required carried rod item.
Waters expose zone/layer, name, surface height, casting distances, cast duration,
bite-delay range, reaction window and skill chance. Each water has a weighted
catch table and a union of polygon pieces. Map editing and the coordinate grid
support adding/moving vertices and reordering polygon vertices. Boundaries must
be simple, non-degenerate polygons with contiguous indices.

Fishable boundaries describe existing world water; they do not create water or
terrain. Fishing currently has no per-water minimum skill, bait or tool-upgrade
bonus system. Rod models and animation behavior remain client assets/code.

## Maximum crafting skill

Open **Settings → Maximum crafting skill** to set the shared cap for Smithing,
Alchemy and Cooking (default 50; supported range 1–65,535). **Save all changes**
commits it with the recipe draft, and closing/reopening `/craft` refreshes it in
the game without a rebuild or restart. Changing this setting participates in
concurrent-edit detection and **Export profession content**.

Lowering the cap preserves characters' earned skills and stat points, even above
the new limit. It stops further skill gains until their level is below the cap
again; it does not prevent mastery or otherwise valid crafting. Recipe requirements
and no-gain levels stay independent. Raise those endpoints for new training content
when needed; raising the global cap alone does not change a recipe's chances.
**Validate / Overview** warns about recipes requiring a level above the current cap.

The current editor requires migrations through 0043 and matching client/GameServer
builds. Subsequent cap edits are purely database driven.

## Alchemy infusions

The **Infusions** section configures the private-hall Alchemy station. It is
separate from Alchemy recipe mastery: an infusion grants the original potion's
full existing skill permanently, rather than copying a percentage of its stats.
The initial four recipes consume **250** Power Potions, Super Haste Potions,
Berserker Potions, or Platinum Adrenaline respectively, plus two secondary
materials. They produce one tradable permanent potion after **5, 6, 8 or 10
hours**. Brewing advances while offline; consuming the finished potion grants
the recipient the permanent effect. Already knowing that effect does not block
production.

- **Brewing recipes:** choose the permanent effect and named finished potion,
  set original-potion cost, output quantity, hours/minutes/seconds and display
  order, and enable or retire the recipe. Accepted jobs keep their original costs,
  output and completion time when you edit a recipe. Existing IDs are retained.
- **Extra ingredients:** select a recipe and add up to eight different ordinary
  stackable materials, with quantities. These are additional to the source-potion
  cost. Currency and permanent potions cannot become ingredients.
- **Permanent potions:** map a new finished item to its exact permanent effect.
  Create its inventory definition in Item Editor first: type 6/subtype 5, no ordinary timed skill (`a_num_0=0`, `a_num_1=0`),
  max use -1, Count/Exchange/Trade flags (Drop optional),
  and enough stack capacity for one output batch. Once saved, item and effect
  IDs cannot be retargeted or removed: older items and jobs retain their meaning.
  Up to 64 mappings are supported. Recipe outputs may change to another valid
  mapping without invalidating an existing job.
- **Permanent effects:** select the original item for a new definition. The skill
  and level are derived from that item and shown read-only. Once saved, the
  effect ID, item, skill and level are fixed; add another definition for a
  different effect. Retiring an offer never revokes an acquired effect.
- **Station:** edit its name, native model path, coordinates, heading and use
  range. Metres and degrees are displayed; storage remains integer hundredths.
  The current station belongs to private hall template 1, zone 8. The server
  checks map bounds; the editor cannot prove physical accessibility or that a
  newly named model is installed.

Construction is configured under **Infusions → Alchemy station** and
**Construction costs**. The station has separate **Construction enabled** and
**Brewing enabled** switches. The Artisan Rank requirement grants permission to
build once; it is not a continuing brewing requirement. Existing built ownership
is saved per character and is never revoked by edits to rank, costs or availability.
Turning off construction prevents new builds; turning off brewing pauses new
brews without deleting stations or paid jobs.

Edit the unbuilt plot name/model separately from the completed station. Both
use the configured private-hall placement. The default one-time cost is **1
Flawed Stone (156) + 1 Herb Trunk (198)**. The linked costs page supports up to
eight different ordinary stackable materials with item lookup and quantities.
Remove every cost for a free build. Gold, permanent potions, quest/expiring items,
duplicates and unsupported materials are rejected before Save. Cost changes affect
future builds only. Plot/station models need normal client asset deployment.

At most 16 effect definitions and 16 offers are supported, including retired
offers. Supported sources are enabled stackable type-6/subtype-5 potions with an
unconditional helpful self buff, a positive duration and no charges or special
lifecycle flags. Magic must be a stat addition/rate; the original Berserker
potion uses its existing special implementation. Skills and magic IDs cannot
overlap between permanent definitions. Exact skill levels must be 1–127 and at
or below the skill prototype's maximum. Save rechecks current item/skill/magic
metadata in its transaction. Skill definitions remain in the existing Skill and
Magic editors; this section does not copy or modify them.

Apply game migrations through **0043** and matching client/server first. Save, then reopen
the station to refresh offers. Placement/model edits may require re-entering the
private hall; new model/texture files need normal client asset deployment.

## Save, reload and version control

Saves affect these 29 **authored content** tables only:

- `t_crafting_settings`
- `t_crafting_recipe`, `t_crafting_ingredient`, `t_crafting_manual`, `t_crafting_bonus_output`
- `t_crafting_mastery`, `t_crafting_mastery_bonus`
- `t_crafting_master_rank`, `t_crafting_master_rank_bonus`
- `t_gathering_material`, `t_gathering_material_bonus`
- `t_gathering_settings`, `t_gathering_node`, `t_gathering_node_loot`, `t_gathering_tool`, `t_gathering_profile`
- `t_herb_population`, `t_herb_spawn_point`, `t_herb_loot`
- `t_fishing_settings`, `t_fishing_water`, `t_fishing_water_vertex`, `t_fishing_loot`
- `t_alchemy_effect`, `t_alchemy_consumable`, `t_alchemy_offer`, `t_alchemy_ingredient`, `t_alchemy_facility`, `t_alchemy_facility_ingredient`

The editor locks and checks the current catalogue against its loaded snapshot before writing. If another editor changed it, Save refuses the overwrite and retains the draft. Revert / Reload then reapply the intended changes. Current item definitions are checked again within the save transaction. Partial SQL failure rolls back the entire save. All 29 tables must be InnoDB; unreviewed custom triggers are refused.

For edits that use already deployed item definitions and artwork, reopen `/craft` for recipes, `/gather` for gathering resource mastery, or `/artisan` for Artisan Ranks. Ordinary catalogue tuning needs no rebuild or restart. Actual herb populations/sites/loot additionally require administrator `/herbpop reload`; changes to the enabled population ID/zone/NPC ownership set require GameServer restart. Static NPC placements and new prototypes follow their normal deployment path. The Overview apply guide explains these distinctions. New item prototypes still require the existing server item-loading and client `.lod` export/deployment workflow; new emblem textures require asset deployment.

After Save, **Export profession content** asks for the game Git checkout and runs its existing `x64-server/db/export-seed.sh` for exactly these 29 tables. This button supports the local Docker Compose database using the toolbox's root credentials and `ep4_data`. Git for Windows and Docker must be available. The exporter verifies that the Compose service is the same database the editor loaded, holds the catalogue stable while exporting, and stages files before replacing the seeds. Passwords are passed through environment variables, never command-line arguments. Other connection setups can still Save; use the normal exporter command outside the toolbox for those setups.

Review and commit the resulting `x64-server/db/seeds/ep4_data` diff. Export new or changed `t_item` content separately, and promote exported client data into the game's authored asset tree. This button never exports player XP, learned recipes, earned ranks, inventory or character stat points.

The Toolbox's **Record saved content in Git** button also includes all six
authored infusion tables. Character-owned `ep4_db.t_alchemy_infusion_unlock`,
`ep4_db.t_alchemy_work_order` jobs/receipts, and `ep4_db.t_alchemy_facility_unlock` built-station ownership are explicitly excluded, including their operation tokens and timestamps.

## Existing engine limits

The editor exposes authored data, not a new rules engine. Existing limits remain: 4,096 recipes, 64 ranks (both including retired entries), maximum supported skill 65,535, ten materials, eight teaching manuals, eight random outputs, eight distinct bonus effects per mastery tier (eight per Artisan Rank), and craft durations of 0.001–600 seconds. Supported bonuses include the game's flat effects 0–23 and HP/MP regeneration 102/103, plus the mastery-only effects below (requires game migration 0038 and matching client/server).

Every recipe, resource and Artisan Rank bonus grid uses readable amounts with an explicit Units column. Bulk recipe editing uses the same units. Flat bonuses remain whole points. Movement Speed accepts values such as **0.6 speed units**; Attack Interval Reduction accepts **6%** (a shorter interval between attacks, not a 6% increase in attacks per second); PvE Double Damage Chance accepts **4%**. The percentage effects and movement accept at most two decimal places. This chance is for normal attacks against NPCs; it does not add a PvP or skill-damage proc.

Tier rows are additional rewards: Bronze 0.15, Silver 0.15 and Gold 0.3 movement add up to **0.6 at Gold**. Recipe/resource summaries, Artisan Rank cumulative summaries and the overview show the combined amount in the same readable units. Earlier tiers and previously earned retired definitions remain part of the total.

Storage stays integer-based and is converted only at the editor boundary. Effects 1000/1001/1002 respectively store hundredths of a speed unit, interval-reduction basis points and chance basis points: **0.6→60**, **6%→600**, **4%→400**. These are mastery effects, not equipment option IDs. Individual amounts and combined totals across crafting, gathering and Artisan Ranks must stay within **327.67 movement units**, **90% interval reduction**, and **100% PvE double-damage chance**. Existing flat channels remain capped at 32,767, including overlapping All/Physical effects and retired rewards. Bonus grids reject excess precision, zero and values above the effect's limit; Save also validates the complete catalogue's combined totals.

The 100/60/20 chances and one stat point per crafting skill level are still engine rules. Changing those requires coordinated game code work.

## Verification

See [CraftingVerification](../tests/CraftingVerification/README.md) for the isolated MariaDB suite and offline WinForms previews. No live game client is launched by that suite.

For a later gameplay check, create a test recipe using deployed items, assign a supported manual, save, reopen `/craft`, use the manual, and verify the recipe/materials/timing. Craft through mastery and a rank threshold, then reopen the editor and confirm the saved values. This implementation was verified with database fixtures and offline UI rendering; no gameplay session was driven.

## Scope and safeguards

General item, NPC, shop and zone definitions remain in their existing editors,
linked from profession entries. NPC Editor supplies appearance and static placements; converted sources use profession loot tables here, rather than legacy NPC product fields. This editor does not edit character skill, XP,
inventory, learned recipes, earned Artisan ranks, live herb occupancy or active
sessions. Published parent IDs and mastery identities remain stable. Removed
child rows are reordered and validated before Save; deleted/retired source content
does not delete player progression.

Validation follows the running game's catalogue bounds, NPC flags, item types and
stack limits, cross-profession mastery links, bonus-channel totals and geometry
contracts. SQL accepting a value is not sufficient. Source geometry, actual terrain
and deployed model compatibility still require the appropriate world/client tools
and manual verification.
