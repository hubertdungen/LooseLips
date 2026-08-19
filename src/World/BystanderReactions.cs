using System;
using LooseLips.Core;
using LooseLips.Player2;
using UnityEngine;

namespace LooseLips.World
{
    /// <summary>
    /// Spreads the consequences of an exchange to everyone who overheard it.
    ///
    /// This is what makes shouting worth doing. A quiet word in a hallway moves one
    /// person; a shout in a crowded lobby ripples outward, and how far it ripples is
    /// decided by the room graph in <see cref="Earshot"/> rather than by the model.
    /// </summary>
    public static class BystanderReactions
    {
        public static void Propagate(Citizen speaker, NpcReply reply, bool shouted)
        {
            if (speaker == null || reply == null) return;
            if (!ModConfig.EnableWorldEffects.Value) return;

            // A calm exchange nobody was meant to hear should not disturb the street.
            var intensity = Mathf.Clamp01(reply.Alarm);
            if (!shouted && intensity < 0.4f) return;

            var listeners = Earshot.CitizensWhoCanHear(speaker, shouted);
            if (listeners.Count == 0) return;

            // Shouting carries further but lands softer the further out it goes; this is a
            // flat approximation of that, scaled down so a crowd does not all panic at once.
            var spread = shouted ? intensity * 0.6f : intensity * 0.25f;
            if (spread < 0.02f) return;

            var player = Player.Instance;
            var cap = ModConfig.MaxSuspicionShiftPerLine.Value;

            foreach (var listener in listeners)
            {
                if (listener == null || listener.humanID == speaker.humanID) continue;

                try
                {
                    if (listener.ai == null) continue;

                    var delta = Mathf.Clamp(spread, 0f, cap);
                    listener.ai.alertness = Mathf.Clamp01(listener.ai.alertness + delta);

                    // Loud, alarming exchanges make people look over.
                    if (shouted && intensity >= 0.5f)
                    {
                        listener.ai.TriggerReactionIndicator();

                        if (player != null && listener.currentNode != null)
                        {
                            listener.ai.SetFacingPosition(player.transform.position);
                        }
                    }

                    // Something genuinely alarming shouted in the open draws officers over
                    // to look, without automatically making the player a suspect.
                    if (shouted && intensity >= 0.75f && listener.isEnforcer && listener.isOnDuty &&
                        ModConfig.AllowPoliceRedirection.Value)
                    {
                        if (player != null && player.currentNode != null)
                        {
                            listener.ai.Investigate(
                                player.currentNode,
                                player.transform.position,
                                null,
                                NewAIController.ReactionState.investigatingSound,
                                1f,
                                0,
                                setHighUrgency: false);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (ModConfig.VerboseLogging.Value)
                        Plugin.Log.LogWarning("Bystander reaction failed: " + e.Message);
                }
            }

            if (ModConfig.VerboseLogging.Value)
            {
                Plugin.Log.LogInfo("Overheard by " + listeners.Count + " citizen(s), spread " + spread.ToString("0.00"));
            }
        }
    }
}
