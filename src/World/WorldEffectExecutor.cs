using System;
using System.Collections.Generic;
using LooseLips.Core;
using LooseLips.Player2;
using UnityEngine;

namespace LooseLips.World
{
    /// <summary>
    /// Turns the model's requested effects into real game actions.
    ///
    /// The rule here is that the model proposes and this class disposes. Every effect is
    /// checked against actual game state before it runs: a citizen cannot hand over an
    /// item they are not holding, cannot summon police when no officer is within earshot,
    /// and cannot shift a relationship further than the configured cap. Anything that
    /// fails a check is refused with a reason, because during a playtest "nothing
    /// happened" and "nothing was allowed to happen" look identical on screen and mean
    /// completely different things.
    /// </summary>
    public static class WorldEffectExecutor
    {
        /// <summary>What a turn actually did to the world, and what it was refused.</summary>
        public sealed class EffectReport
        {
            public readonly List<string> Applied = new List<string>();
            public readonly List<string> Rejected = new List<string>();

            public void Reject(string effect, string reason) => Rejected.Add(effect + " (" + reason + ")");
        }

        private static bool _registered;

        /// <summary>
        /// Declare every effect once: name, how it is explained to the model, the setting that
        /// gates it, what it contradicts, and what it does. The vocabulary sent to the model is
        /// generated from this same list, so the two can never disagree.
        /// </summary>
        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;

            // --- Mood -----------------------------------------------------------
            Add("end_conversation", "you walk away and refuse to keep talking",
                r => EndConversation(r.Speaker));
            Add("calm_down", "you settle, becoming less alarmed",
                r => ShiftAlertness(r.Speaker, -0.3f), conflicts: "mood",
                aliases: new[] { "calm", "relax" });
            Add("alarm", "you become noticeably more frightened",
                r => ShiftAlertness(r.Speaker, +0.3f), conflicts: "mood",
                aliases: new[] { "panic", "get_scared" });
            Add("answer_door", "you go and open the door",
                r => AnswerDoor(r.Speaker));

            // --- Standing and running -------------------------------------------
            Add("flee", "you turn and run; put a name in target to get away from somebody else",
                r => Flee(r.Speaker, r.Target),
                gate: () => ModConfig.AllowCombatEffects.Value, conflicts: "stance",
                aliases: new[] { "run", "run_away", "escape" });
            Add("attack", "you attack somebody; leave target empty for the investigator, or put a name",
                r => Attack(r.Speaker, r.Target, r.Shouted),
                gate: () => ModConfig.AllowCombatEffects.Value, conflicts: "stance",
                aliases: new[] { "fight", "assault", "strike" });
            Add("surrender", "you stop fighting and give yourself up",
                r => Surrender(r.Speaker),
                gate: () => ModConfig.AllowCombatEffects.Value, conflicts: "stance",
                aliases: new[] { "give_up", "yield" });

            // --- Handing things over --------------------------------------------
            Add("give_item", "you hand over the item you are holding",
                r => GiveHeldItem(r.Speaker),
                gate: () => ModConfig.AllowItemHandover.Value,
                aliases: new[] { "hand_over", "give", "give_object" });
            Add("give_money", "you hand over cash you are carrying; put the amount in target",
                r => WalletReader.GiveMoney(r.Speaker, r.Target),
                gate: () => ModConfig.AllowMoneyHandover.Value,
                aliases: new[] { "give_cash", "pay_them", "hand_over_money" });

            // --- Taking sides ----------------------------------------------------
            Add("side_with_them", "you decide you are on this investigator's side and will back them up",
                r => Allegiance.SideWith(r.Speaker),
                gate: () => ModConfig.AllowAllegiance.Value, conflicts: "allegiance",
                aliases: new[] { "ally", "join_them", "help_them" });
            Add("turn_against_them", "you decide you are against this investigator",
                r => Allegiance.TurnAgainst(r.Speaker),
                gate: () => ModConfig.AllowAllegiance.Value, conflicts: "allegiance",
                aliases: new[] { "oppose_them", "become_hostile" });

            // --- Money for words -------------------------------------------------
            Add("name_a_price", "you will talk, for money; put the amount in target and what for in detail",
                r => Negotiation.Demand_(r.Speaker, r.Target, r.Detail),
                gate: () => ModConfig.AllowNegotiation.Value, conflicts: "deal",
                aliases: new[] { "demand_payment", "ask_for_money", "set_price" });
            Add("take_the_money", "they have agreed to a price you already named, so you take it",
                r => Negotiation.TakePayment(r.Speaker),
                gate: () => ModConfig.AllowNegotiation.Value, conflicts: "deal",
                aliases: new[] { "accept_payment", "take_payment" });

            // --- Coming along -----------------------------------------------------
            Add("follow", "you agree to come along with the investigator",
                r => FollowDirector.Start(r.Speaker),
                gate: () => ModConfig.AllowFollowing.Value, conflicts: "escort",
                aliases: new[] { "follow_them", "come_along", "accompany" });
            Add("stop_following", "you have had enough and stop going with them",
                r => FollowDirector.Stop(r.Speaker),
                gate: () => ModConfig.AllowFollowing.Value, conflicts: "escort",
                aliases: new[] { "leave_them", "stop_follow" });

            // --- The police -------------------------------------------------------
            // Named by direction. An earlier "call_police" read as calling them for help and in
            // fact set them on the investigator, so reporting a mugging got the player held at
            // gunpoint. Nothing here can be read the wrong way round.
            Add("report_the_investigator", "you turn the police on the investigator themselves",
                r => SetOfficerPursuit(r.Speaker, Player.Instance, r.Shouted),
                gate: () => ModConfig.AllowPoliceRedirection.Value, conflicts: "police",
                aliases: new[] { "report_them", "report_the_player" });
            Add("send_police_after", "you set the police on somebody else here; put their name in target",
                r => AccuseOther(r.Speaker, r.Target, r.Shouted),
                gate: () => ModConfig.AllowPoliceRedirection.Value, conflicts: "police",
                aliases: new[] { "accuse" });
            Add("call_police_off", "you call the police off the investigator",
                r => CallOffOfficers(r.Speaker, r.Shouted),
                gate: () => ModConfig.AllowPoliceRedirection.Value, conflicts: "police",
                aliases: new[] { "protect", "vouch_for_them" });

            // Accepted but never offered: only honoured when a target settles the ambiguity.
            Add("call_police", null,
                r => string.IsNullOrWhiteSpace(r.Target)
                    ? "ambiguous - use report_the_investigator or send_police_after"
                    : AccuseOther(r.Speaker, r.Target, r.Shouted),
                gate: () => ModConfig.AllowPoliceRedirection.Value, conflicts: "police");

            // --- What they saw -----------------------------------------------------
            Add("tell_what_i_saw", "you give up where and when you saw someone; put their name in target",
                r => Testimony.RevealSighting(r.Speaker, r.Target),
                gate: () => ModConfig.AllowTestimony.Value,
                aliases: new[] { "testify", "reveal_sighting", "tell_what_i_know" });

            // --- Going somewhere ----------------------------------------------------
            Add("go", "you drop what you were doing and leave; put go_home, go_to_work, go_to_bed or leave in target",
                r => GoalDirector.Send(r.Speaker, r.Target),
                gate: () => ModConfig.AllowGoalRedirection.Value, conflicts: "errand",
                aliases: new[] { "leave", "go_away", "depart" });
            Add("come_and_look", "you go over to see what the fuss is about",
                r => GoalDirector.InvestigateHere(r.Speaker, r.Shouted),
                gate: () => ModConfig.AllowGoalRedirection.Value, conflicts: "errand",
                aliases: new[] { "investigate", "come_over" });

            // --- Everyone who heard --------------------------------------------------
            Add("crowd_panic", "everyone who heard you scatters",
                r => CrowdEffects.Panic(r.Speaker, r.Shouted),
                gate: () => ModConfig.AllowCrowdEffects.Value, conflicts: "crowd");
            Add("crowd_settle", "everyone who heard you calms down",
                r => CrowdEffects.Settle(r.Speaker, r.Shouted),
                gate: () => ModConfig.AllowCrowdEffects.Value, conflicts: "crowd");
            Add("crowd_gather", "everyone who heard you comes over to look",
                r => CrowdEffects.Gather(r.Speaker, r.Shouted),
                gate: () => ModConfig.AllowCrowdEffects.Value, conflicts: "crowd");
        }

        private static void Add(string name, string description, Func<EffectCatalogue.Request, string> run,
                                Func<bool> gate = null, string conflicts = null, string[] aliases = null)
        {
            EffectCatalogue.Register(new EffectCatalogue.Definition
            {
                Name = name,
                Description = description,
                Run = run,
                Enabled = gate ?? (() => true),
                Conflicts = conflicts,
                Aliases = aliases ?? new string[0]
            });
        }

        /// <summary>Effect vocabulary offered to the model, filtered by config.</summary>
        public static IEnumerable<string> PermittedEffectNames()
        {
            if (!ModConfig.EnableWorldEffects.Value) yield break;

            RegisterAll();
            foreach (var definition in EffectCatalogue.Offered())
            {
                yield return definition.Name + " - " + definition.Description;
            }
        }

        /// <summary>Apply a reply's effects. Runs on the main thread.</summary>
        public static EffectReport Apply(Citizen speaker, NpcReply reply, bool shouted)
        {
            var report = new EffectReport();
            if (reply == null) return report;
            if (!IsUsable(speaker))
            {
                report.Reject("everything", "the person is gone");
                return report;
            }

            RegisterAll();
            ApplyRelationship(speaker, reply, report);

            if (!ModConfig.EnableWorldEffects.Value)
            {
                if (reply.Effects != null && reply.Effects.Count > 0)
                    report.Reject("all effects", "world effects are switched off");
                return report;
            }

            ApplyAlarm(speaker, reply, report);

            if (reply.Effects == null) return report;

            var alreadyRun = new HashSet<string>();
            var groupsUsed = new Dictionary<string, string>();

            foreach (var effect in reply.Effects)
            {
                if (effect == null || string.IsNullOrWhiteSpace(effect.Type)) continue;

                var written = effect.Type.Trim();
                var definition = EffectCatalogue.Find(written);

                if (definition == null)
                {
                    report.Reject(written, "not an effect this mod knows");
                    continue;
                }

                if (!definition.Enabled())
                {
                    report.Reject(definition.Name, "switched off in the settings");
                    continue;
                }

                // The same effect twice in one reply is one effect.
                if (!alreadyRun.Add(definition.Name)) continue;

                // Contradictions: the first one asked for wins, so the outcome does not depend
                // on the order a model happened to list them in.
                if (!string.IsNullOrEmpty(definition.Conflicts))
                {
                    string winner;
                    if (groupsUsed.TryGetValue(definition.Conflicts, out winner))
                    {
                        report.Reject(definition.Name, "contradicts " + winner);
                        continue;
                    }
                    groupsUsed[definition.Conflicts] = definition.Name;
                }

                try
                {
                    var refusal = definition.Run(new EffectCatalogue.Request
                    {
                        Speaker = speaker,
                        Target = effect.Target,
                        Detail = effect.Detail,
                        Shouted = shouted
                    });

                    if (refusal == null) report.Applied.Add(definition.Name);
                    else report.Reject(definition.Name, refusal);
                }
                catch (Exception e)
                {
                    report.Reject(definition.Name, "threw: " + e.Message);
                    Plugin.Log.LogWarning("Effect " + definition.Name + " threw: " + e.Message);
                }
            }

            return report;
        }

        /// <summary>
        /// Whether this citizen can still be acted on. A reply can arrive seconds after it was
        /// asked for, by which time the person may have been despawned, and a destroyed object
        /// does not always compare equal to null from managed code.
        /// </summary>
        private static bool IsUsable(Citizen citizen)
        {
            if (citizen == null) return false;
            try
            {
                if (citizen.Pointer == IntPtr.Zero) return false;
                var unused = citizen.humanID;
                return !citizen.isDead;
            }
            catch
            {
                return false;
            }
        }


        // --- Relationship -------------------------------------------------------

        private static void ApplyRelationship(Citizen speaker, NpcReply reply, EffectReport report)
        {
            var d = reply.RelationshipDelta;
            if (d == null) return;

            try
            {
                var player = Player.Instance;
                if (player == null) return;

                Acquaintance acq;
                if (!speaker.FindAcquaintanceExists(player, out acq) || acq == null)
                {
                    // A conversation is itself an introduction.
                    if (Mathf.Abs(d.Known) > 0.001f || Mathf.Abs(d.Like) > 0.001f)
                    {
                        speaker.AddAcquaintance(player, Mathf.Clamp(d.Known, 0f, 0.2f),
                            Acquaintance.ConnectionType.stranger);
                        report.Applied.Add("met the investigator");
                    }
                    return;
                }

                var likeCap = ModConfig.MaxLikeShiftPerLine.Value;
                var likeShift = Mathf.Clamp(d.Like, -likeCap, likeCap);
                if (Mathf.Abs(likeShift) > 0.001f)
                {
                    acq.like = Mathf.Clamp01(acq.like + likeShift);
                    report.Applied.Add("like " + (likeShift > 0 ? "+" : "") + likeShift.ToString("0.00"));
                    if (Mathf.Abs(d.Like) > likeCap + 0.001f)
                        report.Reject("like " + d.Like.ToString("0.00"), "capped at " + likeCap.ToString("0.00"));
                }

                var knownShift = Mathf.Clamp(d.Known, 0f, likeCap);
                if (knownShift > 0.001f)
                {
                    acq.AddKnow(knownShift);
                    report.Applied.Add("known +" + knownShift.ToString("0.00"));
                }
            }
            catch (Exception e)
            {
                report.Reject("relationship", "threw: " + e.Message);
                Plugin.Log.LogWarning("Relationship update failed: " + e.Message);
            }
        }

        private static void ApplyAlarm(Citizen speaker, NpcReply reply, EffectReport report)
        {
            var cap = ModConfig.MaxSuspicionShiftPerLine.Value;
            var target = Mathf.Clamp01(reply.Alarm);
            if (target <= 0.001f) return;

            try
            {
                if (speaker.ai == null) return;
                var current = speaker.ai.alertness;
                var delta = Mathf.Clamp(target - current, -cap, cap);
                if (Mathf.Abs(delta) < 0.01f) return;

                speaker.ai.alertness = Mathf.Clamp01(current + delta);
                if (delta > 0) speaker.ai.TriggerReactionIndicator();
                report.Applied.Add("alertness " + (delta > 0 ? "+" : "") + delta.ToString("0.00"));
            }
            catch (Exception e)
            {
                report.Reject("alarm", "threw: " + e.Message);
                Plugin.Log.LogWarning("Alarm update failed: " + e.Message);
            }
        }

        // --- Behaviour ----------------------------------------------------------

        private static string ShiftAlertness(Citizen speaker, float delta)
        {
            if (speaker.ai == null) return "no AI on this citizen";
            var cap = ModConfig.MaxSuspicionShiftPerLine.Value;
            speaker.ai.alertness = Mathf.Clamp01(speaker.ai.alertness + Mathf.Clamp(delta, -cap, cap));
            return null;
        }

        private static string EndConversation(Citizen speaker)
        {
            try
            {
                if (speaker.ai != null && speaker.ai.currentGoal != null) speaker.ai.currentGoal.Complete();
                speaker.speechController?.SetSpeechActive(false);
                return null;
            }
            catch (Exception e)
            {
                return "could not end the conversation: " + e.Message;
            }
        }

        /// <summary>
        /// Run. The game's flee state has no target - it is a mood, not a direction - so getting
        /// away from a particular person is approximated by fleeing and then heading home, which
        /// is what actually puts distance between them.
        /// </summary>
        private static string Flee(Citizen speaker, string fromWhom)
        {
            if (!ModConfig.AllowCombatEffects.Value) return "fleeing and combat are switched off";
            if (speaker.ai == null) return "no AI on this citizen";
            if (speaker.ai.restrained) return "they are restrained and cannot run";

            speaker.ai.CancelCombat();
            speaker.ai.inFleeState = true;
            speaker.ai.TriggerReactionIndicator();

            if (!string.IsNullOrWhiteSpace(fromWhom))
            {
                // Somewhere to run to, rather than just away. Failure here is not failure of the
                // flee itself, so it is not reported as one.
                GoalDirector.Send(speaker, "go_home");
            }

            return null;
        }

        /// <summary>
        /// Attack the investigator, or somebody else standing there. A named target has to be
        /// present and audible - talking somebody into attacking a person who is not in the
        /// room would be a sentence with nothing behind it.
        /// </summary>
        private static string Attack(Citizen speaker, string targetName, bool shouted)
        {
            if (!ModConfig.AllowCombatEffects.Value) return "fleeing and combat are switched off";
            if (speaker.ai == null) return "no AI on this citizen";
            if (speaker.ai.restrained) return "they are restrained";

            Actor target = Player.Instance;

            if (!string.IsNullOrWhiteSpace(targetName))
            {
                Citizen found = null;
                foreach (var cit in Earshot.CitizensWhoCanHear(speaker, shouted))
                {
                    try
                    {
                        if (cit == null || cit.isPlayer) continue;
                        if (cit.humanID == speaker.humanID) continue;
                        var n = cit.GetCitizenName();
                        if (!string.IsNullOrEmpty(n) &&
                            n.IndexOf(targetName.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found = cit;
                            break;
                        }
                    }
                    catch { }
                }

                if (found == null) return "that person is not here to be attacked";
                target = found;
            }

            if (target == null) return "nobody to attack";

            speaker.ai.SetInCombat(true);
            speaker.ai.StartAttack(target);
            return null;
        }

        private static string Surrender(Citizen speaker)
        {
            if (speaker.ai == null) return "no AI on this citizen";
            if (!speaker.ai.inCombat && !speaker.ai.inFleeState) return "they were not fighting or fleeing";

            speaker.ai.CancelCombat();
            speaker.ai.inFleeState = false;
            return null;
        }

        private static string GiveHeldItem(Citizen speaker)
        {
            if (!ModConfig.AllowItemHandover.Value) return "handing over items is switched off";

            var player = Player.Instance;
            if (player == null) return "no player to give to";

            // Only something they are demonstrably holding. No conjuring items from nothing.
            var item = speaker.rightHandInteractable ?? speaker.leftHandInteractable;
            if (item == null) return "their hands are empty";

            return player.TryGiveItem(item, speaker, true) ? null : "the game refused the handover";
        }

        private static string AnswerDoor(Citizen speaker)
        {
            try
            {
                if (speaker.ai == null) return "no AI on this citizen";
                if (speaker.home == null) return "they have no home";
                if (!speaker.isHome) return "they are not at home";

                var entrances = speaker.home.entrances;
                if (entrances == null || entrances.Count == 0) return "their home has no entrance";

                var door = entrances[0].door;
                if (door == null) return "the entrance has no door";

                speaker.ai.AnswerDoor(door, speaker.currentGameLocation, Player.Instance);
                return null;
            }
            catch (Exception e)
            {
                return "could not answer the door: " + e.Message;
            }
        }

        // --- Law enforcement ----------------------------------------------------

        private static List<Citizen> OfficersInEarshot(Citizen speaker, bool shouted)
        {
            var officers = new List<Citizen>();
            foreach (var cit in Earshot.CitizensWhoCanHear(speaker, shouted))
            {
                try
                {
                    if (cit != null && cit.isEnforcer && !cit.isDead) officers.Add(cit);
                }
                catch { }
            }
            return officers;
        }

        private static string SetOfficerPursuit(Citizen speaker, Actor target, bool shouted)
        {
            if (!ModConfig.AllowPoliceRedirection.Value) return "police redirection is switched off";
            if (target == null) return "nobody to pursue";

            var officers = OfficersInEarshot(speaker, shouted);
            if (officers.Count == 0) return "no officer close enough to hear";

            var any = false;
            foreach (var officer in officers)
            {
                try
                {
                    officer.ai.SetPersue(target, true, 2, true);
                    any = true;
                }
                catch { }
            }
            return any ? null : "the officers refused the order";
        }

        private static string CallOffOfficers(Citizen speaker, bool shouted)
        {
            if (!ModConfig.AllowPoliceRedirection.Value) return "police redirection is switched off";

            var officers = OfficersInEarshot(speaker, shouted);
            if (officers.Count == 0) return "no officer close enough to hear";

            var any = false;
            foreach (var officer in officers)
            {
                try
                {
                    if (officer.ai == null || !officer.ai.persuit) continue;
                    officer.ai.CancelPersue();
                    any = true;
                }
                catch { }
            }
            return any ? null : "no officer was chasing anyone";
        }

        private static string AccuseOther(Citizen speaker, string targetName, bool shouted)
        {
            if (!ModConfig.AllowPoliceRedirection.Value) return "police redirection is switched off";
            if (string.IsNullOrWhiteSpace(targetName)) return "no name given to accuse";

            // The accused has to be someone actually present, not a name plucked from the air.
            Citizen accused = null;
            foreach (var cit in Earshot.CitizensWhoCanHear(speaker, shouted))
            {
                try
                {
                    if (cit == null || cit.isPlayer) continue;
                    var n = cit.GetCitizenName();
                    if (!string.IsNullOrEmpty(n) &&
                        n.IndexOf(targetName.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        accused = cit;
                        break;
                    }
                }
                catch { }
            }

            if (accused == null) return "that person is not here to be accused";
            return SetOfficerPursuit(speaker, accused, shouted);
        }
    }
}
