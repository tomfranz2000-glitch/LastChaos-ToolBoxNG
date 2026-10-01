-- Make ToolBoxNG content saves transactional on the LastChaos development schema.
-- Canonical game deployment: copy this as a new immutable numbered migration.
-- Run once with the normal migration runner after backing up the database.
-- No player/account data, authored values, encodings, keys or numeric limits change.
--
-- Five legacy tables exceed InnoDB's 8126-byte local-row limit because many
-- single-byte VARCHAR(255) columns cannot use overflow pages. TEXT can overflow;
-- the CHECK constraints retain each original 255-character maximum. Character
-- sets, collations, nullability and defaults are retained explicitly.
-- Merely increasing VARCHAR to 256 passes DDL but cannot store densely filled
-- rows whose actual values remain <=255 bytes; it is not sufficient here.
--
-- Scope: the 48 tables writable by the registered editors or DBStringTranslator,
-- including item-delete dependencies. Existing InnoDB tables remain InnoDB.
-- The editor must use real transactions and reject nontransactional destinations.

CREATE DATABASE IF NOT EXISTS ep4_data;
USE ep4_data;
SET SESSION innodb_strict_mode = ON;
ALTER TABLE ep4_data.t_affinity ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_affinity_npc ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_affinity_reward_item ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_affinity_work ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_catalog ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_ct_item ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_drop_item_data ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_drop_item_head ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_factory_item ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_fortune_data ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_fortune_head ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_holy_water ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_item_collection ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_item_exchange ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_lacarette ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_luckydrawbox ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_luckydrawneed ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_luckydrawresult ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_magic ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_magiclevel ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_missioncase ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_monster_mercenary ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_moonstone_reward ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_npc_cube ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_npc_drop_all ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_npc_dropjob ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_npc_dropraid ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_npc_regen ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_npc_regen_combo ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_npc_regen_raid ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_option ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_pet_fairy_skill_whitelist ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_quest ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_reward_data ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_reward_head ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_set_item ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_shop ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_shopitem ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_skilllevel ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_special_skill ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_string ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_title ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;
ALTER TABLE ep4_data.t_zonedata ENGINE=InnoDB, ROW_FORMAT=DYNAMIC;

ALTER TABLE ep4_data.t_action
  MODIFY COLUMN a_name_thai_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_thai TEXT CHARACTER SET tis620 COLLATE tis620_thai_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_twn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_chn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_thai_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_thai TEXT CHARACTER SET tis620 COLLATE tis620_thai_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_ani1 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_ani2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_ani3 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_ani4 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_ani5 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_ani6 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_jpn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_mal TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_mal_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_usa TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_brz TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_hk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_hk_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_ger TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_spn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_frc TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_pld TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_rus TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_tur TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_spn2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_frc2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_ita TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_mex TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_nld TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_uk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  ADD CONSTRAINT IF NOT EXISTS ck_t_action_legacy_text_length CHECK (CHAR_LENGTH(a_name_thai_eng) <= 255 AND CHAR_LENGTH(a_name_thai) <= 255 AND CHAR_LENGTH(a_client_description) <= 255 AND CHAR_LENGTH(a_client_description_twn) <= 255 AND CHAR_LENGTH(a_client_description_chn) <= 255 AND CHAR_LENGTH(a_client_description_thai_eng) <= 255 AND CHAR_LENGTH(a_client_description_thai) <= 255 AND CHAR_LENGTH(a_client_ani1) <= 255 AND CHAR_LENGTH(a_client_ani2) <= 255 AND CHAR_LENGTH(a_client_ani3) <= 255 AND CHAR_LENGTH(a_client_ani4) <= 255 AND CHAR_LENGTH(a_client_ani5) <= 255 AND CHAR_LENGTH(a_client_ani6) <= 255 AND CHAR_LENGTH(a_client_description_jpn) <= 255 AND CHAR_LENGTH(a_client_description_mal) <= 255 AND CHAR_LENGTH(a_client_description_mal_eng) <= 255 AND CHAR_LENGTH(a_client_description_usa) <= 255 AND CHAR_LENGTH(a_client_description_brz) <= 255 AND CHAR_LENGTH(a_client_description_hk) <= 255 AND CHAR_LENGTH(a_client_description_hk_eng) <= 255 AND CHAR_LENGTH(a_client_description_ger) <= 255 AND CHAR_LENGTH(a_client_description_spn) <= 255 AND CHAR_LENGTH(a_client_description_frc) <= 255 AND CHAR_LENGTH(a_client_description_pld) <= 255 AND CHAR_LENGTH(a_client_description_rus) <= 255 AND CHAR_LENGTH(a_client_description_tur) <= 255 AND CHAR_LENGTH(a_client_description_spn2) <= 255 AND CHAR_LENGTH(a_client_description_frc2) <= 255 AND CHAR_LENGTH(a_client_description_ita) <= 255 AND CHAR_LENGTH(a_client_description_mex) <= 255 AND CHAR_LENGTH(a_client_description_nld) <= 255 AND CHAR_LENGTH(a_client_description_uk) <= 255),
  ENGINE=InnoDB,
  ROW_FORMAT=DYNAMIC;

ALTER TABLE ep4_data.t_item
  MODIFY COLUMN a_descr TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_twn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_chn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_file_smc TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_thai TEXT CHARACTER SET tis620 COLLATE tis620_thai_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_thai_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_twn2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_jpn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_mal TEXT CHARACTER SET latin1 COLLATE latin1_bin NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_mal_eng TEXT CHARACTER SET latin1 COLLATE latin1_bin NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_usa TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_brz TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_hk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_hk_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_ger TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_effect_name TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NULL DEFAULT NULL,
  MODIFY COLUMN a_attack_effect_name TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NULL DEFAULT NULL,
  MODIFY COLUMN a_damage_effect_name TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NULL DEFAULT NULL,
  MODIFY COLUMN a_descr_spn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_frc TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_pld TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_rus TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_tur TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_spn2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_frc2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_ita TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_mex TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_nld TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_uk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_uk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_dev TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  ADD CONSTRAINT IF NOT EXISTS ck_t_item_legacy_text_length CHECK (CHAR_LENGTH(a_descr) <= 255 AND CHAR_LENGTH(a_descr_twn) <= 255 AND CHAR_LENGTH(a_descr_chn) <= 255 AND CHAR_LENGTH(a_file_smc) <= 255 AND CHAR_LENGTH(a_descr_thai) <= 255 AND CHAR_LENGTH(a_descr_thai_eng) <= 255 AND CHAR_LENGTH(a_descr_twn2) <= 255 AND CHAR_LENGTH(a_descr_jpn) <= 255 AND CHAR_LENGTH(a_descr_mal) <= 255 AND CHAR_LENGTH(a_descr_mal_eng) <= 255 AND CHAR_LENGTH(a_descr_usa) <= 255 AND CHAR_LENGTH(a_descr_brz) <= 255 AND CHAR_LENGTH(a_descr_hk) <= 255 AND CHAR_LENGTH(a_descr_hk_eng) <= 255 AND CHAR_LENGTH(a_descr_ger) <= 255 AND CHAR_LENGTH(a_effect_name) <= 255 AND CHAR_LENGTH(a_attack_effect_name) <= 255 AND CHAR_LENGTH(a_damage_effect_name) <= 255 AND CHAR_LENGTH(a_descr_spn) <= 255 AND CHAR_LENGTH(a_descr_frc) <= 255 AND CHAR_LENGTH(a_descr_pld) <= 255 AND CHAR_LENGTH(a_descr_rus) <= 255 AND CHAR_LENGTH(a_descr_tur) <= 255 AND CHAR_LENGTH(a_descr_spn2) <= 255 AND CHAR_LENGTH(a_descr_frc2) <= 255 AND CHAR_LENGTH(a_descr_ita) <= 255 AND CHAR_LENGTH(a_descr_mex) <= 255 AND CHAR_LENGTH(a_descr_nld) <= 255 AND CHAR_LENGTH(a_name_uk) <= 255 AND CHAR_LENGTH(a_descr_uk) <= 255 AND CHAR_LENGTH(a_descr_dev) <= 255),
  ENGINE=InnoDB,
  ROW_FORMAT=DYNAMIC;

ALTER TABLE ep4_data.t_npc
  MODIFY COLUMN a_descr TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_twn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_chn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_long TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_skill0 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT ' -1',
  MODIFY COLUMN a_skill1 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT ' -1',
  MODIFY COLUMN a_skill2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT ' -1',
  MODIFY COLUMN a_skill3 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT ' -1',
  MODIFY COLUMN a_file_smc TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_fireEffect0 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_fireEffect1 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_fireEffect2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_thai TEXT CHARACTER SET tis620 COLLATE tis620_thai_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_thai_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_jpn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_mal TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_mal_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_usa TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_brz TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_hk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_ger TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_spn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_frc TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_pld TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_rus TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_tur TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_spn2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_frc2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_ita TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_mex TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_nld TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_uk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_descr_dev TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  ADD CONSTRAINT IF NOT EXISTS ck_t_npc_legacy_text_length CHECK (CHAR_LENGTH(a_descr) <= 255 AND CHAR_LENGTH(a_descr_twn) <= 255 AND CHAR_LENGTH(a_descr_chn) <= 255 AND CHAR_LENGTH(a_descr_long) <= 255 AND CHAR_LENGTH(a_skill0) <= 255 AND CHAR_LENGTH(a_skill1) <= 255 AND CHAR_LENGTH(a_skill2) <= 255 AND CHAR_LENGTH(a_skill3) <= 255 AND CHAR_LENGTH(a_file_smc) <= 255 AND CHAR_LENGTH(a_fireEffect0) <= 255 AND CHAR_LENGTH(a_fireEffect1) <= 255 AND CHAR_LENGTH(a_fireEffect2) <= 255 AND CHAR_LENGTH(a_descr_thai) <= 255 AND CHAR_LENGTH(a_descr_thai_eng) <= 255 AND CHAR_LENGTH(a_descr_jpn) <= 255 AND CHAR_LENGTH(a_descr_mal) <= 255 AND CHAR_LENGTH(a_descr_mal_eng) <= 255 AND CHAR_LENGTH(a_descr_usa) <= 255 AND CHAR_LENGTH(a_descr_brz) <= 255 AND CHAR_LENGTH(a_descr_hk) <= 255 AND CHAR_LENGTH(a_descr_ger) <= 255 AND CHAR_LENGTH(a_descr_spn) <= 255 AND CHAR_LENGTH(a_descr_frc) <= 255 AND CHAR_LENGTH(a_descr_pld) <= 255 AND CHAR_LENGTH(a_descr_rus) <= 255 AND CHAR_LENGTH(a_descr_tur) <= 255 AND CHAR_LENGTH(a_descr_spn2) <= 255 AND CHAR_LENGTH(a_descr_frc2) <= 255 AND CHAR_LENGTH(a_descr_ita) <= 255 AND CHAR_LENGTH(a_descr_mex) <= 255 AND CHAR_LENGTH(a_descr_nld) <= 255 AND CHAR_LENGTH(a_descr_uk) <= 255 AND CHAR_LENGTH(a_descr_dev) <= 255),
  ENGINE=InnoDB,
  ROW_FORMAT=DYNAMIC;

ALTER TABLE ep4_data.t_rareoption
  MODIFY COLUMN a_name TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_twn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_thai TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_thai_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_jpn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_mal TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_mal_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_usa TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_brz TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_hk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_hk_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_ger TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_twn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_thai TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_thai_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_jpn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_mal TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_mal_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_usa TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_brz TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_hk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_hk_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_ger TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_spn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_spn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_frc TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_frc TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_pld TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_pld TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_rus TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_rus TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_tur TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_tur TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_spn2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_spn2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_frc2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_frc2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_ita TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_ita TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_mex TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_mex TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_nld TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_nld TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_prefix_uk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_name_uk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  ADD CONSTRAINT IF NOT EXISTS ck_t_rareoption_legacy_text_length CHECK (CHAR_LENGTH(a_name) <= 255 AND CHAR_LENGTH(a_prefix) <= 255 AND CHAR_LENGTH(a_prefix_twn) <= 255 AND CHAR_LENGTH(a_prefix_thai) <= 255 AND CHAR_LENGTH(a_prefix_thai_eng) <= 255 AND CHAR_LENGTH(a_prefix_jpn) <= 255 AND CHAR_LENGTH(a_prefix_mal) <= 255 AND CHAR_LENGTH(a_prefix_mal_eng) <= 255 AND CHAR_LENGTH(a_prefix_usa) <= 255 AND CHAR_LENGTH(a_prefix_brz) <= 255 AND CHAR_LENGTH(a_prefix_hk) <= 255 AND CHAR_LENGTH(a_prefix_hk_eng) <= 255 AND CHAR_LENGTH(a_prefix_ger) <= 255 AND CHAR_LENGTH(a_name_twn) <= 255 AND CHAR_LENGTH(a_name_thai) <= 255 AND CHAR_LENGTH(a_name_thai_eng) <= 255 AND CHAR_LENGTH(a_name_jpn) <= 255 AND CHAR_LENGTH(a_name_mal) <= 255 AND CHAR_LENGTH(a_name_mal_eng) <= 255 AND CHAR_LENGTH(a_name_usa) <= 255 AND CHAR_LENGTH(a_name_brz) <= 255 AND CHAR_LENGTH(a_name_hk) <= 255 AND CHAR_LENGTH(a_name_hk_eng) <= 255 AND CHAR_LENGTH(a_name_ger) <= 255 AND CHAR_LENGTH(a_prefix_spn) <= 255 AND CHAR_LENGTH(a_name_spn) <= 255 AND CHAR_LENGTH(a_prefix_frc) <= 255 AND CHAR_LENGTH(a_name_frc) <= 255 AND CHAR_LENGTH(a_prefix_pld) <= 255 AND CHAR_LENGTH(a_name_pld) <= 255 AND CHAR_LENGTH(a_prefix_rus) <= 255 AND CHAR_LENGTH(a_name_rus) <= 255 AND CHAR_LENGTH(a_prefix_tur) <= 255 AND CHAR_LENGTH(a_name_tur) <= 255 AND CHAR_LENGTH(a_prefix_spn2) <= 255 AND CHAR_LENGTH(a_name_spn2) <= 255 AND CHAR_LENGTH(a_prefix_frc2) <= 255 AND CHAR_LENGTH(a_name_frc2) <= 255 AND CHAR_LENGTH(a_prefix_ita) <= 255 AND CHAR_LENGTH(a_name_ita) <= 255 AND CHAR_LENGTH(a_prefix_mex) <= 255 AND CHAR_LENGTH(a_name_mex) <= 255 AND CHAR_LENGTH(a_prefix_nld) <= 255 AND CHAR_LENGTH(a_name_nld) <= 255 AND CHAR_LENGTH(a_prefix_uk) <= 255 AND CHAR_LENGTH(a_name_uk) <= 255),
  ENGINE=InnoDB,
  ROW_FORMAT=DYNAMIC;

ALTER TABLE ep4_data.t_skill
  MODIFY COLUMN a_cd_ra TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_re TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_sa TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_fa TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_fe0 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_fe1 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_fe2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_fe_after TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_fe_after2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_twn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_chn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_twn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_chn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_ra2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_re2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_sa2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_fa2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_fe3 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_fe4 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_cd_fe5 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_thai TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_thai TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_thai_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_thai_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_jpn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_jpn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_mal TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_mal TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_mal_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_mal_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_usa TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_usa TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_brz TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_brz TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_hk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_hk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_hk_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_hk_eng TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_ger TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_ger TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_spn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_spn TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_frc TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_frc TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_pld TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_pld TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_rus TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_rus TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_tur TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_tur TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_spn2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_spn2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_frc2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_frc2 TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_ita TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_ita TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_mex TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_mex TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_nld TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_nld TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_uk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_uk TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_description_dev TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  MODIFY COLUMN a_client_tooltip_dev TEXT CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL DEFAULT '',
  ADD CONSTRAINT IF NOT EXISTS ck_t_skill_legacy_text_length CHECK (CHAR_LENGTH(a_cd_ra) <= 255 AND CHAR_LENGTH(a_cd_re) <= 255 AND CHAR_LENGTH(a_cd_sa) <= 255 AND CHAR_LENGTH(a_cd_fa) <= 255 AND CHAR_LENGTH(a_cd_fe0) <= 255 AND CHAR_LENGTH(a_cd_fe1) <= 255 AND CHAR_LENGTH(a_cd_fe2) <= 255 AND CHAR_LENGTH(a_cd_fe_after) <= 255 AND CHAR_LENGTH(a_cd_fe_after2) <= 255 AND CHAR_LENGTH(a_client_description) <= 255 AND CHAR_LENGTH(a_client_description_twn) <= 255 AND CHAR_LENGTH(a_client_description_chn) <= 255 AND CHAR_LENGTH(a_client_tooltip) <= 255 AND CHAR_LENGTH(a_client_tooltip_twn) <= 255 AND CHAR_LENGTH(a_client_tooltip_chn) <= 255 AND CHAR_LENGTH(a_cd_ra2) <= 255 AND CHAR_LENGTH(a_cd_re2) <= 255 AND CHAR_LENGTH(a_cd_sa2) <= 255 AND CHAR_LENGTH(a_cd_fa2) <= 255 AND CHAR_LENGTH(a_cd_fe3) <= 255 AND CHAR_LENGTH(a_cd_fe4) <= 255 AND CHAR_LENGTH(a_cd_fe5) <= 255 AND CHAR_LENGTH(a_client_description_thai) <= 255 AND CHAR_LENGTH(a_client_tooltip_thai) <= 255 AND CHAR_LENGTH(a_client_description_thai_eng) <= 255 AND CHAR_LENGTH(a_client_tooltip_thai_eng) <= 255 AND CHAR_LENGTH(a_client_description_jpn) <= 255 AND CHAR_LENGTH(a_client_tooltip_jpn) <= 255 AND CHAR_LENGTH(a_client_description_mal) <= 255 AND CHAR_LENGTH(a_client_tooltip_mal) <= 255 AND CHAR_LENGTH(a_client_description_mal_eng) <= 255 AND CHAR_LENGTH(a_client_tooltip_mal_eng) <= 255 AND CHAR_LENGTH(a_client_description_usa) <= 255 AND CHAR_LENGTH(a_client_tooltip_usa) <= 255 AND CHAR_LENGTH(a_client_description_brz) <= 255 AND CHAR_LENGTH(a_client_tooltip_brz) <= 255 AND CHAR_LENGTH(a_client_description_hk) <= 255 AND CHAR_LENGTH(a_client_tooltip_hk) <= 255 AND CHAR_LENGTH(a_client_description_hk_eng) <= 255 AND CHAR_LENGTH(a_client_tooltip_hk_eng) <= 255 AND CHAR_LENGTH(a_client_description_ger) <= 255 AND CHAR_LENGTH(a_client_tooltip_ger) <= 255 AND CHAR_LENGTH(a_client_description_spn) <= 255 AND CHAR_LENGTH(a_client_tooltip_spn) <= 255 AND CHAR_LENGTH(a_client_description_frc) <= 255 AND CHAR_LENGTH(a_client_tooltip_frc) <= 255 AND CHAR_LENGTH(a_client_description_pld) <= 255 AND CHAR_LENGTH(a_client_tooltip_pld) <= 255 AND CHAR_LENGTH(a_client_description_rus) <= 255 AND CHAR_LENGTH(a_client_tooltip_rus) <= 255 AND CHAR_LENGTH(a_client_description_tur) <= 255 AND CHAR_LENGTH(a_client_tooltip_tur) <= 255 AND CHAR_LENGTH(a_client_description_spn2) <= 255 AND CHAR_LENGTH(a_client_tooltip_spn2) <= 255 AND CHAR_LENGTH(a_client_description_frc2) <= 255 AND CHAR_LENGTH(a_client_tooltip_frc2) <= 255 AND CHAR_LENGTH(a_client_description_ita) <= 255 AND CHAR_LENGTH(a_client_tooltip_ita) <= 255 AND CHAR_LENGTH(a_client_description_mex) <= 255 AND CHAR_LENGTH(a_client_tooltip_mex) <= 255 AND CHAR_LENGTH(a_client_description_nld) <= 255 AND CHAR_LENGTH(a_client_tooltip_nld) <= 255 AND CHAR_LENGTH(a_client_description_uk) <= 255 AND CHAR_LENGTH(a_client_tooltip_uk) <= 255 AND CHAR_LENGTH(a_client_description_dev) <= 255 AND CHAR_LENGTH(a_client_tooltip_dev) <= 255),
  ENGINE=InnoDB,
  ROW_FORMAT=DYNAMIC;
