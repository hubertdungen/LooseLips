using UnityEngine;

namespace LooseLips.Dialog
{
    /// <summary>
    /// Base for a dialogue option this mod adds.
    ///
    /// The game has no extension point for new options, so the established approach in
    /// the modding community is to register a DialogPreset and route its behaviour
    /// through an existing DialogController method that we intercept. See
    /// <see cref="DialogRegistry"/> for that plumbing.
    /// </summary>
    public abstract class CustomDialogPreset
    {
        public string Name { get; protected set; }
        public DialogPreset Preset { get; protected set; }

        /// <summary>Whether this option should appear for the given citizen right now.</summary>
        public abstract bool IsAvailable(DialogPreset preset, Citizen saysTo, SideJob jobRef);

        /// <summary>Runs when the player picks the option.</summary>
        public abstract void RunDialogMethod(DialogController instance, Citizen saysTo,
            Interactable saysToInteractable, NewNode where, Actor saidBy, bool success,
            NewRoom roomRef, SideJob jobRef);

        /// <summary>Lets the option decide its own success instead of rolling against traits.</summary>
        public virtual DialogController.ForceSuccess ShouldDialogSucceedOverride(DialogController instance,
            EvidenceWitness.DialogOption dialog, Citizen saysTo, NewNode where, Actor saidBy)
            => DialogController.ForceSuccess.none;

        protected static DialogPreset NewPreset(string name, string msgID, int ranking)
        {
            var preset = ScriptableObject.CreateInstance<DialogPreset>();

            // Unity destroys a ScriptableObject nobody owns when the scene changes, and the
            // game changes scene often. The game's own dialogue lists go on holding the dead
            // reference, so the option is still drawn and still clickable - it simply no longer
            // resolves to anything, which reads in play as "Say something... does nothing".
            // Worse, a mod that walks the option list and reads preset.msgID without a null
            // check takes a NullReferenceException mid-loop and leaves every option it had
            // already hidden still hidden. HideAndDontSave keeps the preset alive for the
            // process and out of the save file.
            preset.hideFlags = HideFlags.HideAndDontSave;

            preset.name = name;
            preset.msgID = msgID;
            preset.defaultOption = true;             // offered to everyone, not tied to a case
            preset.tiedToKey = Evidence.DataKey.voice;
            preset.ranking = ranking;
            preset.removeAfterSaying = false;        // you can keep talking
            preset.useSuccessTest = false;           // the model decides how it lands, not a dice roll
            preset.baseChance = 1f;
            preset.affectChanceIfRestrained = 0f;
            preset.specialCase = DialogPreset.SpecialCase.none;
            return preset;
        }
    }
}
