using System;
using System.Collections.Generic;
using LooseLips.Core;

namespace LooseLips.World
{
    /// <summary>
    /// What a citizen is actually carrying, and handing some of it over.
    ///
    /// This exists because of a specific failure seen in a playtest. Asked for money, a
    /// citizen answered "I dislike the investigator and have nothing to give" - and she was
    /// wrong. She had money; the mod simply never told the model, because it only ever looked
    /// at what was in her hands. A model cannot trade away something it does not know exists,
    /// so the refusal was invented to explain an absence the prompt had created.
    ///
    /// Citizens carry money, keys and evidence as <c>walletItems</c>, each a type and an
    /// amount. Money moves for real. Keys and evidence are reported to the model so they can
    /// be talked about and admitted to, but are not conjured into your inventory: a wallet
    /// entry is a record rather than an object, and spawning a physical item from one is a
    /// different job with far more ways to go wrong.
    /// </summary>
    public static class WalletReader
    {
        /// <summary>Plain description of what they carry, for the prompt.</summary>
        public static List<string> Describe(Citizen citizen)
        {
            var lines = new List<string>();
            if (citizen == null) return lines;

            try
            {
                var items = citizen.walletItems;
                if (items == null) return lines;

                var money = 0;
                var keys = 0;
                var evidence = 0;

                foreach (var item in items)
                {
                    if (item == null) continue;
                    switch (item.itemType)
                    {
                        case Human.WalletItemType.money: money += item.money; break;
                        case Human.WalletItemType.key: keys++; break;
                        case Human.WalletItemType.evidence: evidence++; break;
                    }
                }

                if (money > 0) lines.Add("You are carrying $" + money + " in cash.");
                if (keys > 0) lines.Add("You are carrying " + keys + (keys == 1 ? " key." : " keys."));
                if (evidence > 0) lines.Add("You are carrying " + evidence +
                                            (evidence == 1 ? " piece of paperwork." : " pieces of paperwork."));
                if (money <= 0 && keys == 0 && evidence == 0) lines.Add("Your pockets are empty.");
            }
            catch (Exception e)
            {
                if (ModConfig.VerboseLogging.Value)
                    Plugin.Log.LogWarning("Could not read a wallet: " + e.Message);
            }

            return lines;
        }

        /// <summary>How much cash they have. Zero when they have none or it cannot be read.</summary>
        public static int CashOn(Citizen citizen)
        {
            var total = 0;
            try
            {
                var items = citizen?.walletItems;
                if (items == null) return 0;
                foreach (var item in items)
                {
                    if (item != null && item.itemType == Human.WalletItemType.money) total += item.money;
                }
            }
            catch { }
            return total;
        }

        /// <summary>
        /// Hand cash to the player. The amount is whatever the model asked for, clamped to
        /// what they are actually carrying and to the configured ceiling, so a persuasive
        /// line cannot empty a stranger's wallet in one sentence.
        /// </summary>
        public static string GiveMoney(Citizen citizen, string requested)
        {
            if (!ModConfig.AllowMoneyHandover.Value) return "handing over money is switched off";
            if (citizen == null) return "nobody to take it from";

            var carried = CashOn(citizen);
            if (carried <= 0) return "they have no cash on them";

            var wanted = ParseAmount(requested);
            if (wanted <= 0) wanted = carried;

            var cap = ModConfig.MaxMoneyPerLine.Value;
            var amount = Math.Min(Math.Min(wanted, carried), cap);
            if (amount <= 0) return "nothing left to give";

            try
            {
                if (!TakeFromWallet(citizen, amount)) return "their cash could not be taken";

                var gameplay = GameplayController.Instance;
                if (gameplay == null) return "no gameplay controller to receive it";

                gameplay.AddMoney(amount, true, citizen.GetCitizenName() + " handed it over");
                return null;
            }
            catch (Exception e)
            {
                return "the handover threw: " + e.Message;
            }
        }

        private static bool TakeFromWallet(Citizen citizen, int amount)
        {
            var remaining = amount;
            var items = citizen.walletItems;
            if (items == null) return false;

            foreach (var item in items)
            {
                if (remaining <= 0) break;
                if (item == null || item.itemType != Human.WalletItemType.money) continue;
                if (item.money <= 0) continue;

                var taken = Math.Min(item.money, remaining);
                item.money -= taken;
                remaining -= taken;
            }

            return remaining < amount;
        }

        /// <summary>Pull a number out of whatever the model put in the target field.</summary>
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
