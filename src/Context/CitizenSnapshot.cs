using System.Collections.Generic;

namespace LooseLips.Context
{
    /// <summary>
    /// A plain, engine-free description of one citizen at one moment. Built on the main
    /// thread, then handed to the network thread, so it must not hold any Il2Cpp objects.
    /// </summary>
    public sealed class CitizenSnapshot
    {
        // Identity
        public int CitizenId;
        public string FullName;
        public string CasualName;
        public int Age;
        public string Job;
        public string Employer;
        public string HomeAddress;

        /// <summary>Trait names such as Char-Friendly, Secret-Addict, Principle-LEMSupporter.</summary>
        public List<string> Traits = new List<string>();

        // Relationship with the player
        public bool HasMetPlayer;
        public float Known;          // 0 stranger .. 1 intimate
        public float Like;           // 0 hostile .. 1 devoted
        public List<string> ConnectionsToPlayer = new List<string>();

        // Situation
        public string TimeOfDay;
        public string LocationName;
        public string RoomName;
        public bool AtHome;
        public bool AtWork;
        public bool IsEnforcer;
        public bool IsOnDuty;
        public bool InCombat;
        public bool IsFleeing;
        public bool IsRestrained;
        public float Alertness;      // 0 calm .. 1 alarmed
        public bool PlayerIsTrespassing;
        public bool PlayerIsArmed;
        public string PlayerHeldItem;
        public bool CitizenIsArmed;
        public string CitizenHeldItem;

        /// <summary>Names of other citizens close enough to hear this exchange.</summary>
        public List<string> Bystanders = new List<string>();

        /// <summary>Whether the line was shouted rather than spoken.</summary>
        public bool WasShouted;

        /// <summary>
        /// Facts this citizen genuinely knows. The model is allowed to withhold or distort
        /// these, but never to invent replacements, so the world stays internally consistent.
        /// </summary>
        public List<string> GroundTruth = new List<string>();

        /// <summary>
        /// What the scripted game would have said here. Used as tone guidance only.
        /// </summary>
        public string VanillaLine;

        /// <summary>Effects the executor is currently willing to honour, by name.</summary>
        public List<string> PermittedEffects = new List<string>();

        /// <summary>
        /// People this citizen has genuinely seen, and could therefore testify about. Naming them
        /// in the prompt is what keeps the model from offering evidence about somebody who was
        /// never there - it can only trade on sightings the game actually recorded.
        /// </summary>
        public List<string> CanTestifyAbout = new List<string>();
    }
}
