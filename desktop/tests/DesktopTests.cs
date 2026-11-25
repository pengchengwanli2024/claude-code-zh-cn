using System;
using System.Drawing;
using System.IO;
using System.Text;

class DesktopTests
{
    static int passed;
    static void Check(bool result, string scenario)
    {
        if (!result) throw new Exception(scenario);
        Console.WriteLine("PASS " + scenario);
        passed++;
    }

    static void WriteArchive(string path)
    {
        byte[] en = Encoding.UTF8.GetBytes("{\"a\":\"About Claude\",\"b\":\"Remove {name}\",\"c\":\"Changed\",\"d\":\"Changed\"}");
        byte[] zh = Encoding.UTF8.GetBytes("{\"a\":\"关于 Claude\",\"b\":\"移除 {name}\",\"c\":\"已更改\",\"d\":\"更改\"}");
        string header = "{\"files\":{\"resources\":{\"files\":{\"i18n\":{\"files\":{" +
            "\"en-US.json\":{\"offset\":\"0\",\"size\":" + en.Length + "}," +
            "\"zh-Hans.json\":{\"offset\":\"" + en.Length + "\",\"size\":" + zh.Length + "}}}}}}}";
        byte[] encoded = Encoding.UTF8.GetBytes(header);
        int aligned = (encoded.Length + 3) & ~3;
        using (var writer = new BinaryWriter(File.Create(path))) {
            writer.Write(4); writer.Write(aligned + 8); writer.Write(aligned + 4); writer.Write(encoded.Length);
            writer.Write(encoded); writer.Write(new byte[aligned - encoded.Length]); writer.Write(en); writer.Write(zh);
        }
    }

    static void Main(string[] args)
    {
        foreach (string label in new[] { "Zoom In Ctrl++", "Settings Ctrl+逗号", "Find Previous Shift+F3" })
            Check(DesktopZh.Shortcut.Match(label).Success, label);
        var keys = DesktopZh.ShortcutList.Match(DesktopZh.LabelName("Next session\u00a0CtrlShift] or CtrlTab"));
        Check(keys.Success && keys.Groups[1].Value == "Next session", "shortcut dialog preserves key badges");
        Check(!DesktopZh.ShortcutList.IsMatch("A user's ordinary sentence"), "ordinary list item");
        Check(!DesktopZh.Overlaps(new Rectangle(0,0,180,43), new Rectangle(0,42,180,43)), "adjacent rows at fractional scale");
        Check(DesktopZh.Overlaps(new Rectangle(0,0,180,43), new Rectangle(5,3,80,23)), "duplicate parent and child");
        string path = args[0];
        WriteArchive(path);
        var locale = NativeCatalog.Load(path);
        Check(locale.Count == 1 && locale["About Claude"] == "关于 Claude", "ID matching, placeholder and ambiguity handling");
        File.WriteAllBytes(path, new byte[12]);
        Check(NativeCatalog.Load(path).Count == 0, "truncated archive falls back");
        File.Delete(path);
        Check(NativeCatalog.Load(path).Count == 0, "missing archive falls back");
        Console.WriteLine("{0} passed", passed);
    }
}
