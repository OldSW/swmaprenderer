namespace SwMapRenderer;

/// <summary>
/// World-space constants, ported verbatim from CentrED (CentrED/Constants.cs) so that the
/// geometry produced here matches the reference renderer tile for tile.
/// </summary>
public static class Constants
{
    // 1 / sqrt(2)
    public const float RSQRT2 = 0.70710678118654752440084436210485f;

    /// <summary>Edge length of one map tile in world units. A tile's art is 44px wide.</summary>
    public const float TILE_SIZE = 44 * RSQRT2;

    /// <summary>World units per unit of tile Z.</summary>
    public const float TILE_Z_SCALE = 4.0f;

    /// <summary>Machine epsilon, used to inset land UVs by half a hair to avoid bleeding.</summary>
    public static readonly float Epsilon = GetMachineEpsilonFloat();

    private static float GetMachineEpsilonFloat()
    {
        float machineEpsilon = 1.0f;
        float comparison;
        do
        {
            machineEpsilon *= 0.5f;
            comparison = 1.0f + machineEpsilon;
        }
        while (comparison > 1.0f);

        return machineEpsilon;
    }
}
