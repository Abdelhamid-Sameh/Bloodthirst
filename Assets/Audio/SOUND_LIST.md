# Bloodthirst – sound list

Drop files into the folders below with these names, then in Unity run
**Bloodthirst → Assign Sounds From Assets-Audio** and save the scene.
Several files with the same prefix (`footstep_01`, `footstep_02`, …) become random variations.
`.wav`, `.ogg` or `.mp3` all work. Slots you have not filled stay silent, so you can add sounds a few at a time.

Sources: **EL** = ElevenLabs Sound Effects (set the duration, prompt influence ≈ 0.5, generate 4 and keep the best),
**PX** = Pixabay sound effects / music search (free, no attribution required, but log it under Credits),
**BC** = compose in Bosca Ceoil and export as .wav.
Trim silence at the start of every effect (Audacity: Effect → Truncate Silence), otherwise sounds feel late.

## Vampire  → `Assets/Audio/Vampire/`

| File name | Count | Source | ElevenLabs prompt / Pixabay search |
|---|---|---|---|
| `footstep_01..04` | 4 | PX | search "footsteps running asphalt", cut single steps (~0.3 s each) |
| `jump_01..02` | 2 | EL 0.6 s | "Quick heavy cloth whoosh of a long coat as a man leaps, no voice" |
| `land_01..02` | 2 | EL 0.5 s | "Leather boots landing hard on wet asphalt, single thud with a short cloth flap" |
| `bite_01..03` | 3 | EL 1 s | "Vampire fangs sinking into a neck, wet flesh puncture with a short snarl, close up" |
| `feed_01..02` | 2 | EL 2.5 s | "Slow wet gulping and sucking, a creature drinking blood, close up, dark" |
| `hurt_01..03` | 3 | EL 0.7 s | "Man grunts in pain as a bullet hits him, short sharp exhale through clenched teeth" |
| `death_01` | 1 | EL 3 s | "Vampire dying, a long rasping hiss turning into a groan, fading, with reverb" |
| `liedown` | 1 | EL 2 s | "Fabric rustle as a man lies down inside a wooden coffin, soft wood creak" |
| `rise` | 1 | EL 3 s | "Deep slow inhale, old wooden coffin creaks, cloak rustles as someone sits up" |
| `vanish` | 1 | EL 1.5 s | "Dark magic puff, a burst of smoke with a low whoosh and a faint bat wing flutter" |
| `appear` | 1 | EL 1.5 s | same as vanish, or the vanish file reversed in Audacity (Effect → Reverse) |
| `heartbeat` | 1 | EL 4 s | "Slow heavy human heartbeat, lub-dub at 66 bpm, deep low thump, seamless loop, no other sounds" (must loop cleanly) |

## Humans  → `Assets/Audio/Humans/`

| File name | Count | Source | ElevenLabs prompt / Pixabay search |
|---|---|---|---|
| `walk_01..04` | 4 | PX | "footsteps walking concrete sneakers", cut single steps |
| `run_01..04` | 4 | PX | "footsteps running concrete", cut single steps |
| `gasp_01..03` | 3 | EL 1 s | mix male/female: "Woman gasps in shock", "Man gasps in fear, sharp inhale" |
| `scream_flee_01..04` | 4 | PX / EL 2 s | PX "woman scream", "man scream terror"; EL "Man screaming in terror while running away, outdoors" |
| `scream_captured_01..02` | 2 | EL 2 s | "Panicked scream cut short as someone is grabbed, struggling" |
| `death_01..02` | 2 | EL 1.5 s | "Weak dying gasp, a last breath fading out" |

## Police  → `Assets/Audio/Police/`

| File name | Count | Source | ElevenLabs prompt / Pixabay search |
|---|---|---|---|
| `gunshot` | 1 | PX / EL 1.5 s | PX "pistol shot"; EL "Single 9mm pistol gunshot outdoors in a city street at night, short echo off buildings" |

## Coffin  → `Assets/Audio/Coffin/`

| File name | Count | Source | ElevenLabs prompt / Pixabay search |
|---|---|---|---|
| `lid_open` | 1 | EL 1.5 s | "Heavy old wooden coffin lid creaking slowly open, hinge squeak" |
| `lid_close` | 1 | EL 1.2 s | "Heavy wooden coffin lid swinging shut with a deep thud" |

## Music & ambience  → `Assets/Audio/Music/`

| File name | Source | Notes |
|---|---|---|
| `music` | BC (or PX "dark gothic ambient loop") | Bosca Ceoil: D minor (Scale: harmonic minor), ~90 BPM, 8–16 bars that loop cleanly. Low drone or organ pad on the root note, a slow plucked bass (pizzicato or synth bass), a harpsichord-style arpeggio on top, sparse percussion (a low tom every bar). Keep it quiet and moody: it plays under screams and gunshots. Export → .wav |
| `ambience` | PX | search "city night ambience" or "distant traffic night wind"; 1–2 min, no voices or music; must loop without a click |

## Credits (fill in as you add files)

| File | Source / author | Link |
|---|---|---|
| | | |
