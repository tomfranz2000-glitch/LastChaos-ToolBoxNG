namespace LastChaos_ToolBoxNG.ContentRecording;

public sealed record ContentTable(string CanonicalSchema, string Table)
{
    public string Key => CanonicalSchema + "/" + Table;
    public string SeedRelativePath => "x64-server/db/seeds/" + Key + ".sql";
}

/// <summary>Reviewed authored content, never discovered by dumping a database.
/// Additions require classification review against the game CLASSIFICATION.md.
/// Same-named character progression in ep4_db is deliberately excluded.</summary>
public static class ContentRecordingPolicy
{
    // Existing optional Toolbox variants can write these schema-only/custom
    // sources. They need explicit classification, seeds and a policy update
    // before they can be recorded; absence or empty tables are harmless.
    public static IReadOnlyList<ContentTable> UnseededEditorTables { get; } = new[] {
        "t_item_exchange", "t_equipment_exchange", "t_daily_reward_item",
        "t_key", "t_lacaball", "t_oxreward"
    }.Select(name => new ContentTable("ep4_data", name)).ToArray();
    private const string DataTables = """
t_achievement
t_achievement_stage
t_action
t_affinity
t_affinity_npc
t_affinity_reward_item
t_affinity_work
t_attack_pet
t_attack_pet_ev
t_attack_pet_exp
t_autobuffs
t_bigpet
t_catalog
t_channel_setting
t_clientversion
t_crafting_bonus_output
t_crafting_ingredient
t_crafting_manual
t_crafting_master_rank
t_crafting_master_rank_bonus
t_crafting_mastery
t_crafting_mastery_bonus
t_crafting_recipe
t_crafting_settings
t_ct_item
t_ct_item_1
t_custom_mercenary
t_drop_item_data
t_drop_item_head
t_drop_prob
t_equipment_collection_bonus
t_equipment_collection_group
t_equipment_collection_layout
t_event
t_event_calendar
t_eventsettings
t_exp
t_exp_speed
t_factory_item
t_fishing_loot
t_fishing_settings
t_fishing_water
t_fishing_water_vertex
t_fortune_data
t_fortune_head
t_gathering_material
t_gathering_material_bonus
t_gathering_node
t_gathering_node_loot
t_gathering_profile
t_gathering_settings
t_gathering_tool
t_hashes
t_help1
t_herb_loot
t_herb_population
t_herb_spawn_point
t_holy_water
t_item
t_item_collection
t_jewel_data
t_jewel_dust_exchange
t_lacarette
t_luckydrawbox
t_luckydrawneed
t_luckydrawresult
t_magic
t_magiclevel
t_mapinfo
t_missioncase
t_monster_mercenary
t_monster_mercenary_ex
t_moonstone_reward
t_npc
t_npc_cube
t_npc_drop_all
t_npc_dropjob
t_npc_dropraid
t_npc_regen
t_npc_regen_combo
t_npc_regen_cube
t_npc_regen_raid
t_option
t_oxquiz
t_pet_fairy_skill_whitelist
t_promoitems
t_quest
t_raid_data
t_raid_object_data
t_raid_object_list
t_rareoption
t_regdate
t_requital_setup
t_reward_data
t_reward_head
t_royalrumble
t_set_item
t_shop
t_shopitem
t_skill
t_skilllevel
t_special_skill
t_speed_zone
t_startitems
t_string
t_title
t_toolversion
t_trigger_set
t_zonedata
""";
    public static IReadOnlyList<ContentTable> AllowlistedTables { get; } =
        DataTables.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Select(name => new ContentTable("ep4_data", name))
        .Concat(new[] {
            new ContentTable("ep4_db", "t_cashitemdate"),
            new ContentTable("ep4_db", "t_castle"),
            new ContentTable("ep4_db", "t_lcball"),
            new ContentTable("ep4_db", "t_start_item"),
            new ContentTable("ep4_db", "t_syndicate_jewel_point"),
            new ContentTable("ep4_db_auth", "t_ct_limit"),
            new ContentTable("ep4_post", "msgtype"),
            new ContentTable("ep4_post", "premiumtype"),
            new ContentTable("ep4_post", "preslot")
        }).OrderBy(table => table.Key, StringComparer.Ordinal).ToArray();

    public static void ValidateCheckout(string checkout)
    {
        var approved = AllowlistedTables.Select(t => t.SeedRelativePath).ToHashSet(StringComparer.Ordinal);
        string seeds = Path.Combine(checkout, "x64-server", "db", "seeds");
        if (!Directory.Exists(seeds)) throw new InvalidOperationException("Select the game checkout containing x64-server/db/seeds.");
        var actual = Directory.EnumerateFiles(seeds, "*.sql", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(checkout, path).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        string[] missing = approved.Except(actual).ToArray();
        string[] unknown = actual.Except(approved).ToArray();
        if (missing.Length != 0 || unknown.Length != 0)
            throw new InvalidOperationException("The checked-out content catalog differs from this Toolbox's reviewed recording policy. Update the Toolbox policy before recording. Missing: " + string.Join(", ", missing) + "; unreviewed: " + string.Join(", ", unknown));
    }
}
