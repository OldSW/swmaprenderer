namespace SwMapRenderer.Map;

/// <summary>Where an item is worn. The values are the ones the server sends.</summary>
public enum Layer
{
    Invalid = 0x00,
    OneHanded = 0x01,
    TwoHanded = 0x02,
    Shoes = 0x03,
    Pants = 0x04,
    Shirt = 0x05,
    Helmet = 0x06,
    Gloves = 0x07,
    Ring = 0x08,
    Talisman = 0x09,
    Necklace = 0x0A,
    Hair = 0x0B,
    Waist = 0x0C,
    Torso = 0x0D,
    Bracelet = 0x0E,
    Face = 0x0F,
    Beard = 0x10,
    Tunic = 0x11,
    Earrings = 0x12,
    Arms = 0x13,
    Cloak = 0x14,
    Backpack = 0x15,
    Robe = 0x16,
    Skirt = 0x17,
    Legs = 0x18,
}

/// <summary>
/// The order a dressed body's layers are painted in.
///
/// Equipment is drawn as a stack of animation frames over the naked body, so the order is the
/// whole of the result: get it wrong and a shirt covers the breastplate over it. There is no
/// rule to derive it from -- the client carries a table, picks between three variants on what
/// is worn, and then shuffles individual layers for a handful of specific graphics whose art
/// was drawn out of order. Cloaks move by facing instead, since a cloak hangs behind a body
/// walking towards you and in front of one walking away.
///
/// Ported from ClassicUO's PaperdollOrder (BSD-2-Clause), which reproduces the table and the
/// reorder rules of the official client.
/// </summary>
public static class PaperdollOrder
{
    /// <summary>Layers scanned by the reorder rules: 0x00 (the sentinel) through 0x18.</summary>
    public const int LayerCount = 0x19;

    /// <summary>Arms drawn late.</summary>
    private static readonly Layer[] ArmsLate =
    [
        Layer.Invalid, Layer.Cloak, Layer.Shirt, Layer.Pants, Layer.Shoes, Layer.Legs,
        Layer.Torso, Layer.Tunic, Layer.Ring, Layer.Bracelet, Layer.Face, Layer.Arms,
        Layer.Gloves, Layer.Skirt, Layer.Robe, Layer.Waist, Layer.Necklace, Layer.Hair,
        Layer.Beard, Layer.Earrings, Layer.Helmet, Layer.OneHanded, Layer.TwoHanded,
        Layer.Backpack, Layer.Talisman,
    ];

    /// <summary>The default: arms drawn early.</summary>
    private static readonly Layer[] Default =
    [
        Layer.Invalid, Layer.Cloak, Layer.Shirt, Layer.Pants, Layer.Shoes, Layer.Legs,
        Layer.Arms, Layer.Torso, Layer.Tunic, Layer.Ring, Layer.Bracelet, Layer.Face,
        Layer.Gloves, Layer.Skirt, Layer.Robe, Layer.Waist, Layer.Necklace, Layer.Hair,
        Layer.Beard, Layer.Earrings, Layer.Helmet, Layer.OneHanded, Layer.TwoHanded,
        Layer.Backpack, Layer.Talisman,
    ];

    /// <summary>Torso pulled to the front, for the female and gargoyle chest pieces.</summary>
    private static readonly Layer[] TorsoFirst =
    [
        Layer.Invalid, Layer.Cloak, Layer.Torso, Layer.Shirt, Layer.Pants, Layer.Shoes,
        Layer.Legs, Layer.Tunic, Layer.Ring, Layer.Bracelet, Layer.Face, Layer.Arms,
        Layer.Gloves, Layer.Skirt, Layer.Robe, Layer.Waist, Layer.Necklace, Layer.Hair,
        Layer.Beard, Layer.Earrings, Layer.Helmet, Layer.OneHanded, Layer.TwoHanded,
        Layer.Backpack, Layer.Talisman,
    ];

    /// <summary>
    /// The layers to paint, back to front.
    /// </summary>
    /// <param name="animIds">
    /// Each layer's equipment animation id, indexed by <see cref="Layer"/>; zero where nothing
    /// is worn. The rules key on these rather than on the world graphic, because two items that
    /// look different on the ground can share the art worn.
    /// </param>
    /// <param name="altTorsoTable">Set for female and gargoyle bodies.</param>
    /// <param name="direction">The facing on the wire, which is what moves the cloak.</param>
    /// <param name="destination">Receives the order; must hold <see cref="LayerCount"/> entries.</param>
    /// <returns>How many entries were written.</returns>
    public static int BuildInWorld(ReadOnlySpan<ushort> animIds, bool altTorsoTable, int direction,
        Span<Layer> destination)
    {
        Span<Layer> order = stackalloc Layer[LayerCount];
        Build(animIds, altTorsoTable, order);

        int count = Filter(order, destination);
        return MoveCloakForDirection(destination, count, direction);
    }

    private static void Build(ReadOnlySpan<ushort> animIds, bool altTorsoTable, Span<Layer> order)
    {
        // An alternate arms graphic settles the table on its own; otherwise the torso decides.
        uint arms = animIds[(int)Layer.Arms];
        bool armsAlternate = arms < 0x3D0
            ? arms is 0x3CF or 0x210 or 0x3B3
            : arms == 0x3DD;

        Layer[] table;

        if (armsAlternate)
        {
            table = ArmsLate;
        }
        else
        {
            uint torso = animIds[(int)Layer.Torso];
            table = torso == 0x21A ? ArmsLate
                : torso - 0x399 < 5 && altTorsoTable ? TorsoFirst
                : Default;
        }

        table.AsSpan(0, LayerCount).CopyTo(order);

        // Shirt worn under one particular pair of leggings: the leggings go under the shirt.
        if (animIds[(int)Layer.Shirt] != 0 && animIds[(int)Layer.Pants] == 0x398)
            MoveTo(order, Layer.Pants, Layer.Shirt);

        uint pants = animIds[(int)Layer.Pants];
        bool pantsSettled = false;

        if (pants < 0x201)
        {
            if (pants is 0x200 or 0x1EB or 0x1FA)
            {
                // These tuck into the boots, so the boots have to be down first.
                int shoes = IndexOf(order, Layer.Shoes);
                int index = IndexOf(order, Layer.Pants);

                if (shoes >= 0 && index >= 0 && index < shoes)
                {
                    order[shoes] = Layer.Pants;
                    order[index] = Layer.Shoes;
                }
            }
        }
        else if (pants - 0x513u < 2)
        {
            if (animIds[(int)Layer.Shoes] != 0)
                MoveTo(order, Layer.Pants, Layer.Shoes);

            pantsSettled = true;
        }

        if (!pantsSettled && animIds[(int)Layer.Shoes] != 0 && animIds[(int)Layer.Pants] == 0x3E4)
            MoveTo(order, Layer.Pants, Layer.Shoes);

        // The surcoat hangs over its belt, and over a robe from the matching set.
        if (animIds[(int)Layer.Tunic] == 0x238)
        {
            MoveAfter(order, Layer.Tunic, Layer.Waist);

            uint robe = animIds[(int)Layer.Robe];
            if (robe is 0x4E8 or 0x4E9 or 0x4EA or 0x4EB or 0x5E2 or 0x5E3 or 0x5E4 or 0x5E5)
                MoveTo(order, Layer.Robe, Layer.Necklace);
        }

        // A quiver is worn on the cloak layer but sits over a robe rather than under it.
        uint cloak = animIds[(int)Layer.Cloak];
        if (cloak is 0x380 or 0x5F3)
            MoveAfter(order, Layer.Cloak, Layer.Robe);

        uint helm = animIds[(int)Layer.Helmet];

        if (helm < 0x202)
        {
            uint neck = animIds[(int)Layer.Necklace];

            if (helm is 0x201 or 0x1A9 && (neck == 0x1C8 || neck is > 0x1D6 and < 0x1D9))
                MoveAfter(order, Layer.Necklace, Layer.Helmet);
        }
        else if (helm - 0x5E9u < 2 && animIds[(int)Layer.Robe] - 0x5E2u < 4)
        {
            MoveTo(order, Layer.Robe, Layer.Helmet);
        }
    }

    /// <summary>Drops the sentinel and the layers that are never drawn on the body.</summary>
    private static int Filter(ReadOnlySpan<Layer> order, Span<Layer> destination)
    {
        int count = 0;

        foreach (var layer in order)
        {
            if (layer is Layer.Invalid or Layer.Backpack)
                continue;

            destination[count++] = layer;
        }

        return count;
    }

    /// <summary>
    /// Puts the cloak where the facing wants it: behind a body walking towards the viewer, over
    /// one walking away, and tucked under the helmet the rest of the time.
    /// </summary>
    private static int MoveCloakForDirection(Span<Layer> layers, int count, int direction)
    {
        int index = IndexOf(layers[..count], Layer.Cloak);
        if (index < 0)
            return count;

        for (int i = index; i < count - 1; i++)
            layers[i] = layers[i + 1];

        count--;

        int insert;

        if (direction == 0)
        {
            insert = count;
        }
        else if (direction == 3)
        {
            insert = 0;
        }
        else
        {
            insert = IndexOf(layers[..count], Layer.Helmet);
            if (insert < 0)
                insert = count;
        }

        for (int i = count; i > insert; i--)
            layers[i] = layers[i - 1];

        layers[insert] = Layer.Cloak;
        return count + 1;
    }

    private static int IndexOf(ReadOnlySpan<Layer> layers, Layer layer)
    {
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] == layer)
                return i;
        }

        return -1;
    }

    /// <summary>Moves <paramref name="layer"/> onto <paramref name="target"/>'s slot.</summary>
    private static void MoveTo(Span<Layer> layers, Layer layer, Layer target)
    {
        int from = IndexOf(layers, layer);
        int to = IndexOf(layers, target);

        if (from < 0 || to < 0 || to == from)
            return;

        if (to < from)
        {
            for (int i = from; i > to; i--)
                layers[i] = layers[i - 1];
        }
        else
        {
            for (int i = from; i < to; i++)
                layers[i] = layers[i + 1];
        }

        layers[to] = layer;
    }

    /// <summary>Moves <paramref name="layer"/> to just after <paramref name="target"/>.</summary>
    private static void MoveAfter(Span<Layer> layers, Layer layer, Layer target)
    {
        int from = IndexOf(layers, layer);
        int to = IndexOf(layers, target);

        if (from < 0 || to < 0 || to == from)
            return;

        if (to < from)
        {
            for (int i = from; i > to + 1; i--)
                layers[i] = layers[i - 1];

            layers[to + 1] = layer;
        }
        else
        {
            for (int i = from; i < to; i++)
                layers[i] = layers[i + 1];

            layers[to] = layer;
        }
    }
}
