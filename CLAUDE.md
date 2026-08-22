# Loose Lips — working notes

Free-form AI dialogue for **Shadows of Doubt**. You talk to any citizen in your own words,
a local LLM answers in character, and what it says changes the world.

This file is the briefing for any Claude Code session opened in this repository, on any
machine. Read it before answering questions about this project.

## The one rule the whole design rests on

**The model proposes, the executor disposes.** Every effect the model asks for is checked
against real game state before anything happens, and refused *with a reason* when it cannot.
A hallucinating model degrades into a citizen who talks a big game and does nothing — never
into a citizen who conjures a key out of nothing.

Two corollaries that have already been paid for in bugs:

- **Name effects by direction, not by the phrase a person would say.** An effect called
  `call_police` set officers on the *player*. Telling an enforcer "that person was trying to
  rob me" got the player held at gunpoint, because the model read the name the way anyone
  would. Anything ambiguous now needs an explicit target or is refused.
- **Never deserialise a model reply strictly.** `System.Text.Json` is all-or-nothing: one
  field in an unexpected shape throws, the reply degrades to prose, and prose carries no
  effects at all. See `src/Player2/TolerantJson.cs`.

## Layout

```
src/Core/      config, main-thread pump, conversation memory, transcript, request budget
src/Context/   reads a citizen into a snapshot, builds the prompt
src/Dialog/    the dialogue options, the typing overlay, settings window, orchestration
src/Player2/   HTTP client for the Player2 app, DTOs, tolerant JSON
src/World/     everything that changes the game: effects, earshot, goals, allegiance…
tests/         off-engine harness — `dotnet run` from that folder
_ref/          decompiled game code (git-ignored, 42 MB)
```

**Adding an effect is one entry** in `WorldEffectExecutor.RegisterAll` — name, description,
config gate, aliases, conflict group, handler. The vocabulary offered to the model is
*generated* from that same list, so the two can never drift apart. Do not add a switch case.

## Build, test, install

```bash
dotnet build -c IL2CPP              # the mod
cd tests && dotnet run              # 33 off-engine checks, no game needed
python tests/scenarios.py           # does a citizen do what they said? live app, costs joules
python tests/investigation.py       # does conversation produce usable evidence? same
```

The two Python harnesses talk to the live Player2 app and cost credits - about a joule a turn,
so a full run of both is a few dozen. They read the effect vocabulary and the prompt out of the
mod's own source rather than restating either, and `investigation.py` mirrors what
`Player2Client` does with a failed reply, so neither can quietly drift from the shipped mod.

Deployment is a manual copy of `bin/IL2CPP/net6.0/LooseLips.dll` into
`<BepInEx profile>/plugins/LooseLips/`. **The DLL is locked while the game runs** — close it
first.

`Directory.Build.props` finds BepInEx by itself and should not need editing: the
`LOOSELIPS_BEPINEX` variable wins if set, then a Thunderstore profile called `Modded`, then a
BepInEx folder beside the repository or beside the game, and finally **any** Thunderstore
profile that has interop assemblies — people rename their profiles. That last step has to run
as a target rather than a property, because profile names are not known until a wildcard is
expanded and item lists do not exist yet while properties are evaluated. It runs before
`ResolvePackageAssets` on purpose: added any later, the explicit reference to the profile's
`Il2CppInterop.Runtime` no longer displaces the NuGet copy, and the build fails with CS1704 —
the same assembly imported twice. For the same reason the reference list in `LooseLips.csproj`
is guarded on the folder existing, so the two paths never both contribute.

## Environment facts that cost time to rediscover

- The game is **IL2CPP** with **BepInEx 6 bleeding-edge**, via Thunderstore Mod Manager.
- **BepInEx deletes and regenerates the interop assemblies at game start.** A build during
  that window fails with "UnityEngine could not be found". Wait, then rebuild — nothing is
  broken.
- Regenerate decompiled reference with `ilspycmd -t <FullNamespace.Type> <Assembly-CSharp.dll>`.
  The **full namespace is required** (`UnityEngine.GUILayout`, not `GUILayout`).
- **IMGUI calls taking arrays want Il2Cpp array types.** A managed `string[]` converts to
  `Il2CppStringArray` implicitly, so `GUILayout.Toolbar` compiles — and silently draws
  nothing. Prefer single-string calls.
- **Never hold a `Citizen` across frames.** The game reuses citizen objects, so a stored
  reference eventually drives a different person — it showed up in testing as a follower
  being visually replaced by a stranger. Store `humanID` and re-resolve with
  `CityData.Instance.GetHuman(id, out human, false)`.
- **The case board is reached through `CasePanelController.Instance`**: `activeCase` is the open
  one, and `PinToCasePanel(case, evidence, dataKey, ...)` files an entry under a key. A
  citizen's own evidence entry is `citizen.interactable.evidence`, and the keys worth filing are
  `Evidence.DataKey.name / address / work / jobTitle / partnerFirstName / telephoneNumber`.
  Sightings are separate and go through `Human.RevealSighting`. Whether pinning also marks the
  value *discovered* is unverified - see `Evidence.AddDiscovery` if a filed card reads "???".
- Taking the mouse for a mod window needs the game's own API, not `UnityEngine.Cursor`
  (overwritten every frame): `InputController.Instance.SetMouseInputMode / SetCursorVisible /
  SetCursorLock`, plus `enableInput = false` to stop game hotkeys firing while typing, plus
  `Player.Instance.EnablePlayerMovement(false)` **and** `EnablePlayerMouseLook(false)` —
  movement and look are separate switches.

## The Player2 app

Local desktop app at `http://localhost:4315`, OpenAPI at **`/v1/openapi.json`** (every path
carries a `/v1` prefix). Header `player2-game-key` is attribution, not a secret, but some
endpoints only answer when it is present.

- `POST /v1/chat/completions` — OpenAI-shaped, backed by gpt-oss-120b. **2.7–5 s in game.**
- **Requests do cost credits**: about a third of a joule per ~750-token exchange, and the
  balance refills over time. `GET /v1/joules`. An early measurement of "zero" was rounding.
  Invisible on a stocked account, decisive on a free one — hence `RequestBudget`.
- 401 / 402 / 429 mean not signed in / out of credits / too fast. Told apart in
  `Player2Status`.
- **The model stops mid-object on some prompts, and `finish_reason` still says `stop`.** It is
  not a length cutoff and not a refusal - the reply simply ends after forty to sixty
  characters, usually inside the private reasoning. It is **prompt-sensitive, not random**:
  accusing a citizen whose ground truth said "I am the murderer the player is hunting" did it
  11 times in 12, the same accusation to an innocent citizen 0 times in 8, and rewording the
  secret to "I am the one responsible for the death they are investigating" took it to 4 in 12.
  Hence `ReplySalvage`, the retry in `GenerateReplyAsync`, and the wording in
  `GroundTruthReader`. **Never treat unparseable output as speech** - that is how a citizen
  ends up saying `{"reason": "I am maintaining my composure and` out loud.
- **Roughly a joule per exchange in practice**, not the third of one measured early on: these
  prompts run past 1000 tokens. 150 calls of the test harnesses cost 153.
- **TTS is unusably slow**: 45 s to synthesise six words. Babbler is the better voice.
- **STT returns transcript only, no amplitude** — whisper/shout detection would have to
  measure the microphone locally.

## Verified in game

Free-form conversation, deliberate lying (`truthfulness` 0.2–0.6 with private reasoning),
alarm driving real AI alertness, `flee`, `call_police`, shouting reaching 8 people,
relationship caps refusing the model's over-reach. Evidence lives in
`<BepInEx>/LooseLips-transcript.log`, which records every exchange and every refusal reason.

**Most of the rest is written and compiles but has not been seen working in game.**

## Publishing

Not published anywhere yet. The standing instruction is **publish only when it works**, and
publishing needs the owner's own Thunderstore / mod.io tokens. Shadows of Doubt uses
**mod.io** (game id 5624), not Steam Workshop; Thunderstore is the channel for BepInEx mods.
