using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace EplanCipMvp.Core.WireNaming
{
    /// <summary>Правила нумерации жил и проводов в JSON рядом с exe; нет файла — пресет «Как 1260».</summary>
    public class WireRuleStore
    {
        private readonly string _path;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly object _lock = new object();

        public WireRuleStore(string path) { _path = path; }

        public List<WireRule> Load()
        {
            lock (_lock)
            {
                if (!File.Exists(_path)) return WireRulePresets.Like1260();
                return _json.Deserialize<List<WireRule>>(File.ReadAllText(_path, Encoding.UTF8)) ?? new List<WireRule>();
            }
        }

        public List<WireRule> Save(List<WireRule> rules)
        {
            lock (_lock)
            {
                rules = rules ?? new List<WireRule>();
                File.WriteAllText(_path, _json.Serialize(rules), Encoding.UTF8);
                return rules;
            }
        }

        public List<WireRule> ResetToPreset() => Save(WireRulePresets.Like1260());
    }
}
