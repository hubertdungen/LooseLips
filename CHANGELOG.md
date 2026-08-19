# Changelog

## 0.8.0

The street answers back.

* **People react to what happens around them.** Citizens near you now say something when they
  notice a crime, when a fight starts, when they bolt, or when something badly frightens them -
  written for who they are and what they saw, rather than picked from a list. It hangs off the
  game's own DialogController.SeenOrHeardUnusual for the moments the game announces, and polls
  for the ones it does not.
* **Whispering.** Volume is now three levels, not two, and the model picks. The engine only
  knows shouting from not shouting, so a whisper is delivered as ordinary speech - what makes
  it a whisper is reach: about two metres, so leaning in to tell somebody something is
  genuinely private and the room does not overhear. Shouting still carries next door.
  The situation carries a stated prior, because tested without one a timid character whispered
  through a brawl - characterful, but it left the shout tier unused.
* **Rationed on purpose.** A chat request was measured costing no joules at all, so the real
  ceiling is not credits but your own machine: every line is a few seconds of it, and a street
  where eight people each react to a gunshot would queue the better part of a minute and
  arrive after the moment had passed. Ambient life is limited to one generation at a time, a
  floor between lines, a per-person cooldown and an hourly ceiling - all adjustable, all
  visible in the settings. Anything you are directly part of is never rationed: a conversation
  you started always answers first.
* Off by default. Turn it on in the Talking tab.

## 0.7.0

A hardening pass. No new powers - the same ones, harder to break.

* **Effects are declared once.** Name, description, config gate, aliases, contradictions and
  handler are now one entry in a catalogue, and the vocabulary sent to the model is generated
  from the same list that dispatches it. Before, three hand-maintained lists had to agree, and
  nothing enforced it: an effect could be offered and never handled, and the symptom of that
  is a citizen who says they will do something and does not. Adding an effect is now one entry.
* **A single malformed field no longer costs the whole turn.** Deserialisation was all or
  nothing, so "effects": ["flee"] instead of a list of objects, or "truthfulness": "0.8" as a
  string, threw - and the reply fell back to being treated as prose, which carries no effects,
  no relationship movement and no alarm. Every field a model can plausibly get slightly wrong
  is now read leniently.
* **Effect names are folded.** "Give Money", "give-money", "giveMoney" and "GIVE_MONEY" all
  reach give_money, plus 46 aliases for the words models reach for instead.
* **Contradictions and duplicates are resolved.** A citizen cannot flee and attack in one
  breath: effects are grouped, the first of a group wins, and the rest are refused with a
  reason. The same effect twice is one effect. The outcome no longer depends on the order the
  model happened to list them in.
* **Replies that arrive too late are dropped safely.** A citizen can be despawned during the
  seconds a request is in flight, and a destroyed object does not always read as null.
* Added an off-engine test harness under tests/, covering name folding, every reply shape and
  the number-format cases. 25 checks, run with `dotnet run` from that folder.

## 0.6.0

* **Fixed the police being turned on you when you reported a crime.** The effect was called
  "call_police", which reads as calling them for help, and in fact set every officer in
  earshot onto the investigator - so telling an enforcer you were being mugged got you held
  at gunpoint. The vocabulary is now directional and cannot be misread:
  report_the_investigator, send_police_after (needs a name), call_police_off. The old name is
  only honoured when a target makes the intent explicit, and refused with a reason otherwise.
* **Friend or foe.** Somebody can be talked into taking your side, or turned against you.
  Liking you is a feeling and siding with you is a decision, so the two are tracked
  separately: an ally has to like you past a threshold first, and cannot be declared into
  existence from nothing. Allies who are close enough, and not already panicking, will go
  after whoever is attacking you. Frightening an ally badly enough takes them out of the
  fight, so loyalty is something you keep rather than something you set.
* **Haggling.** A citizen can name a price for what they know, and be paid it out of your own
  money. The price has to be named in one turn and settled in another, so nothing can be
  invented and paid for in the same breath, and payment is refused unless the demand was
  really made and you really have the money. Paying in full buys goodwill.
* **Fleeing from a named person.** The game's flee state is a mood with no direction, so this
  is fleeing plus heading home, which is what actually puts distance between them.

## 0.5.0

Fixes the settings window, and closes most of the gap between what the mod could do and
what it was asked to do.

* **Fixed the settings window being unusable in game.** It set UnityEngine.Cursor directly,
  which the game overwrites through its own InputController every frame - so the mouse kept
  turning the character and the window would not take clicks. It now asks the game via
  SetMouseInputMode / SetCursorVisible / SetCursorLock, suspends player movement while open,
  and keeps asserting the claim rather than setting it once. The window is also pulled back
  on screen when opened, drawn on top of the game's HUD, and the hotkey is logged.
* **People remember you between sessions.** Conversations are kept per city, keyed by the
  city seed, so somebody you talked into something yesterday does not greet you as a
  stranger today, and can be held to what they said.
* **Cash changes hands.** Citizens carry money in their wallet, and can now be talked or
  frightened into handing some over, capped per conversation. The prompt is also told what
  is in their pockets - a playtest had somebody refuse with "I have nothing to give" while
  carrying money, because the mod had only ever looked at their hands.
* **Attacking somebody other than you.** attack now takes a target, who has to be present.
* **Talking somebody into coming with you.** The game has no companion behaviour at all, so
  this repeatedly re-points them at where you are standing: they trail you, take their own
  route, and give up if you outrun them or after a timer.

## 0.4.0

The half of the original design that was missing: a conversation that changes more than
the person you are having it with.

* **Real evidence.** A citizen can now give up where and when they saw somebody, through the
  game's own witness mechanism, so it arrives as a followable lead in your case file rather
  than a sentence that merely sounds like one. They can only name people the game recorded
  them actually seeing, and the prompt is given that list, so testimony cannot be invented.
* **Changing what people are doing.** Talk somebody into going home, to work, to bed, or into
  leaving - this rewrites their AI goal rather than their mood. Goal presets are matched by
  name at runtime, and the Debug tab can write the game's real goal list to the transcript so
  the matching can be corrected from evidence.
* **The whole room, not one person.** crowd_panic, crowd_settle and crowd_gather land on
  everybody within earshot, which is what shouting was always for.
* **Citizens talking to each other.** Pairs standing near you hold their own generated
  conversations, and gossip in them is real: when one mentions seeing somebody, the other
  genuinely learns it and can be asked about it afterwards. Off by default, and only ever
  where you can overhear - a conversation nobody hears costs a request and returns nothing.
* Generated lines now have typographic punctuation folded down to ASCII, so a missing glyph
  cannot show up as a box mid-sentence.

## 0.3.0

* Renamed to **Loose Lips**. The plugin id changed with it, so the old settings file
  (`dev.hubert.sodplayer2.cfg`) is no longer read and settings start from their defaults.
* Added a transcript, written to `BepInEx/LooseLips-transcript.log`: every exchange, how long
  the model took, what it asked for, and what the game allowed or refused. On by default.
* Refused effects now come with a reason. Previously an effect that could not happen was
  silently dropped, which is indistinguishable in game from a citizen who simply said no.
* Added a self-test in the Debug tab that walks the whole chain against the person nearest you
  and names the first step that fails.
* Fixed text to speech, which could never have worked: the request sent `voice_id` where the
  API expects a `voice_ids` list and omitted the required `speed`, so it was answered with 422.
  It also shared the chat deadline, which is far too short for synthesis, and now has its own.
* Added a window opacity setting, beside the interface scale.

## 0.2.1

* Fixed the settings window not opening. The overlay was created from a Toolbox.Start
  postfix, and Harmony runs every postfix for a method as one chain, so another mod
  throwing in that same chain skipped ours entirely. It is now created at plugin load,
  where nothing else can starve it.
* Rebuilt against the interop assemblies regenerated for the current game build.

## 0.2.0

* Added an in-game settings window on F4. Every setting can be changed while playing,
  including a live test of the connection to the Player2 app and a running count of how
  many people your voice currently reaches.
* Added an interface scale option for high resolution screens.

## 0.1.0

First working build.

* Two new dialogue options on every citizen: "Say something..." and "Shout something...".
* Free text entry, with a voice reach meter showing who is close enough to hear.
* Replies are generated by the Player2 desktop app running locally and spoken through the
  game's own speech pipeline, so they get bubbles, subtitles and are overheard normally.
* The model is given the citizen's traits, job, home, relationship to you, alarm level and
  what they genuinely know, and decides for itself whether to be honest.
* Convincing lines can move relationships, hand over a held item, call police onto you or
  off you, accuse someone else present, or make a citizen flee, fight or surrender.
* Shouting carries into adjacent rooms and stirs up bystanders.
