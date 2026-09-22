using Server.Items;
using Server.Multis;
using System;
using System.IO;
using System.Text.Json;

/*******************************************************************************
 * ExportItemsMobilesCommand
 * *****************************************************************************
 * writes all visible items not in any container as well as visible mobiles from
 * Map 1 into JSON files
 * ****************************************************************************/
namespace Server.Commands;

public class ExportItemsMobilesCommand : IBootConfigure, IBootInitialize
{
    private const string _ItemFileName = "items.json";
    private const string _MobileFileName = "mobiles.json";

    [Boot]
    public static void Configure()
    {
        // Initialize() runs after World.Load(), so the handler has to be registered during Configure().
        EventSink.WorldLoad += OnWorldLoad;
    }

    [Boot]
    public static void Initialize()
    {
        CommandSystem.Register("exportitemsmobiles", AccessLevel.Administrator, ExportItemsMobiles_OnCommand);
    }

    public static void OnWorldLoad()
    {
        Console.Write("ExportItemsMobiles: exporting locked down items ...");
        var items = ExportItems(out var itemPath);
        Console.WriteLine(items < 0 ? "failed." : $"done. ({items} items to {itemPath})");

        Console.Write("ExportItemsMobiles: exporting mobiles ...");
        var mobiles = ExportMobiles(out var mobilePath);
        Console.WriteLine(mobiles < 0 ? "failed." : $"done. ({mobiles} mobiles to {mobilePath})");
    }

    [CommandUsage("ExportItemsMobiles")]
    [Description("Exports all visible and locked down items and all visible mobiles located on the Schattenwelt map into json files.")]
    public static void ExportItemsMobiles_OnCommand(CommandEventArgs e)
    {
        var items = ExportItems(out var itemPath);
        e.Mobile.SendMessage(items < 0 ? $"Error while exporting to {itemPath}, see the console log for details." : $"{items} items have been exported to {itemPath}");

        var mobiles = ExportMobiles(out var mobilePath);
        e.Mobile.SendMessage(mobiles < 0 ? $"Error while exporting to {mobilePath}, see the console log for details." : $"{mobiles} mobiles have been exported to {mobilePath}");
    }

    /// <summary>
    /// Writes all visible and locked down items on the map into a json file.
    /// </summary>
    /// <param name="path">The path of the json file.</param>
    /// <returns>The number of exported items, or -1 if the export failed.</returns>
    private static int ExportItems(out string path)
    {
        return Export(_ItemFileName, out path, writer =>
        {
            var items = 0;

            foreach (Item item in World.Items.Values)
            {
                if (item.Deleted || item.Map != Map.Trammel || !item.Visible || item.Parent != null)
                    continue;

                writer.WriteStartObject();
                writer.WriteNumber("itemId", item.ItemID);
                writer.WriteNumber("hue", item.Hue);
                writer.WriteString("name", GetName(item));
                writer.WriteString("type", item.GetType().Name);
                writer.WriteBoolean("multi", IsMulti(item));
                writer.WriteStartObject("position");
                writer.WriteNumber("x", item.X);
                writer.WriteNumber("y", item.Y);
                writer.WriteNumber("z", item.Z);
                writer.WriteEndObject();

                if (item is HouseFoundation foundation)
                    WriteDesign(writer, foundation);

                writer.WriteEndObject();
                items++;
            }

            return items;
        });
    }

    /// <summary>
    /// Writes all visible non player mobiles on the Schattenwelt map into a json file. Mobiles with a
    /// human body additionally carry the items on their wearing layers.
    /// </summary>
    /// <param name="path">The path of the json file.</param>
    /// <returns>The number of exported mobiles, or -1 if the export failed.</returns>
    private static int ExportMobiles(out string path)
    {
        return Export(_MobileFileName, out path, writer =>
        {
            var mobiles = 0;

            foreach (Mobile mobile in World.Mobiles.Values)
            {
                // player mobiles stay in the world after logout, exporting them would leak their whereabouts
                if (mobile.Deleted || mobile.Map != Map.Trammel || mobile.Hidden || mobile.Player)
                    continue;

                writer.WriteStartObject();
                writer.WriteNumber("body", mobile.Body.BodyID);
                writer.WriteNumber("hue", mobile.Hue);
                writer.WriteString("name", mobile.Name ?? "");
                writer.WriteString("title", mobile.Title ?? "");
                writer.WriteString("type", mobile.GetType().Name);
                writer.WriteBoolean("female", mobile.Female);
                writer.WriteNumber("direction", (int)(mobile.Direction & Direction.Mask));
                writer.WriteStartObject("position");
                writer.WriteNumber("x", mobile.X);
                writer.WriteNumber("y", mobile.Y);
                writer.WriteNumber("z", mobile.Z);
                writer.WriteEndObject();

                if (mobile.Body.IsHuman)
                    WriteEquipment(writer, mobile);

                writer.WriteEndObject();
                mobiles++;
            }

            return mobiles;
        });
    }

    /// <summary>
    /// Writes the items on the wearing layers of a mobile, hair and beard included. Layers the client
    /// never draws on the mobile itself (backpack, bank box, vendor packs, mount) are left out.
    /// </summary>
    private static void WriteEquipment(Utf8JsonWriter writer, Mobile mobile)
    {
        writer.WriteStartArray("equipment");

        foreach (Item item in mobile.Items)
        {
            if (item.Deleted || item.Layer == Layer.Backpack || item.Layer < Layer.FirstValid || item.Layer > Layer.LastUserValid)
                continue;

            writer.WriteStartObject();
            writer.WriteNumber("itemId", item.ItemID);
            writer.WriteNumber("hue", item.Hue);
            writer.WriteString("name", GetName(item));
            writer.WriteString("type", item.GetType().Name);
            writer.WriteString("layer", item.Layer.ToString());
            writer.WriteNumber("layerId", (int)item.Layer);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes the currently visible design of a customizable house. The tiles replace the ones of the
    /// foundation multi, their positions are absolute so that they can be drawn without the foundation.
    /// </summary>
    private static void WriteDesign(Utf8JsonWriter writer, HouseFoundation foundation)
    {
        var state = foundation.CurrentState;
        var components = state.Components;

        writer.WriteStartObject("design");
        writer.WriteNumber("revision", state.Revision);
        writer.WriteNumber("width", components.Width);
        writer.WriteNumber("height", components.Height);
        writer.WriteStartArray("tiles");

        var list = components.List;
        for (var i = 0; i < list.Length; i++)
        {
            // the client skips tiles without flags, only the first entry (the foundation itself) is always drawn
            if (i != 0 && list[i].m_Flags == 0)
                continue;

            writer.WriteStartObject();
            // the design holds static tiles, the multi bit is only set internally and is masked out for the client as well
            writer.WriteNumber("itemId", list[i].m_ItemID & 0x3FFF);
            writer.WriteNumber("x", foundation.X + list[i].m_OffsetX);
            writer.WriteNumber("y", foundation.Y + list[i].m_OffsetY);
            writer.WriteNumber("z", foundation.Z + list[i].m_OffsetZ);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a json array into a file in the web directory.
    /// </summary>
    /// <param name="fileName">The name of the json file.</param>
    /// <param name="path">The path of the json file.</param>
    /// <param name="writeEntries">Writes the entries of the array and returns their number.</param>
    /// <returns>The number of written entries, or -1 if the export failed.</returns>
    private static int Export(string fileName, out string path, Func<Utf8JsonWriter, int> writeEntries)
    {
        path = Path.Combine(Core.WebPath, fileName);

        try
        {
            if (!Directory.Exists(Core.WebPath))
                Directory.CreateDirectory(Core.WebPath);

            int entries;

            using (var filestream = File.Open(path, FileMode.Create))
            using (Utf8JsonWriter writer = new(filestream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartArray();
                entries = writeEntries(writer);
                writer.WriteEndArray();
            }

            return entries;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ExportItemsMobiles: error while exporting to {path}: {ex}");
            return -1;
        }
    }

    private static bool IsMulti(Item item)
    {
        // multi tiles start at 0x4000, but not every multi is a BaseMulti (e.g. boats) and vice versa
        return item is BaseMulti || item.ItemID >= 0x4000;
    }

    private static string GetName(Item item)
    {
        if (!string.IsNullOrEmpty(item.Name))
            return item.Name;

        // the tile data only holds names for static tiles, masking a multi id would yield an unrelated name
        return IsMulti(item) ? "" : TileData.ItemTable[item.ItemID & 0x3FFF].Name ?? "";
    }
}
