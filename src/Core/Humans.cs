namespace LooseLips.Core
{
    /// <summary>
    /// Looking a citizen up again by id, which is the only safe way to refer to one across
    /// frames.
    ///
    /// The game pools and reuses <c>Citizen</c> objects, so a reference kept from one frame
    /// to the next eventually drives a different person. It does not look like a stale
    /// pointer when it happens: the citizen you were talking to visibly becomes a stranger,
    /// and every effect the conversation earned lands on them instead. A conversation is
    /// several seconds of typing plus several more waiting on the model, which is long
    /// enough for exactly that.
    ///
    /// Hold <c>humanID</c>, resolve it again at the moment of use, and treat null as "they
    /// are no longer around" rather than as an error.
    /// </summary>
    public static class Humans
    {
        /// <summary>The citizen with this id, or null when they are gone.</summary>
        public static Citizen Resolve(int id)
        {
            try
            {
                var city = CityData.Instance;
                if (city == null) return null;

                Human human;
                if (!city.GetHuman(id, out human, false) || human == null) return null;
                return human.TryCast<Citizen>();
            }
            catch
            {
                return null;
            }
        }
    }
}
