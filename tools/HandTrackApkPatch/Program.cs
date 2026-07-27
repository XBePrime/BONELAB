using System.IO.Compression;
using QuestPatcher.Axml;

namespace BePrime.Nerve.HandTrackApkPatch;

/// <summary>
/// One-shot APK AndroidManifest patcher: adds Meta Quest hand-tracking
/// permission + uses-feature so BONELAB can enumerate hand devices.
///
/// Usage:
///   HandTrackApkPatch path/to/BONELAB.apk
///
/// After patching, re-sign / reinstall (LemonLoader re-patch works).
/// Without these manifest entries Quest never exposes hands to the app —
/// OVRPlugin and Unity XR both stay empty (ovrEnable=False, handData=0).
/// </summary>
internal static class Program
{
    private static readonly Uri AndroidNs = new("http://schemas.android.com/apk/res/android");
    private const int NameAttrId = 16842755;      // android:name
    private const int RequiredAttrId = 16843342;  // android:required
    private const int ValueAttrId = 16842788;     // android:value

    private const string HandPerm = "com.oculus.permission.HAND_TRACKING";
    private const string HandFeature = "oculus.software.handtracking";

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: HandTrackApkPatch <path-to-bonelab.apk> [output.apk]");
            return 2;
        }

        string input = Path.GetFullPath(args[0]);
        if (!File.Exists(input))
        {
            Console.Error.WriteLine("APK not found: " + input);
            return 1;
        }

        string output = args.Length >= 2
            ? Path.GetFullPath(args[1])
            : Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + ".handtrack.apk");

        Console.WriteLine("Input : " + input);
        Console.WriteLine("Output: " + output);

        File.Copy(input, output, overwrite: true);

        using (FileStream zipStream = new(output, FileMode.Open, FileAccess.ReadWrite))
        using (ZipArchive archive = new(zipStream, ZipArchiveMode.Update))
        {
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(e => e.FullName == "AndroidManifest.xml");
            if (entry == null)
            {
                Console.Error.WriteLine("AndroidManifest.xml missing in APK");
                return 1;
            }

            byte[] original;
            using (Stream s = entry.Open())
            using (MemoryStream ms = new())
            {
                s.CopyTo(ms);
                original = ms.ToArray();
            }

            AxmlElement manifest;
            using (MemoryStream ms = new(original))
                manifest = AxmlLoader.LoadDocument(ms);

            bool changed = false;
            changed |= AddPermission(manifest, HandPerm);
            changed |= AddUsesFeature(manifest, HandFeature, required: false);
            changed |= AddAppMeta(manifest, "com.oculus.handtracking.frequency", "HIGH");
            changed |= AddAppMeta(manifest, "com.oculus.handtracking.version", "V2.0");

            if (!changed)
            {
                Console.WriteLine("Manifest already has hand-tracking entries — nothing to do.");
            }
            else
            {
                using MemoryStream save = new();
                AxmlSaver.SaveDocument(save, manifest);
                byte[] patched = save.ToArray();

                // Replace entry contents
                entry.Delete();
                ZipArchiveEntry fresh = archive.CreateEntry("AndroidManifest.xml", CompressionLevel.Optimal);
                using Stream outStream = fresh.Open();
                outStream.Write(patched, 0, patched.Length);
                Console.WriteLine("Wrote patched AndroidManifest.xml");
            }
        }

        Console.WriteLine();
        Console.WriteLine("NEXT:");
        Console.WriteLine("  1) Re-sign + install this APK (LemonLoader → re-patch BONELAB, or apksigner).");
        Console.WriteLine("  2) On first launch allow Hand Tracking if Quest prompts.");
        Console.WriteLine("  3) Install NERVE 1.1.8+, put controllers down, check log for handData>0.");
        return 0;
    }

    private static bool AddPermission(AxmlElement manifest, string permission)
    {
        if (HasNamedChild(manifest, "uses-permission", permission))
        {
            Console.WriteLine("OK already: uses-permission " + permission);
            return false;
        }

        AxmlElement el = new("uses-permission");
        el.Attributes.Add(new AxmlAttribute("name", AndroidNs, NameAttrId, permission));
        manifest.Children.Add(el);
        Console.WriteLine("ADD uses-permission " + permission);
        return true;
    }

    private static bool AddUsesFeature(AxmlElement manifest, string feature, bool required)
    {
        if (HasNamedChild(manifest, "uses-feature", feature))
        {
            Console.WriteLine("OK already: uses-feature " + feature);
            return false;
        }

        AxmlElement el = new("uses-feature");
        el.Attributes.Add(new AxmlAttribute("name", AndroidNs, NameAttrId, feature));
        el.Attributes.Add(new AxmlAttribute("required", AndroidNs, RequiredAttrId, required));
        manifest.Children.Add(el);
        Console.WriteLine($"ADD uses-feature {feature} required={required}");
        return true;
    }

    private static bool AddAppMeta(AxmlElement manifest, string name, string value)
    {
        AxmlElement app = manifest.Children.First(c => c.Name == "application");
        if (HasNamedChild(app, "meta-data", name))
        {
            Console.WriteLine("OK already: meta-data " + name);
            return false;
        }

        AxmlElement el = new("meta-data");
        el.Attributes.Add(new AxmlAttribute("name", AndroidNs, NameAttrId, name));
        el.Attributes.Add(new AxmlAttribute("value", AndroidNs, ValueAttrId, value));
        app.Children.Add(el);
        Console.WriteLine($"ADD meta-data {name}={value}");
        return true;
    }

    private static bool HasNamedChild(AxmlElement parent, string childName, string androidName)
    {
        foreach (AxmlElement child in parent.Children)
        {
            if (child.Name != childName) continue;
            foreach (AxmlAttribute attr in child.Attributes)
            {
                if (attr.Namespace == AndroidNs && attr.Name == "name" && Equals(attr.Value, androidName))
                    return true;
            }
        }
        return false;
    }
}
