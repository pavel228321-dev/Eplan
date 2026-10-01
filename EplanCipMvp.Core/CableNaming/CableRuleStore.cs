using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace EplanCipMvp.Core.CableNaming
{
    /// <summary>Правила нумерации кабелей в JSON рядом с exe; нет файла — пресет «Как 1260».</summary>
    public class CableRuleStore
    {
        private readonly string _path;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly object _lock = new object();

        public CableRuleStore(string path) { _path = path; }

        public List<CableRule> Load()
        {
            lock (_lock)
            {
                if (!File.Exists(_path)) return CableRulePresets.Like1260();
                return _json.Deserialize<List<CableRule>>(File.ReadAllText(_path, Encoding.UTF8)) ?? new List<CableRule>();
            }
        }

        public List<CableRule> Save(List<CableRule> rules)
        {
            lock (_lock)
            {
                rules = rules ?? new List<CableRule>();
                File.WriteAllText(_path, _json.Serialize(rules), Encoding.UTF8);
                return rules;
            }
        }

        public List<CableRule> ResetToPreset() => Save(CableRulePresets.Like1260());
    }
}
