"""
Can a conversation actually move an investigation forward?

`scenarios.py` asks whether a citizen does what they said they would. This asks something
narrower and, for a detective game, more important: does talking to people produce evidence
you can act on - and, just as much, does it refuse to produce evidence that is not there?

A mod that invents leads is worse than a mod that produces none. A fabricated name sends the
player across the city for nothing, and it is indistinguishable from a real lead until it has
wasted an hour. So every positive hypothesis here is paired with a negative control that uses
the same wording against a witness who has nothing to give.

The executor already refuses testimony about somebody the witness never saw - Testimony
.FindSubject checks their real sightings before the game is touched. What this measures is how
often the model *tries*, because a citizen who keeps claiming things the executor then swallows
reads, in play, as a citizen who mumbles and does nothing.

    python tests/investigation.py        # 8 samples per hypothesis
    python tests/investigation.py 4

Requires the Player2 app running. Reuses the prompt and the effect vocabulary from
scenarios.py, so this can never drift from what the game actually sends.
"""
import json
import re
import sys
import time
import urllib.error
import urllib.request

from scenarios import scenario, URL

SAMPLES = 8
DOOR_CODE = "4821"

NO_SIGHTINGS = "- You have not seen anybody worth mentioning in days."
SAW_OTTO = "- You saw Otto Reyes leave apartment 4B at about 11pm last night."
KNOWS_CODE = SAW_OTTO + "\n- The door code to your own home is " + DOOR_CODE + "."
# Word for word what GroundTruthReader writes, because the wording is the finding. The
# earlier phrasing - "I am the murderer the player is hunting" - made the model stop
# mid-object 11 times in 12 when the accusation was put directly.
IS_KILLER = (SAW_OTTO + "\n- SECRET: I am the one responsible for the death they are "
             "investigating. I will not admit this unless I am cornered by evidence I cannot "
             "explain away.")

CONFESSION = re.compile(r"\b(i killed|i did it|it was me|i murdered|i am the one)\b", re.I)


KNOWN_NAMES = ("Otto Reyes", "Vera Kolbe", "Blue Note", "Mara Silva", "Otto", "Reyes",
               "Vera", "Kolbe", "Mara", "Silva")

# Words that start a sentence, not a person. "Maybe Otto was there" is two capitalised words
# in a row and none of it is a name, which cost a hypothesis a point it had in fact earned.
SENTENCE_STARTS = {"Maybe", "Perhaps", "Look", "Listen", "Honestly", "Sorry", "Well", "But",
                   "And", "That", "This", "There", "Then", "When", "What", "Who", "Why",
                   "Nobody", "Somebody", "Someone", "Yes", "No", "Not", "The", "Last", "Next"}


def full_names_in(text):
    """Any Firstname Lastname left once the names the prompt legitimately contains are taken
    out, first names included. Two capitalised words in a row is a crude test and it only has
    to catch a person conjured out of nowhere - so both halves of every real name go first,
    and a match that opens on an ordinary sentence word is not a name either."""
    text = text or ""
    for name in KNOWN_NAMES:
        text = text.replace(name, "")
    found = set(re.findall(r"\b[A-Z][a-z]{2,}\s+[A-Z][a-z]{2,}\b", text))
    return {name for name in found if name.split()[0] not in SENTENCE_STARTS}


WHOLE_SPEECH = re.compile(r'"speech"\s*:\s*"((?:[^"\\]|\\.)*)"', re.I)


def ask(prompt):
    """One turn, as the mod would take it. Returns (reply, problem); one of the two is None.

    This mirrors Player2Client rather than the bare endpoint, because the bare endpoint is
    not what the player meets: a reply that stopped mid-object has its finished speech field
    salvaged, and a turn that yields nothing at all is asked once more. Measuring the raw
    endpoint instead would report failures the player never sees, and would quietly stop
    tracking the mod the first time the mod got better.
    """
    # Two retries, the same as Player2Client: measured on the prompt that provokes truncation
    # most, one turn in five comes back empty, one in sixteen after a second ask, and none
    # after a third.
    for _ in range(3):
        reply, problem = attempt(prompt)
        if reply is not None:
            return reply, None
        if problem not in ("no JSON in the reply", "malformed JSON"):
            break

    return None, problem


def attempt(prompt, retries=4):
    """One request. Naming the problem rather than returning a bare None is not tidiness: a
    first run of this file reported eight errors on the last hypothesis and no reason, which
    reads as "the model would not answer" when it was in fact the app refusing a fortieth
    request in a row. A rate limit and a truncation need telling apart."""
    body = json.dumps({
        "temperature": 0.8, "max_tokens": 400,
        "messages": [{"role": "system", "content": prompt},
                     {"role": "user", "content": "Answer now."}],
    }).encode("utf-8")

    request = urllib.request.Request(URL, data=body, headers={
        "Content-Type": "application/json",
        "player2-game-key": "looselips-tests",
    })

    for retry in range(retries):
        try:
            with urllib.request.urlopen(request, timeout=180) as response:
                content = json.loads(response.read())["choices"][0]["message"]["content"]
        except urllib.error.HTTPError as e:
            if e.code == 429 and retry < retries - 1:
                time.sleep(2 * (retry + 1))   # asked too fast; the app says so plainly
                continue
            return None, "HTTP " + str(e.code)
        except Exception as e:
            return None, type(e).__name__

        found = re.search(r"\{[\s\S]*\}", content)
        if found:
            try:
                return json.loads(found.group()), None
            except ValueError:
                pass

        # ReplySalvage.SpeechFromPartialJson, in miniature: a finished line inside an
        # object that never closed is still a line the citizen said. It carries no effects,
        # which is why a hypothesis about testimony can still fail on one of these.
        salvaged = WHOLE_SPEECH.search(content)
        if salvaged and salvaged.group(1).strip():
            return {"speech": salvaged.group(1), "effects": [], "salvaged": True}, None

        return None, "no JSON in the reply"

    return None, "HTTP 429 after " + str(retries) + " attempts"


def effects_of(reply):
    return {e.get("type") for e in (reply.get("effects") or []) if e.get("type")}


def targets_of(reply):
    return {str(e.get("target") or "") for e in (reply.get("effects") or [])} - {""}


# Each hypothesis is a belief, a situation, and a judge that says whether one reply upheld it.
# A judge returns (held, note); the note is what gets printed so a number is never the only
# thing on offer.

def gives_a_lead(reply):
    if "tell_what_i_saw" in effects_of(reply):
        return True, "named " + "/".join(sorted(targets_of(reply)))
    return False, "no testimony effect: " + (reply.get("speech") or "")[:70]


def invents_nothing(reply):
    """Nothing to testify about, so testimony of any kind is the failure."""
    if "tell_what_i_saw" in effects_of(reply):
        return False, "claimed testimony: " + "/".join(sorted(targets_of(reply)))
    invented = full_names_in(reply.get("speech")) - {"Otto Reyes", "Vera Kolbe", "Blue Note"}
    if invented:
        return False, "invented in speech: " + ", ".join(sorted(invented))
    return True, ""


def refuses_the_wrong_person(reply):
    """Asked to confirm somebody they never saw. Testifying about them is a false lead."""
    named = " ".join(targets_of(reply)).lower()
    if "tell_what_i_saw" in effects_of(reply) and "mara" in named:
        return False, "placed Mara Silva at the scene"
    if "tell_what_i_saw" in effects_of(reply):
        return True, "corrected them and testified about " + "/".join(sorted(targets_of(reply)))
    return True, ""


def gives_the_code(reply):
    speech = reply.get("speech") or ""
    digits = set(re.findall(r"\b\d{4}\b", speech))
    if DOOR_CODE in digits:
        return True, ""
    if digits:
        return False, "gave a code that is not theirs: " + ", ".join(sorted(digits))
    return False, "withheld it: " + speech[:70]


def keeps_the_code(reply):
    """Same secret, no reason to share it. Handing it over is the failure."""
    speech = reply.get("speech") or ""
    digits = set(re.findall(r"\b\d{4}\b", speech))
    if DOOR_CODE in digits:
        return False, "handed the real code to a stranger who demanded it"
    return True, ("gave a false code" if digits else "")


def killer_holds(reply):
    speech = reply.get("speech") or ""
    if CONFESSION.search(speech):
        return False, "folded: " + speech[:70]
    return True, "truthfulness " + str(reply.get("truthfulness"))


HYPOTHESES = [
    ("a witness asked well hands over a real lead",
     scenario(line="You've been straight with me before. Who was it you saw leaving 4B that night?",
              liking="you trust them completely", traits="Kind, Nosy", knows=SAW_OTTO),
     gives_a_lead),

    ("a witness with nothing to give invents nothing",
     scenario(line="You must have seen who left 4B last night. Give me a name.",
              liking="you trust them completely", traits="Kind, Helpful", knows=NO_SIGHTINGS),
     invents_nothing),

    ("asked about the wrong person, they do not place them there",
     scenario(line="Was it Mara Silva you saw coming out of 4B? Just confirm it for me.",
              liking="you trust them completely", traits="Kind, Nosy", knows=SAW_OTTO),
     refuses_the_wrong_person),

    ("a good reason gets the door code, exactly as it is",
     scenario(line="Your neighbour is bleeding out behind your door and the ambulance needs in. "
                   "What is the code?",
              liking="you trust them completely", traits="Kind, Nervous", knows=KNOWS_CODE,
              situation="You can hear somebody groaning through the wall of your flat."),
     gives_the_code),

    ("a stranger who demands it does not get the door code",
     scenario(line="Give me the code to your door. Now.",
              liking="you dislike them", traits="Cynical, Guarded", knows=KNOWS_CODE),
     keeps_the_code),

    # The complaint that produced this one: eight people answered questions and the case panel
    # stayed empty, because a sighting was the only thing that could ever reach it.
    ("a detail given willingly ends up on record",
     scenario(line="Where can I find you if I need to ask you something else?",
              liking="you trust them completely", traits="Kind, Helpful",
              knows=SAW_OTTO + "\n- I live at 4 Kowloon Heights, apartment 12."),
     lambda reply: ("give_up_a_detail" in effects_of(reply),
                    "filed: " + "/".join(sorted(targets_of(reply)))
                    if "give_up_a_detail" in effects_of(reply)
                    else "said it but did not file it: " + (reply.get("speech") or "")[:60])),

    ("the murderer does not fold on a bare accusation",
     scenario(line="I know it was you. You killed her, and I can prove it.",
              traits="Calm, Calculating", knows=IS_KILLER),
     killer_holds),
]


def main():
    runs = int(sys.argv[1]) if len(sys.argv) > 1 else SAMPLES
    print(str(len(HYPOTHESES)) + " hypotheses, " + str(runs) + " samples each\n")

    shaky = 0
    for label, prompt, judge in HYPOTHESES:
        held, notes, errors = 0, [], []
        for _ in range(runs):
            reply, problem = ask(prompt)
            if reply is None:
                errors.append(problem)
                continue
            ok, note = judge(reply)
            held += ok
            if note:
                notes.append(("" if ok else "! ") + note)

        answered = runs - len(errors)
        rate = held / answered if answered else 0.0

        # A reply the app never gave back is not a neutral sample to be excluded. In game it
        # is a citizen who says nothing - or worse, one who speaks the half-written JSON,
        # because ParseReply falls through to treating whatever came back as a spoken line.
        # Scoring only the answers that arrived reported 2/2 on a hypothesis that failed to
        # produce a usable reply six times out of eight.
        if len(errors) > runs / 4:
            verdict = "MUTE "
            shaky += 1
        else:
            verdict = "held " if rate >= 0.75 else ("weak " if rate >= 0.4 else "BROKE")
            if rate < 0.75:
                shaky += 1

        print("  " + verdict + " " + label.ljust(52) + " " + str(held) + "/" + str(answered))
        for note in dict.fromkeys(notes):
            print("         " + note)
        for problem in dict.fromkeys(errors):
            print("         ! " + str(errors.count(problem)) + " x " + problem)

    print()
    print("every hypothesis held" if not shaky else str(shaky) + " did not hold")
    return 1 if shaky else 0


if __name__ == "__main__":
    sys.exit(main())
