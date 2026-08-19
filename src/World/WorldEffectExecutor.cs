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

        /// <summary>Effect vocabulary offered to the model, filtered by config.</summary>
        public static IEnumerable<string> PermittedEffectNames()
        {
            if (!ModConfig.EnableWorldEffects.Value) yield break;

            yield return "end_conversation - you walk away and refuse to keep talking";
            yield return "calm_down - you settle, becoming less alarmed";
            yield return "alarm - you become noticeably more frightened";

            if (ModConfig.AllowCombatEffects.Value)
            {
                yield return "flee - you turn and run from the investigator";
                yield return "attack - you attack the investigator";
                yield return "surrender - you stop fighting and give yourself up";
            }

            if (ModConfig.AllowItemHandover.Value)
            {
                yield return "give_item - you hand over the item you are holding";
            }

            if (ModConfig.AllowPoliceRedirection.Value)
            {
                yield return "call_police - nearby officers are told to go after the investigator";
                yield return "protect - nearby officers are called off the investigator";
                yield return "accuse - nearby officers are sent after someone else; put their name in target";
            }

            yield return "answer_door - you go and open the door";

            if (ModConfig.AllowTestimony.Value)
            {
                yield return "tell_what_i_saw - you give up where and when you saw someone; put their name in target";
            }

            if (ModConfig.AllowGoalRedirection.Value)
            {
                yield return "go - you drop what you were doing and leave; put go_home, go_to_work, go_to_bed or leave in target";
                yield return "come_and_look - you go over to see what the fuss is about";
            }

            if (ModConfig.AllowCrowdEffects.Value)
            {
                yield return "crowd_panic - everyone who heard you scatters";
                yield return "crowd_settle - everyone who heard you calms down";
                yield return "crowd_gather - everyone who heard you comes over to look";
            }
        }

        /// <summary>Apply a reply's effects. Runs on the main thread.</summary>
        public static EffectReport Apply(Citizen speaker, NpcReply reply, bool shouted)
        {
            var report = new EffectReport();
            if (speaker == null || reply == null) return report;

            ApplyRelationship(speaker, reply, report);

            if (!ModConfig.EnableWorldEffects.Value)
            {
                if (reply.Effects != null && reply.Effects.Count > 0)
                    report.Reject("all effects", "world effects are switched off");
                return report;
            }

            ApplyAlarm(speaker, reply, report);

            if (reply.Effects == null) return report;

            foreach (var effect in reply.Effects)
            {
                if (effect == null || string.IsNullOrWhiteSpace(effect.Type)) continue;

                var name = effect.Type.Trim().ToLowerInvariant();
                try
                {
                    var refusal = Dispatch(speaker, name, effect, shouted);
                    if (refusal == null) report.Applied.Add(name);
                    else report.Reject(name, refusal);
                }
                catch (Exception e)
                {
                    report.Reject(name, "threw: " + e.Message);
                    Plugin.Log.LogWarning("Effect " + name + " threw: " + e.Message);
                }
            }

            return report;
        }

        /// <summary>Returns null when the effect ran, or a short reason why it did not.</summary>
        private static string Dispatch(Citizen speaker, string name, WorldEffect effect, bool shouted)
        {
            switch (name)
            {
                case "end_conversation": return EndConversation(speaker);
                case "calm_down": return ShiftAlertness(speaker, -0.3f);
                case "alarm": return ShiftAlertness(speaker, +0.3f);
                case "flee": return Flee(speaker);
                case "attack": return Attack(speaker);
                case "surrender": return Surrender(speaker);
                case "give_item": return GiveHeldItem(speaker);
                case "call_police": return SetOfficerPursuit(speaker, Player.Instance, shouted);
                case "protect": return CallOffOfficers(speaker, shouted);
                case "accuse": return AccuseOther(speaker, effect.Target, shouted);
                case "answer_door": return AnswerDoor(speaker);

                case "tell_what_i_saw": return Testimony.RevealSighting(speaker, effect.Target);
                case "go": return GoalDirector.Send(speaker, effect.Target);
                case "come_and_look": return GoalDirector.InvestigateHere(speaker, shouted);

                case "crowd_panic": return CrowdEffects.Panic(speaker, shouted);
                case "crowd_settle": return CrowdEffects.Settle(speaker, shouted);
                case "crowd_gather": return CrowdEffects.Gather(speaker, shouted);

                default: return "not an effect this mod knows";
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

        private static string Flee(Citizen speaker)
        {
            if (!ModConfig.AllowCombatEffects.Value) return "fleeing and combat are switched off";
            if (speaker.ai == null) return "no AI on this citizen";
            if (speaker.ai.restrained) return "they are restrained and cannot run";

            speaker.ai.CancelCombat();
            speaker.ai.inFleeState = true;
            speaker.ai.TriggerReactionIndicator();
            return null;
        }

        private static string Attack(Citizen speaker)
        {
            if (!ModConfig.AllowCombatEffects.Value) return "fleeing and combat are switched off";
            if (speaker.ai == null) return "no AI on this citizen";
            if (speaker.ai.restrained) return "they are restrained";

            var player = Player.Instance;
            if (player == null) return "no player to attack";

            speaker.ai.SetInCombat(true);
            speaker.ai.StartAttack(player);
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
