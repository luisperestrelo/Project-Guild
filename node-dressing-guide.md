# Node Dressing Guide

Reference guide for dressing node scenes with Synty assets.

## Synty Packs Available

| Pack | Key Use |
|---|---|
| AnimationBaseLocomotion | Runner animations (idle, walk, run) |
| PolygonAdventure | Villages, outdoor props, characters |
| PolygonDungeon | Dungeon interiors, torches, chests, skeletons, mine infrastructure |
| PolygonDungeonMap | Library/archive rooms, books |
| PolygonDungeonRealms | Ore piles, ore inserts, anvils, forges, dwarf miner, camp tents, waterfalls |
| PolygonExplorers | Explorer characters, backpacks, compasses, maps |
| PolygonFantasyCharacters | Fantasy NPCs (druid, wizard, peasant, etc.) |
| PolygonFantasyKingdom | LARGEST PACK - buildings, castle, market stalls, ore props, fish, flowers, planters |
| PolygonGeneric | Utility props, barrels, crates, pipes, scaffolding |
| PolygonGoblinWarCamp | Mine entrance/tower/wheel, mine tracks, minecarts, goblin camp props, log walls |
| PolygonNature | Trees, rocks, bushes, ferns, mushrooms, flowers, lily pads, reeds, cave entrances, terrain textures |
| PolygonVikings | Docks, fishing boats, fish racks, fish props, longhouses, Viking buildings |

## Terrain Setup Per Node

1. Delete the plane
2. GameObject > 3D Object > Terrain
3. Position at (-25, 0, -25) for a centered 50x50 terrain (or adjust size per node)
4. Terrain Settings (gear icon): Width=50, Length=50, Height=20
5. Paint Texture: create terrain layers from PolygonNature/Textures/Ground_Textures/
6. Paint height for gentle hills, raised edges
7. Click "Snap All Points to Terrain" on NodeSceneRoot in Inspector
8. Re-bake NavMesh

### Terrain Layers (create once, reuse across scenes)

Save in Assets/Art/TerrainLayers/:
- TL_Grass (from Texture_01_A or BaseGrass)
- TL_Dirt (from Mud texture)
- TL_Rock (from Rockwall or Cliffwall)
- TL_Moss (from Moss)
- TL_Sand (from Sand)
- TL_Pebbles (from Pebbles)

Set normal maps on each layer where available (files ending in _normals or _Normal).

### Texture Combos Per Node

| Node | Base Layer | Detail Layers |
|---|---|---|
| PineForest | Grass | Dirt, Moss, Rock |
| CopperMine | Rock/Cliffwall | Dirt, sparse Grass |
| DeepMine | Rock/Cliffwall | Dirt (darker, more barren) |
| GoblinCamp | Dirt/Mud | Grass (patchy), Rock |
| LakesideGrove | Grass | Dirt, Sand (near water), Moss |
| GuildHall | Grass | Dirt (paths), Rock (foundations) |
| HerbGarden | Grass | Flowers, Dirt (garden beds), Moss |
| SunlitPond | Grass | Sand/Pebbles (shoreline), Dirt, Mud |

## Per-Node Dressing

### Node_PineForest
**Pack: PolygonNature** (Assets/Art/Synty/PolygonNature/Prefabs/)

At gathering spots:
- SM_Env_Tree_Pine_01, SM_Env_Tree_Pine_02 - one at/near each spot
- SM_Env_Tree_Stump_01 through _04 - stumps near spots

Perimeter decoration:
- SM_Env_Tree_Pine_Large_01, _02 - big trees around edges
- SM_Env_Tree_Pine_Small_01, _02 - fill gaps
- SM_Env_Tree_Birch_01 through _04 - mix in a few
- SM_Env_Bush_01
- SM_Env_Fern_01, SM_Env_Fern_Leaves_01
- SM_Env_Rock_01 through _04
- SM_Env_Mushroom_*
- SM_Env_Tree_Log_01
- SM_Prop_CampFire

### Node_CopperMine
**Packs: PolygonGoblinWarCamp + PolygonFantasyKingdom + PolygonDungeon + PolygonNature**

Focal point:
- SM_Bld_Mine_Entrance_01 - mine entrance building
- SM_Bld_Mine_Tower_01 - mine tower with wheel
- SM_Bld_Mine_Wheel_01

At gathering spots (ore veins):
- From PolygonFantasyKingdom: SM_Prop_Ore_Iron_01, SM_Prop_Ore_Gold_01, SM_Prop_Ore_Gem_01
- From PolygonDungeonRealms: SM_Env_Ore_Pile_01, _02, SM_Prop_Ore_Insert_Large_01

Mine infrastructure:
- SM_Env_Mine_Track_Straight_01, _02
- SM_Env_Mine_Track_Corner_01
- SM_Veh_Minecart_01
- SM_Env_Mine_Track_Ending_Block_01

Props:
- From PolygonDungeon: SM_Prop_Torch_01, SM_Prop_Torch_Ornate_01
- Barrels and crates from any pack
- From PolygonNature: sparse rocks, minimal vegetation

### Node_DeepMine
Same as CopperMine but darker/heavier. More mine tracks, ore piles, support beams.
- From PolygonDungeon: SM_Env_Reinforced_Pole_01, _02 - wooden supports
- More minecarts, more ore, fewer plants, more rock

### Node_GoblinCamp
**Pack: PolygonGoblinWarCamp**
- SM_Bld_Tent_* or SM_Prop_Tent_* - tents
- SM_Prop_CampFire_* - central campfire
- SM_Bld_LogWall_01 through _06 - crude palisade
- SM_Bld_Gate_* - camp entrance
- SM_Bld_HeadQuarters_* - main building
- SM_Bld_Barracks_*
- Weapon racks, crates, barrels
- From PolygonNature: trees and rocks outside

### Node_LakesideGrove (3 gatherables: pine, trout, sage)
**Packs: PolygonNature + PolygonVikings**

Zone the gathering types:
- Woodcutting area: Pine trees at spots, stumps, logs
- Fishing area: From PolygonVikings: SM_Prop_Dock_01, SM_Prop_Fish_Rack_01, SM_Prop_Fish_Pile_01. Water plane from PolygonNature or PolygonGeneric (SM_Generic_Water_Plane_*)
- Foraging area: Bushes, ferns, flowers from PolygonNature

### Node_GuildHall
**Packs: PolygonVikings + PolygonFantasyKingdom + PolygonAdventure**
- From PolygonVikings: SM_Bld_* longhouse buildings
- From PolygonFantasyKingdom: Market stalls (SM_Bld_Stall_*), fences, roads
- From PolygonAdventure: SM_Bld_Village_*, SM_Bld_Well_01
- Barrels, crates, banners around bank
- Campfire in central area
- Fences/paths between buildings
- From PolygonNature: trees, bushes around edges

### Node_HerbGarden
**Packs: PolygonNature + PolygonFantasyKingdom**
- From PolygonFantasyKingdom: SM_Env_Planter_* at gathering spots
- SM_Env_Flowers_*
- From PolygonNature: SM_Env_Bush_01, SM_Env_Flower_Patch_01, SM_Env_Purple_Flower_01
- SM_Env_Mushroom_*
- SM_Prop_Fence_01, _02 - garden fencing
- SM_Prop_Pillar_*, SM_Prop_Pillar_Arch_* - broken garden arches
- Scattered rocks, path

### Node_SunlitPond
**Packs: PolygonNature + PolygonVikings**
- Water surface at center
- From PolygonVikings: SM_Prop_Dock_01, _02, _03 at fishing spots
- SM_Prop_Dock_Pole_01, _02
- SM_Prop_Fish_Rack_01, _02
- From PolygonNature: SM_Env_Lily_Pad_Large_01 through _03, SM_Env_Lily_Pad_Small_01
- SM_Env_Reeds_01, _02
- SM_Env_Tree_Willow_Large, _Medium, _Small
- Rocks along shore
- SM_Prop_CampFire

## General Tips
- Don't be symmetrical. Cluster things (3-5 trees together, rock next to a log, ferns around tree bases)
- After placing props, re-bake NavMesh
- Use "Snap All Points to Terrain" button after terrain changes
- Brush opacity 0.3-0.5 for texture painting (full opacity looks bad)
- Keep node centers relatively flat for gameplay
- Raise edges 3-5m for enclosure feel, paint rock texture on edges, dense tree ring on top
- For caves/mines: raise edges HIGH (8-10m), steep slopes, use dark rock textures, add point lights for torchlight
