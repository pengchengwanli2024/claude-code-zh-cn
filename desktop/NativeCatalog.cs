using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

// Reads two data files from the locally installed Electron archive. No extraction or writes.
internal static class NativeCatalog
{
    const int Limit = 16 * 1024 * 1024;
    static readonly char[] Template = { '{', '}', '<', '>', '%' };

    static Dictionary<string,object> Child(Dictionary<string,object> node, string key)
    {
        object value;
        return node != null && node.TryGetValue(key, out value) ? value as Dictionary<string,object> : null;
    }

    static string ReadText(BinaryReader reader, int length)
    {
        if (length <= 0 || length > Limit) throw new InvalidDataException("Invalid locale length.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    static Dictionary<string,object> Locale(BinaryReader reader, long start, Dictionary<string,object> directory,
        string filename, JavaScriptSerializer json)
    {
        var entry = Child(directory, filename);
        object offset, size;
        long relative;
        int length;
        if (entry == null || !entry.TryGetValue("offset", out offset) || !entry.TryGetValue("size", out size) ||
            !Int64.TryParse(Convert.ToString(offset, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out relative) ||
            !Int32.TryParse(Convert.ToString(size, CultureInfo.InvariantCulture), out length) ||
            length <= 0 || length > Limit || relative > reader.BaseStream.Length - start - length) return null;
        reader.BaseStream.Position = start + relative;
        return json.DeserializeObject(ReadText(reader, length)) as Dictionary<string,object>;
    }

    internal static Dictionary<string,string> Load(string archivePath)
    {
        var result = new Dictionary<string,string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        try {
            var json = new JavaScriptSerializer { MaxJsonLength = Limit, RecursionLimit = 160 };
            using (var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new BinaryReader(file)) {
                if (r.ReadUInt32() != 4) return result;
                uint headerSize = r.ReadUInt32();
                uint payloadSize = r.ReadUInt32();
                int jsonSize = r.ReadInt32();
                long start = 8L + headerSize;
                if (headerSize > Limit || start > file.Length || payloadSize + 4L != headerSize ||
                    jsonSize <= 0 || jsonSize + 4L > payloadSize) return result;
                var node = json.DeserializeObject(ReadText(r, jsonSize)) as Dictionary<string,object>;
                foreach (var part in new[] { "files", "resources", "files", "i18n", "files" }) node = Child(node, part);
                var en = Locale(r, start, node, "en-US.json", json);
                var zh = Locale(r, start, node, "zh-Hans.json", json);
                if (en == null || zh == null) return result;
                foreach (var pair in en) {
                    string source = pair.Value as string;
                    object raw;
                    if (String.IsNullOrWhiteSpace(source) || source.IndexOfAny(Template) >= 0 || !zh.TryGetValue(pair.Key, out raw)) continue;
                    string target = raw as string;
                    if (String.IsNullOrWhiteSpace(target) || target.IndexOfAny(Template) >= 0 || !target.Any(c => c >= '\u3400' && c <= '\u9fff')) continue;
                    string existing;
                    if (result.TryGetValue(source, out existing) && existing != target) ambiguous.Add(source);
                    else result[source] = target;
                }
            }
        } catch (IOException) { result.Clear(); }
          catch (UnauthorizedAccessException) { result.Clear(); }
          catch (ArgumentException) { result.Clear(); }
          catch (InvalidOperationException) { result.Clear(); }
          catch (InvalidDataException) { result.Clear(); }
        foreach (var key in ambiguous) result.Remove(key);
        return result;
    }
}
