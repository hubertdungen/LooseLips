using LooseLips.Core;

namespace LooseLips.Dialog
{
    /// <summary>
    /// "Say something..." - opens the free text box, then sends whatever the player typed
    /// to the citizen in front of them at conversational volume.
    /// </summary>
    public sealed class SpeakFreelyPreset : CustomDialogPreset
    {
        public const string PresetName = "Player2_SpeakFreely";

        public SpeakFreelyPreset(string msgID)
        {
            Name = PresetName;
            Preset = NewPreset(PresetName, msgID, ranking: 1);
        }

        public override bool IsAvailable(DialogPreset preset, Citizen saysTo, SideJob jobRef)
        {
            if (saysTo == null) return false;
            if (saysTo.isDead || saysTo.isAsleep || saysTo.isStunned) return false;
            if (ConversationOrchestrator.IsBusy(saysTo)) return false;
            return true;
        }

        public override void RunDialogMethod(DialogController instance, Citizen saysTo,
            Interactable saysToInteractable, NewNode where, Actor saidBy, bool success,
            NewRoom roomRef, SideJob jobRef)
        {
            if (saysTo == null) return;

            // The id, not the object: the player may spend a while typing, and a Citizen
            // reference held across those frames can end up pointing at somebody else.
            var id = saysTo.humanID;

            ChatOverlay.Open(saysTo, shouted: false, onSubmit: line =>
            {
                var who = Humans.Resolve(id);
                if (who == null) return;

                ConversationOrchestrator.Speak(who, line, shouted: false,
                    vanillaLine: VanillaLineCapture.TakeLastFor(who));
            });
        }
    }

    /// <summary>
    /// "Shout..." - the same thing at volume. Carries much further, is heard by
    /// bystanders, and reads as aggressive to the citizen on the receiving end.
    /// </summary>
    public sealed class ShoutPreset : CustomDialogPreset
    {
        public const string PresetName = "Player2_Shout";

        public ShoutPreset(string msgID)
        {
            Name = PresetName;
            Preset = NewPreset(PresetName, msgID, ranking: 2);
        }

        public override bool IsAvailable(DialogPreset preset, Citizen saysTo, SideJob jobRef)
        {
            if (saysTo == null) return false;
            if (saysTo.isDead) return false;
            // Shouting works on someone asleep - that is rather the point.
            if (ConversationOrchestrator.IsBusy(saysTo)) return false;
            return true;
        }

        public override void RunDialogMethod(DialogController instance, Citizen saysTo,
            Interactable saysToInteractable, NewNode where, Actor saidBy, bool success,
            NewRoom roomRef, SideJob jobRef)
        {
            if (saysTo == null) return;

            // The id, not the object: the player may spend a while typing, and a Citizen
            // reference held across those frames can end up pointing at somebody else.
            var id = saysTo.humanID;

            ChatOverlay.Open(saysTo, shouted: true, onSubmit: line =>
            {
                var who = Humans.Resolve(id);
                if (who == null) return;

                ConversationOrchestrator.Speak(who, line, shouted: true,
                    vanillaLine: VanillaLineCapture.TakeLastFor(who));
            });
        }
    }
}
