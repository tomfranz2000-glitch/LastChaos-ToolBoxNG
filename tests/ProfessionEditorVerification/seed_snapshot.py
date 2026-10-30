"""Read tracked content into an offline GUI fixture; no database connection."""
import json
from pathlib import Path
import sys

game = Path(sys.argv[1]).resolve()
sys.path.insert(0, str(game / 'tools/gathering'))
from HerbSpawnPoints import sql_rows

seed = game / 'x64-server/db/seeds/ep4_data'
def decode(value):
    if value.startswith("'") and value.endswith("'"):
        text = value[1:-1]
        result, index = '', 0
        escapes = {'0': '\0', 'n': '\n', 'r': '\r', 't': '\t', 'b': '\b', 'Z': '\x1a'}
        while index < len(text):
            if text[index] == '\\' and index + 1 < len(text):
                index += 1
                result += escapes.get(text[index], text[index])
            else:
                result += text[index]
            index += 1
        return result
    return value

tables = {}
for path in seed.glob('*.sql'):
    if path.stem.startswith(('t_crafting_', 't_gathering_', 't_herb_', 't_fishing_', 't_alchemy_')):
        tables[path.stem] = [{key: decode(value) for key, value in row.items()} for row in sql_rows(path)]
refs = set()
for rows in tables.values():
    for row in rows:
        for column in ('a_item_index', 'a_output_item', 'a_output_item_index', 'a_manual_item', 'a_rod_item_index', 'a_visual_tool_index'):
            if column in row: refs.add(int(row[column]))
items = []
for raw in sql_rows(seed / 't_item.sql'):
    if int(raw['a_index']) not in refs: continue
    row = {key: decode(value) for key, value in raw.items()}
    names = ('a_index', 'a_name_usa', 'a_enable', 'a_type_idx', 'a_subtype_idx', 'a_flag', 'a_weight', 'a_texture_id', 'a_texture_row', 'a_texture_col', 'a_wearing')
    items.append({key: row[key] for key in names})
npc_refs = {int(row['a_npc_index']) for rows in tables.values() for row in rows if 'a_npc_index' in row}
npcs = []
for raw in sql_rows(seed / 't_npc.sql'):
    if int(raw['a_index']) not in npc_refs: continue
    npcs.append({key: decode(raw[key]) for key in ('a_index', 'a_name', 'a_flag', 'a_enable')})
zones = [{key: decode(row[key]) for key in ('a_zone_index', 'a_name')} for row in sql_rows(seed / 't_zonedata.sql')]
metadata = {table: [{key: decode(value) for key, value in row.items()} for row in sql_rows(seed / (table + '.sql'))] for table in ('t_item','t_skill','t_skilllevel','t_magic','t_magiclevel')}
Path(sys.argv[2]).write_text(json.dumps({'Tables': tables, 'Items': items, 'Npcs': npcs, 'Zones': zones, 'InfusionMetadata': metadata}, ensure_ascii=False), encoding='utf-8')
