using Quick.Fields;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YiQiDong.Protocol.V1.Model;

namespace YiQiDong.MySQL.Utils
{
    public class IniFileUtils
    {
        public static Dictionary<string, string> Load(string file)
        {
            var lines = File.ReadAllLines(file);

            var properties = new Dictionary<string, string>();
            foreach (var line in lines)
            {
                if (line.StartsWith("#"))
                    continue;
                var spIndex = line.IndexOf('=');
                if (spIndex <= 0)
                    continue;
                var key = line.Substring(0, spIndex);
                var value = line.Substring(spIndex + 1).Trim();
                properties[key] = value;
            }
            return properties;
        }
        public static void Save(string file, FieldForPost[] fields)
        {
            Save(
                file,
                fields
                .Where(t => !string.IsNullOrEmpty(t.Id))
                .ToDictionary(t => t.Id, t => t.Value));
        }

        public static void Save(string file, Dictionary<string, string> fieldDict)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line))
                    continue;
                if (line.StartsWith("#"))
                    continue;
                var spIndex = line.IndexOf('=');
                if (spIndex <= 0)
                    continue;
                var key = line.Substring(0, spIndex);
                if (fieldDict.ContainsKey(key))
                    lines[i] = $"{key}={fieldDict[key]}";
            }
            File.WriteAllLines(file, lines);
        }
    }
}
