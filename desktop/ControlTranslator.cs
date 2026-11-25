using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Web.Script.Serialization;

// Queue only named UI actions. Raw document text, editable fields, code and paths
// are never submitted to the model. The cache lives only for the current session.
sealed class ControlTranslator : IDisposable
{
    readonly string root;
    readonly string python;
    readonly Dictionary<string,string> cache = new Dictionary<string,string>();
    readonly List<string> pending = new List<string>();
    Process worker;
    volatile bool failed;
    bool running;

    internal ControlTranslator(string directory)
    {
        root = directory;
        python = Path.Combine(Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar)), "mt-runtime", "Scripts", "python.exe");
    }

    internal void Consider(string label, ControlType role)
    {
        if (failed || label == null || label.Length > 100) return;
        if (role != ControlType.Button && role != ControlType.MenuItem && role != ControlType.TabItem &&
            role != ControlType.RadioButton && role != ControlType.CheckBox) return;
        if (!Regex.IsMatch(label, @"^(?:Add|Allow|Apply|Cancel|Change|Check|Choose|Clear|Close|Configure|Continue|Copy|Create|Deny|Disable|Dismiss|Download|Edit|Enable|Expand|Export|Go|Hide|Import|Install|Load|Manage|Open|Pause|Remove|Rename|Reset|Resume|Retry|Run|Save|Select|Send|Set|Show|Sign|Start|Stop|Switch|Toggle|Update|Use|View) [A-Za-z][A-Za-z '’-]*$", RegexOptions.IgnoreCase)) return;
        lock (cache) {
            if (!running && pending.Count < 12 && !cache.ContainsKey(label) && !pending.Contains(label)) pending.Add(label);
        }
    }

    internal void TranslatePending()
    {
        string[] batch;
        lock (cache) {
            if (running || pending.Count == 0) return;
            batch = pending.ToArray();
            pending.Clear();
            running = true;
        }
        Task.Run(() => Translate(batch));
    }

    void Translate(string[] batch)
    {
        try {
            if (worker == null) {
                if (!File.Exists(python)) { failed = true; return; }
                var start = new ProcessStartInfo(python, "-I -X utf8 -c \"import sys,runpy;sys.path.insert(0,'.');runpy.run_path('translate_controls.py',run_name='__main__')\"");
                start.WorkingDirectory = root;
                start.UseShellExecute = false;
                start.RedirectStandardInput = true;
                start.RedirectStandardOutput = true;
                start.CreateNoWindow = true;
                worker = Process.Start(start);
            }
            var json = new JavaScriptSerializer();
            // Requests contain English; the worker emits ASCII escaped JSON.
            worker.StandardInput.WriteLine(json.Serialize(batch));
            var response = worker.StandardOutput.ReadLineAsync();
            if (!response.Wait(10000) || response.Result == null) { failed = true; return; }
            var translated = json.Deserialize<string[]>(response.Result);
            if (translated.Length != batch.Length) { failed = true; return; }
            lock (cache) {
                if (cache.Count + batch.Length > 1024) cache.Clear();
                for (int i = 0; i < batch.Length; i++) cache[batch[i]] = translated[i];
            }
        } catch (IOException) { failed = true; }
          catch (InvalidOperationException) { failed = true; }
          catch (System.ComponentModel.Win32Exception) { failed = true; }
          catch (AggregateException) { failed = true; }
        finally { lock (cache) running = false; if (failed) Dispose(); }
    }

    internal string Get(string label)
    {
        string result;
        lock (cache) return cache.TryGetValue(label, out result) && result.Any(c => c >= '\u3400' && c <= '\u9fff') ? "≈" + result : null;
    }

    public void Dispose()
    {
        if (worker == null) return;
        try { worker.StandardInput.Close(); } catch (IOException) { }
        worker.Dispose();
        worker = null;
    }
}
