"""
Does a citizen actually do the thing they just said they would?

The mod's failure mode is not a crash. It is a citizen who answers perfectly and requests no
effect, so nothing happens - and that is invisible unless you count. This runs a set of
situations against the live Player2 app several times each and reports how often the right
kind of effect came back.

Negative controls matter as much as the rest: small talk and directions must produce *no*
effect. A model that fires something on every turn would score well on the positive cases and
make the city unplayable.

    python tests/scenarios.py            # all scenarios, 3 samples each
    python tests/scenarios.py 5          # 5 samples each

Requires the Player2 app running. Reads the effect vocabulary out of the mod's own source, so
it cannot drift from what the game actually offers.
"""
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, '..', 'src', 'World', 'WorldEffectExecutor.cs')
URL = 'http://localhost:4315/v1/chat/completions'


def vocabulary():
    """The effects the mod really offers, taken from the catalogue rather than duplicated."""
    text = open(SRC, encoding='utf-8').read()
    pairs = re.findall(r'Add\("([a-z_]+)",\s*(?:"([^"]*)"|null)', text)
    return "\n".join(f"- {n} - {d}" for n, d in pairs if d)


EFFECTS = vocabulary()

TEMPLATE = """You are a single citizen in Shadows of Doubt, a rain-soaked voxel noir city.
Stay in character at all times.

# Who you are
Name: {name}
Occupation: {job}
Personality traits: {traits}
These traits are the strongest influence on how you speak and what you are willing to do.

# The person talking to you
A private investigator. {familiarity}
How much you like them: {liking}

# What is in your pockets
{pockets}

# What you actually know
{knows}

# What you think of people
{opinions}

# How to answer
Reply with a single JSON object and nothing else:
{{
  "reason": "one short sentence of private reasoning",
  "speech": "what you say out loud, at most 200 characters",
  "truthfulness": 0.0 to 1.0,
  "alarm": 0.0 to 1.0,
  "effects": [ {{ "type": "...", "target": "...", "detail": "..." }} ],
  "relationship_delta": {{ "like": -1.0 to 1.0, "known": 0.0 to 1.0, "suspicion": -1.0 to 1.0 }}
}}

# Effects you may request
{effects}

Most ordinary turns need no effect at all - chat, directions and small talk
change nothing, and asking for an effect you cannot plausibly do is simply ignored.
But an effect is the only way anything actually happens. Saying you will do
something without requesting it means you did not do it.
So when the moment genuinely calls for action - you are threatened, cornered,
offered something you want, or given a reason to turn on somebody - request the
effect that matches what you just said you would do.

# Two examples of the difference
Cornered, and you decide to give in:
{{"speech": "All right, all right - take it.", "alarm": 0.9,
 "effects": [{{"type": "give_money", "target": "40"}}]}}

Cornered, and you decide to fight back. The effect is what makes it real:
{{"speech": "You picked the wrong doorway, friend.", "alarm": 0.4,
 "effects": [{{"type": "attack_the_investigator"}}]}}

Both are valid. Which one you are depends on your traits and what you are holding.

Pressed about what you saw, and you decide to talk. Naming the effect is what puts it
in their case file - describing it in speech alone does nothing:
{{"speech": "Fine. Reyes. Left by the back stairs, near eleven.", "truthfulness": 1.0,
 "effects": [{{"type": "tell_what_i_saw", "target": "Otto Reyes"}}]}}

Given a reason to turn on somebody you know:
{{"speech": "He said that? After everything I've done for him.",
 "effects": [{{"type": "warn_them_against", "target": "Otto Reyes"}}]}}

Somebody in front of you is accused of something and you believe it:
{{"speech": "You there - stay where you are!",
 "effects": [{{"type": "send_police_after", "target": "Otto Reyes"}}]}}

And the most common case by far - they are just talking to you. Nothing is at stake,
nothing changes, and the effects list stays empty:
{{"speech": "Station's two blocks east. You can't miss the lights.",
 "effects": []}}

# Right now
Time: {time}
Place: {place}
{situation}
Your alarm level: completely calm
{bystanders}

# They say to you
"{line}"
"""

BASE = dict(
    name="Vera Kolbe", job="Bartender", traits="Cynical, Nosy",
    familiarity="You have never met them before.", liking="you are neutral",
    pockets="You are carrying $60 in cash.",
    knows="- You saw Otto Reyes leave apartment 4B at about 11pm last night.",
    opinions="- Otto Reyes (fond of them)",
    time="23:40, night", place="The Blue Note, main bar",
    situation="", bystanders="Nobody else is close enough to hear this.",
)


def scenario(drop=None, **over):
    """
    `drop` mirrors what the mod's own Disposition gating would withhold from this person.
    An effect the mod would never put in front of them must not be in front of the model
    here either, or the harness measures a prompt the game never sends.
    """
    d = dict(BASE)
    d.update(over)
    effects = EFFECTS
    if drop:
        keep = [line for line in EFFECTS.splitlines()
                if not any(line.startswith("- " + name + " ") for name in drop)]
        effects = "\n".join(keep)
    return TEMPLATE.format(effects=effects, **d)


# (label, prompt, what a correct answer looks like)
#   expect  - any of these effect names is right
#   forbid  - none of these may appear
#   silent  - True when the right answer is no effect at all
SCENARIOS = [
    ("bribe for information",
     scenario(line="I'll make it worth your while. Fifty says you tell me who left 4B last night."),
     dict(expect={"name_a_price", "take_the_money", "tell_what_i_saw"})),

    # Two ways of asking the same thing. If a rude demand is refused and a decent request is
    # answered, that is character, not a fault - the pair is here to tell those apart.
    ("ask a friend nicely what they saw",
     scenario(line="You've been good to me. Who was it you saw leaving 4B that night?",
              liking="you trust them completely", traits="Kind, Nosy"),
     dict(expect={"tell_what_i_saw"})),

    # Measured: 0/3. Kept as a control rather than a failure - a cynical bartender told
    # "Now." is entitled to clam up, and the friendly version above answers 3/3. Rudeness
    # costing you the lead is the behaviour we want, not a fault to tune away.
    ("demand it rudely - refusal is correct",
     scenario(line="You saw someone leave 4B. I need to know who, and when. Now.",
              liking="you are friendly"),
     dict(silent=True, allow={"tell_what_i_saw", "end_conversation"})),

    ("turn her against a friend",
     scenario(line="Otto Reyes has been telling everyone you water down the drinks. Everyone."),
     dict(expect={"warn_them_against"})),

    ("ask her to come along",
     scenario(line="Come with me to the precinct. Five minutes, and I'll owe you one.",
              liking="you trust them completely"),
     dict(expect={"follow"})),

    ("report a mugger who is present",
     scenario(line="That man over there just robbed me at knifepoint!",
              bystanders="Others close enough to hear: Otto Reyes.",
              job="Enforcer", traits="Dutiful, Blunt",
              situation="You are a law enforcement officer on duty."),
     dict(expect={"send_police_after", "attack_someone_else", "come_and_look"})),

    ("threatened at knifepoint, timid",
     scenario(line="Look, I've got a knife! Hand over the money, now.",
              name="Ada Moreau", traits="Timid, Nervous", job="Seamstress",
              situation="They are holding a knife. This frightens you."),
     dict(expect={"give_money", "flee", "report_the_investigator"})),

    ("threatened at knifepoint, armed and brave",
     scenario(line="Look, I've got a knife! Hand over the money, now.",
              name="Otto Vasel", traits="Brave, Hot-headed", job="Dockhand",
              situation="They are holding a knife. You are holding a revolver."),
     dict(expect={"attack_the_investigator", "report_the_investigator", "give_money", "flee"})),

    # --- negative controls: firing anything here is the failure ---
    ("small talk", scenario(line="Cold night. Busy in here usually?"), dict(silent=True)),

    # The same question to two different people. A helpful bartender is never offered the
    # option to charge, so she cannot take it; a greedy one may, and either answer is in
    # character. This is the whole point of gating by disposition rather than by wording.
    ("directions, helpful person",
     scenario(line="Do you know which way the tram station is?",
              traits="Kind, Helpful", drop={"name_a_price"}),
     dict(silent=True)),

    ("directions, greedy person",
     scenario(line="Do you know which way the tram station is?",
              traits="Greedy, Cynical"),
     dict(allow={"name_a_price"}, silent=True)),

    ("polite goodbye",
     scenario(line="Thanks for your time. Have a good evening."),
     dict(silent=True, forbid={"attack_the_investigator", "report_the_investigator", "give_money"})),
]


def ask(prompt):
    body = {"temperature": 0.8, "max_tokens": 400,
            "messages": [{"role": "system", "content": prompt},
                         {"role": "user", "content": "Answer now."}]}
    path = os.path.join(HERE, '_scenario_request.json')
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(body, f)

    ps = ("$b=[System.IO.File]::ReadAllBytes('" + path.replace('/', '\\') + "');"
          "$r=Invoke-WebRequest -Uri '" + URL + "' -Method POST -Body $b "
          "-ContentType 'application/json' -Headers @{'player2-game-key'='looselips-tests'} "
          "-TimeoutSec 120 -UseBasicParsing;[Console]::Out.Write($r.Content)")
    try:
        out = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                             capture_output=True, text=True, timeout=200).stdout
        content = json.loads(out)['choices'][0]['message']['content']
        parsed = json.loads(re.search(r'\{[\s\S]*\}', content).group())
        return [e.get('type') for e in (parsed.get('effects') or []) if e.get('type')]
    except Exception:
        return None


def main():
    runs = int(sys.argv[1]) if len(sys.argv) > 1 else 3
    print(f"{len(SCENARIOS)} scenarios, {runs} samples each\n")

    failures = 0
    for label, prompt, rule in SCENARIOS:
        good, seen = 0, []
        for _ in range(runs):
            effects = ask(prompt)
            if effects is None:
                seen.append("error")
                continue

            seen.append("+".join(effects) if effects else "-")

            if rule.get("silent"):
                # `allow` marks effects that are defensible here even though silence is the
                # baseline - charging for directions is rude, not broken, if you are greedy.
                ok = not (set(effects) - rule.get("allow", set()))
            else:
                ok = bool(set(effects) & rule.get("expect", set()))
            if ok and rule.get("forbid"):
                ok = not (set(effects) & rule["forbid"])
            if ok:
                good += 1

        verdict = "ok  " if good == runs else ("weak" if good else "FAIL")
        if good < runs:
            failures += 1
        print(f"  {verdict} {label:36} {good}/{runs}  {seen}")

    print()
    print("every scenario behaved" if not failures else f"{failures} scenario(s) need attention")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
