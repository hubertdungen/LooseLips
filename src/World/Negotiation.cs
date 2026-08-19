using System;
using System.Collections.Generic;
using LooseLips.Core;

namespace LooseLips.World
{
    /// <summary>
    /// Money going the other way, so a conversation can be a transaction rather than a plea.
    ///
    /// Everything else in this mod moves things from a citizen towards you. Buying information
    /// needs the opposite, and it needs two turns to be worth anything: they name a price, you
    /// answer, and only then does anyone's wallet open. A single turn that both agreed a price
    /// and paid it would let a model invent a debt and settle it in the same breath.
    ///
    /// So a demand is remembered, and payment is only accepted against one that was actually
    /// made, at the price that was actually named, if you genuinely have the money.
    /// </summary>
    public static class Negotiation
    {
        private sealed class Demand
        {
            public int Amount;
            public string For;
            public float MadeAt;
        }

        private static readonly Dictionary<int, Demand> Outstanding = new Dictionary<int, Demand>();

        /// <summary>What they are asking you for, if anything. For the prompt.</summary>
        public static string PendingFor(Citizen citizen)
        {
            if (citizen == null) return null;

            Demand demand;
            if (!Outstanding.TryGetValue(citizen.humanID, out demand)) return null;

            if (UnityEngine.Time.time - demand.MadeAt > ModConfig.DemandExpiry.Value)
            {
                Outstanding.Remove(citizen.humanID);
                return null;
            }

            return "You have asked this investigator for $" + demand.Amount +
                   (string.IsNullOrWhiteSpace(demand.For) ? "" : " in return for " + demand.For) +
                   ", and they have not paid yet.";
        }

        /// <summary>Name a price. Returns null when the demand stands, or a reason.</summary>
        public static string Demand_(Citizen citizen, string amountText, string forWhat)
        {
            if (!ModConfig.AllowNegotiation.Value) return "haggling is switched off";
            if (citizen == null) return "nobody to ask";

            var amount = ParseAmount(amountText);
            if (amount <= 0) return "no price named";

            var cap = ModConfig.MaxDemand.Value;
            if (amount > cap) amount = cap;

            Outstanding[citizen.humanID] = new Demand
            {
                Amount = amount,
                For = forWhat,
                MadeAt = UnityEngine.Time.time
            };
            return null;
        }

        /// <summary>
        /// Take payment for a demand already made. Refuses when nothing was agreed, or when the
        /// investigator cannot actually cover it.
        /// </summary>
        public static string TakePayment(Citizen citizen)
        {
            if (!ModConfig.AllowNegotiation.Value) return "haggling is switched off";
            if (citizen == null) return "nobody to pay";

            Demand demand;
            if (!Outstanding.TryGetValue(citizen.humanID, out demand))
                return "they never named a price, so there is nothing to settle";

            try
            {
                var gameplay = GameplayController.Instance;
                if (gameplay == null) return "no gameplay controller to take it from";

                if (gameplay.money < demand.Amount)
                    return "the investigator does not have $" + demand.Amount;

                gameplay.AddMoney(-demand.Amount, true, "paid to " + citizen.GetCitizenName());

                // The money lands in their pocket rather than evaporating.
                try
                {
                    var wallet = citizen.walletItems;
                    if (wallet != null)
                    {
                        var placed = false;
                        foreach (var item in wallet)
                        {
                            if (item == null || item.itemType != Human.WalletItemType.money) continue;
                            item.money += demand.Amount;
                            placed = true;
                            break;
                        }
                        if (!placed && ModConfig.VerboseLogging.Value)
                            Plugin.Log.LogInfo("Paid " + citizen.GetCitizenName() +
                                               " but they had no cash entry to add it to.");
                    }
                }
                catch { }

                Outstanding.Remove(citizen.humanID);

                // Being paid what was asked buys real goodwill, over and above the words.
                try
                {
                    var player = Player.Instance;
                    Acquaintance acq;
                    if (player != null && citizen.FindAcquaintanceExists(player, out acq) && acq != null)
                        acq.like = UnityEngine.Mathf.Clamp01(acq.like + ModConfig.PaymentGoodwill.Value);
                }
                catch { }

                SessionLog.Note("Paid " + citizen.GetCitizenName() + " $" + demand.Amount +
                                (string.IsNullOrWhiteSpace(demand.For) ? "" : " for " + demand.For) + ".");
                return null;
            }
            catch (Exception e)
            {
                return "the payment threw: " + e.Message;
            }
        }

        public static void Clear() => Outstanding.Clear();

        private static int ParseAmount(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            var digits = "";
            foreach (var c in text)
            {
                if (c >= '0' && c <= '9') digits += c;
                else if (digits.Length > 0) break;
            }

            int value;
            return int.TryParse(digits, out value) ? value : 0;
        }
    }
}
