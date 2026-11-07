using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using ProjectZ;
using QuestEditor_Library;

namespace IsoRimCQFPack
{
    // The layout of the map a def was saved from.
    internal struct Layout
    {
        public int floors, groundX, groundZ, stride;

        // Does the def's data still have this layout, and is it one ProjectZ lays out the same way today?
        public bool FitsDef(CustomMapDataDef def) =>
            floors >= 2 && floors <= ProjectZSettings.MaxFloors && groundX > 0 && groundZ > 0 && stride == ProjectZ.ProjectZ.StrideFor(groundZ)
            && def.size.x == groundX && def.size.z == floors * stride;
    }

    // The def's tag "IsoRimZ.floors=2.ground=100x100.stride=102". CQF compares tags by equality only (QuestNode_RandomCustomMap,
    // CustomMapGenerationSet, the entrances and world actions look up the tags their authors wrote), so no pick can land on it.
    internal static class Marker
    {
        private const string Start = "IsoRimZ.floors=";
        private static readonly Regex Shape = new Regex(@"^IsoRimZ\.floors=(\d{1,3})\.ground=(\d{1,5})x(\d{1,5})\.stride=(\d{1,5})$", RegexOptions.CultureInvariant);

        internal static bool IsMarker(string tag) => tag != null && tag.StartsWith(Start, System.StringComparison.Ordinal);

        internal static string Format(Layout l) =>
            string.Format(CultureInfo.InvariantCulture, "{0}{1}.ground={2}x{3}.stride={4}", Start, l.floors, l.groundX, l.groundZ, l.stride);

        // False for a def without the tag: the one test every patch makes first.
        internal static bool TryRead(CustomMapDataDef def, out Layout layout)
        {
            layout = default(Layout);
            List<string> tags = def?.tags;
            if (tags == null)
            {
                return false;
            }
            for (int i = 0; i < tags.Count; i++)
            {
                if (!IsMarker(tags[i]))
                {
                    continue;
                }
                Match m = Shape.Match(tags[i]);
                if (m.Success)
                {
                    layout.floors = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    layout.groundX = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    layout.groundZ = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                    layout.stride = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                }
                return true;   // (a broken tag still marks the def: its layout fits nothing, so it is refused)
            }
            return false;
        }
    }
}
