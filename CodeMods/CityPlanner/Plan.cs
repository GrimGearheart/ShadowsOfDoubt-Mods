using System;
using System.Collections.Generic;
using System.IO;

namespace CityPlanner;

internal class PlannedDistrict
{
    public char Key;
    public string Name;
    public string Type;
    public BuildingPreset.LandValue LandValue;
    public BuildingPreset.Density Density;
}

internal class PlannedTile
{
    public PlannedDistrict District;
    public string Building;
    /// <summary>Which way the building faces (its front), or null to let the game choose.</summary>
    public NewBuilding.Direction? Facing;
}

/// <summary>The city plan, read from BepInEx/config/cityplan.txt (see that file for the format).</summary>
internal class Plan
{
    public int Width, Height;
    public float Population = 1f;
    public readonly Dictionary<char, PlannedDistrict> Districts = new();

    /// <summary>Interior tiles by city coordinate (x 1..Width-2 west to east, y 1..Height-2 south to north).</summary>
    public readonly Dictionary<(int x, int y), PlannedTile> Tiles = new();

    public static Plan Load(string path, out string error)
    {
        error = null;
        if (!File.Exists(path))
        {
            error = "no plan file at " + path;
            return null;
        }
        var plan = new Plan();
        var codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<string[]>();
        var section = "";
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }
            switch (section)
            {
                case "city":
                {
                    var (k, v) = KeyValue(line);
                    if (k.Equals("Size", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = v.ToLowerInvariant().Split('x');
                        int.TryParse(p[0], out plan.Width);
                        if (p.Length > 1) int.TryParse(p[1], out plan.Height);
                    }
                    else if (k.Equals("Population", StringComparison.OrdinalIgnoreCase))
                    {
                        float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out plan.Population);
                    }
                    break;
                }
                case "districts":
                {
                    var (k, v) = KeyValue(line);
                    var p = v.Split('|');
                    if (k.Length != 1 || p.Length < 4) throw new FormatException("bad district line: " + line);
                    plan.Districts[k[0]] = new PlannedDistrict
                    {
                        Key = k[0],
                        Name = p[0].Trim(),
                        Type = p[1].Trim(),
                        LandValue = Enum.Parse<BuildingPreset.LandValue>(p[2].Trim(), true),
                        Density = Enum.Parse<BuildingPreset.Density>(p[3].Trim(), true),
                    };
                    break;
                }
                case "buildings":
                {
                    var (k, v) = KeyValue(line);
                    codes[k] = v;
                    break;
                }
                case "map":
                    rows.Add(line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                    break;
            }
        }

        if (plan.Width < 5 || plan.Height < 5)
        {
            error = "missing or bad Size";
            return null;
        }
        if (rows.Count != plan.Height - 2)
        {
            error = $"map has {rows.Count} rows, a {plan.Width}x{plan.Height} city needs {plan.Height - 2}";
            return null;
        }
        for (var r = 0; r < rows.Count; r++)
        {
            if (rows[r].Length != plan.Width - 2)
            {
                error = $"map row {r + 1} has {rows[r].Length} cells, needs {plan.Width - 2}";
                return null;
            }
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = rows[r][c].Split(':');
                if (cell.Length != 2 || cell[0].Length != 1 || !plan.Districts.TryGetValue(cell[0][0], out var district))
                {
                    error = $"map row {r + 1}, column {c + 1}: '{rows[r][c]}' isn't District:Building with a known district";
                    return null;
                }
                // Optional facing: Code/N, /E, /S or /W
                var code = cell[1];
                NewBuilding.Direction? facing = null;
                var slash = code.IndexOf('/');
                if (slash >= 0)
                {
                    // The plan names the side the front door is on. The game's Direction names the opposite side
                    // (its own facing code gives a building with its busiest street to the west "East").
                    facing = code[(slash + 1)..].ToUpperInvariant() switch
                    {
                        "N" => NewBuilding.Direction.South,
                        "E" => NewBuilding.Direction.West,
                        "S" => NewBuilding.Direction.North,
                        "W" => NewBuilding.Direction.East,
                        _ => null,
                    };
                    if (facing == null)
                    {
                        error = $"map row {r + 1}, column {c + 1}: facing must be /N, /E, /S or /W in '{rows[r][c]}'";
                        return null;
                    }
                    code = code[..slash];
                }
                if (!codes.TryGetValue(code, out var building))
                {
                    error = $"map row {r + 1}, column {c + 1}: unknown building code '{code}'";
                    return null;
                }
                // Row 0 is the north edge: the highest y.
                plan.Tiles[(c + 1, plan.Height - 2 - r)] = new PlannedTile { District = district, Building = building, Facing = facing };
            }
        }
        return plan;
    }

    private static (string, string) KeyValue(string line)
    {
        var i = line.IndexOf('=');
        return i < 0 ? (line.Trim(), "") : (line[..i].Trim(), line[(i + 1)..].Trim());
    }

    /// <summary>The planned tile nearest to a coordinate (used to give coastline tiles a district).</summary>
    public PlannedTile Nearest(int x, int y) =>
        Tiles[(Math.Clamp(x, 1, Width - 2), Math.Clamp(y, 1, Height - 2))];
}
