# Tutorial / First-Hour Flow — Design Draft

Status: Early draft, not set in stone. Reference for later when we design the tutorial.

## Key Principles
- NOT a separate mode — just loads at start of Fresh Run, continues from there
- Very short, teaches minimal. Players learn by discovering. An "acceleration lane."
- Experienced players can turn it off
- Teaches 3 core verbs: **assign** (send runners places), **equip** (craft/gear up), **coach** (observe, diagnose, adjust rules)

## Starting State
- 3 fixed pawns (same for every player, variance comes from later pawns)
- Hand-tuned stats, no gear, classical archetypes:
  - Tank: 5 Melee, 7 Defence (passion in one or both)
  - Mage: 6 Mage, 6 Hitpoints (passion in one or both)
  - Healer: 5 Restoration, 7 Execution (passion in one or both)
  - Gathering skills: whatever we decide, not critical
- Each has a basic spammable ability (auto-attack, fireball, filler heal)
- Hub has pre-built Engineering Station (base building is minor, don't imply otherwise early)

## Flow

### Phase 1: Gathering
- Tutorial: "These are your pawns. You need equipment for the battles ahead. Some Copper Ore should help."
- Player sends runners to gather (Copper Mine, Pine Forest, etc.)
- Default automation handles deposit-and-return loop invisibly
- Player watches resources flow in, skills level up, low-key satisfying
- Feedback/notifications/celebrations on level-ups

### Phase 2: Crafting
- Tutorial: "You need equipment. Here's how to craft basic gear."
- Player crafts basic sword, some armor at the Engineering Station
- NO complex crafting automation shown yet (batch orders, etc.) — hidden or simply not introduced
- Just "send a guy to craft some stuff"

### Phase 3: Combat Introduction (Overworld Mobs)
- Tutorial: "Goblins have set up camp nearby. Go fight them!"
- Goblin Camp node appears on map
- Player sends runners (probably all 3, tutorial nudges toward this)
- Pawns work together (not 1v1 per mob) — overworld farming is group content
- Player watches combat, low-key satisfying (fireballs, healing, tanking)
- Items drop (few), gold drops, combat skill XP flows
- Healer heals allies, doesn't need offensive skills yet (but Restoration will have some later)

### Phase 4: The "Culling Frost Moment" — Automation Introduction
- A pawn unlocks a new ability (e.g., Mage at level 8 gets Culling Frost)
- Culling Frost: normally weaker than Fireball, but against enemies <35% HP, significantly stronger
- This creates INTRINSIC motivation to open the automation system
- Tutorial shows: open runner's automation/behavior tab, see Fireball on autocast, add rule: "If enemy HP < 35%, cast Culling Frost" with higher priority
- Player sees the mage actually switch spells mid-fight — immediate, visible, satisfying feedback
- DESIGN NOTE: Every runner's first unlocked ability should be "obviously needs a conditional rule" — even if the player only sent the healer to goblins, their first new ability should work this way

### Phase 5: First Raid (Intentional Wipe)
- Tutorial: "A goblin muttered about a Laboratory nearby, full of treasure..."
- Raid node revealed nearby, max 3 pawns
- Player gears up, enters raid
- Boss does big AoE at 50% HP + nasty DoT — designed to guarantee a wipe
  - Can hardcode % HP damage for this specific encounter if needed to ensure wipe
- Let player observe the arena after wipe (don't immediately kick them out)
- Pawns respawn at Hub (simplified for tutorial — normally pack animal system)

### Phase 6: The Coaching Loop — Prerecording Walkthrough
- Tutorial: "Your job is to observe, analyse, and plan your runners' behavior so they can succeed. Here's how."
- Show the fight again in a prerecording/replay format
- Prerecording demonstrates:
  1. The wipe happening (boss at ~50% HP, big AoE, everyone dies)
  2. Logbook being written: "Wiped to big AoE at 2:17. Boss was exactly 50% HP — that's the trigger."
  3. Opening healer's automation rules
  4. Adding phase-based rules: stop CoH autocast at ~55% HP, cast Barrier at ~52%, resume CoH at ~49%
- Implementation of "prerecording" TBD — could be actual replay, could be guided walkthrough, static walkthrough, advisor overlay, etc.
- Player regains control, likely copies the shown approach, succeeds on retry
- Teaches the CORE GAMEPLAY LOOP: observe failure → diagnose cause → adjust rules → succeed

### Phase 7: Tutorial Reward & Freedom
- Player beats the raid, gets loot
- 4th pawn arrives at Hub — semi-deterministic:
  - Guaranteed decent level + passion in one random gathering skill
  - Comes with basic tool for that skill
- Tutorial: "A new Runner arrived. They're skilled in <skill>, perhaps send them to gather while your others farm the raid?"
- This is the first "manage parallel activities" moment (3 in raid, 1 gathering)
- Tutorial ends. Everything opens up. Full map revealed (or majority).
- Player explores freely — can send runners anywhere, including impossible endgame content

## Open Questions
- When/how does the player first consciously modify a GATHERING automation rule? Currently only combat rules are tutorialized. Gathering customization is discovered organically. Might need a gentle nudge — TBD.
- Exact ability system (level-based unlocks vs itemized drops vs something else)
- Whether to add more automation tutorial steps after the raid (macro automation, more gathering rules) or just let the player figure it out
- Possible later tutorial moments: Pack Animal unlock, Macro automation introduction
