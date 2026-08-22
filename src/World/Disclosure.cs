using System;
using LooseLips.Core;

namespace LooseLips.World
{
    /// <summary>
    /// A detail a citizen gives up about themselves, put where the game can see it.
    ///
    /// Until this existed, the only thing a conversation could leave on the case board was a
    /// sighting, through <see cref="Testimony"/>. Everything else - where they live, who they
    /// work for, who they are married to - was said out loud, written to our own transcript,
    /// and then existed nowhere the game knew about. The first playtest read that as the mod
    /// not working: eight people answered questions and the case panel stayed empty.
    ///
    /// The same rule applies here as everywhere else. The model does not get to write a fact;
    /// it only decides whether the citizen is willing to part with one. What gets filed is the
    /// game's own evidence entry for that person, under the key they actually have - so a
    /// citizen with no job cannot disclose an employer, however confidently the model asks.
    /// </summary>
    public static class Disclosure
    {
        /// <summary>
        /// File a detail about <paramref name="citizen"/> under the open case.
        /// Returns null on success, or the reason it could not happen.
        /// </summary>
        public static string Reveal(Citizen citizen, string what)
        {
            if (!ModConfig.AllowDisclosure.Value) return "filing details is switched off";
            if (citizen == null) return "nobody to file";

            Evidence.DataKey key;
            var problem = Resolve(citizen, what, out key);
            if (problem != null) return problem;

            try
            {
                var panel = CasePanelController.Instance;
                if (panel == null) return "the case panel is not up yet";

                var openCase = panel.activeCase;
                if (openCase == null) return "there is no case open to file it under";

                var interactable = citizen.interactable;
                var evidence = interactable != null ? interactable.evidence : null;
                if (evidence == null) return "the game keeps no evidence entry for them";

                panel.PinToCasePanel(openCase, evidence, key, false, default);

                SessionLog.Note(citizen.GetCitizenName() + " gave up their " + Describe(key)
                                + ", filed under " + openCase.name + ".");
                return null;
            }
            catch (Exception e)
            {
                return "the game refused to file it: " + e.Message;
            }
        }

        /// <summary>What this citizen could credibly disclose, for the prompt.</summary>
        public static System.Collections.Generic.List<string> PossibleDetails(Citizen citizen)
        {
            var details = new System.Collections.Generic.List<string>();
            if (citizen == null) return details;

            try
            {
                details.Add("name");
                if (citizen.home != null) details.Add("address");
                if (citizen.job != null) details.Add("job");
                if (citizen.job != null && citizen.job.employer != null) details.Add("workplace");
                if (citizen.partner != null) details.Add("partner");
            }
            catch (Exception e)
            {
                if (ModConfig.VerboseLogging.Value)
                    Plugin.Log.LogWarning("Could not list what they could disclose: " + e.Message);
            }

            return details;
        }

        /// <summary>
        /// Turn what the model asked for into a key this citizen genuinely has. Refusing the
        /// ones they do not is the whole point: a fact filed about somebody's employer when
        /// they have no job is exactly the invented lead this mod is built not to produce.
        /// </summary>
        private static string Resolve(Citizen citizen, string what, out Evidence.DataKey key)
        {
            key = Evidence.DataKey.name;

            var wanted = (what ?? string.Empty).Trim().ToLowerInvariant();
            if (wanted.Length == 0) return "they did not say which detail";

            try
            {
                if (wanted.Contains("name") && !wanted.Contains("partner"))
                {
                    key = Evidence.DataKey.name;
                    return null;
                }

                if (wanted.Contains("address") || wanted.Contains("home") || wanted.Contains("live"))
                {
                    if (citizen.home == null) return "they have nowhere the game calls home";
                    key = Evidence.DataKey.address;
                    return null;
                }

                if (wanted.Contains("workplace") || wanted.Contains("employer"))
                {
                    if (citizen.job == null || citizen.job.employer == null)
                        return "they do not work anywhere";
                    key = Evidence.DataKey.work;
                    return null;
                }

                if (wanted.Contains("job") || wanted.Contains("work") || wanted.Contains("title"))
                {
                    if (citizen.job == null) return "they have no job";
                    key = Evidence.DataKey.jobTitle;
                    return null;
                }

                if (wanted.Contains("partner") || wanted.Contains("spouse") || wanted.Contains("married"))
                {
                    if (citizen.partner == null) return "they have no partner";
                    key = Evidence.DataKey.partnerFirstName;
                    return null;
                }

                if (wanted.Contains("phone") || wanted.Contains("telephone") || wanted.Contains("number"))
                {
                    key = Evidence.DataKey.telephoneNumber;
                    return null;
                }
            }
            catch (Exception e)
            {
                return "checking what they have threw: " + e.Message;
            }

            return "there is no such detail to file";
        }

        private static string Describe(Evidence.DataKey key)
        {
            switch (key)
            {
                case Evidence.DataKey.address: return "address";
                case Evidence.DataKey.work: return "workplace";
                case Evidence.DataKey.jobTitle: return "job";
                case Evidence.DataKey.partnerFirstName: return "partner";
                case Evidence.DataKey.telephoneNumber: return "telephone number";
                default: return "name";
            }
        }
    }
}
