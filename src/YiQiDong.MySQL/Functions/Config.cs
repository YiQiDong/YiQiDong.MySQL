using Quick.Fields;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using YiQiDong.MySQL.Utils;
using YiQiDong.Protocol.V1.Model;
using YiQiDong.Core;

namespace YiQiDong.MySQL.Functions
{
    class Config : AbstractFunction
    {
        public const string CONFIG_FILE = "my.ini";
        public static Config Instance { get; private set; }

        private string name;
        public override string Name => name;
        public Dictionary<string, string> Properties = null;
        private string containerConfigFile;

        public void RefreshProperties()
        {
            if (File.Exists(containerConfigFile))
                Properties = IniFileUtils.Load(containerConfigFile);
        }

        public Config(string name,string imageFolder, string containerFolder)
        {
            Instance = this;
            this.name = name;

            containerConfigFile = Path.Combine(containerFolder, CONFIG_FILE);
            if (!File.Exists(containerConfigFile))
            {
                var folder = Path.GetDirectoryName(containerConfigFile);
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                var imageConfigFile = Path.Combine(imageFolder, CONFIG_FILE);
                if (File.Exists(imageConfigFile))
                    File.Copy(imageConfigFile, containerConfigFile, true);
            }
            RefreshProperties();
        }

        public string GetConnectHost()
        {
            if (Properties == null)
                throw new ApplicationException($"配置文件[{CONFIG_FILE}]不存在！");

            var ret = Properties["bind-address"];
            if (ret == "0.0.0.0")
                ret = "127.0.0.1";
            return ret;
        }

        public int GetConnectPort()
        {
            if (Properties == null)
                throw new ApplicationException($"配置文件[{CONFIG_FILE}]不存在！");

            return int.Parse(Properties["port"]);
        }

        private List<FieldForGet> innerGet(FunctionRequest request, bool isReadOnly = false)
        {
            List<FieldForGet> list = new List<FieldForGet>();
            if (!File.Exists(containerConfigFile))
            {
                list.Add(new FieldForGet() { Name = "失败", Description = $"配置文件[{CONFIG_FILE}]不存在！", Input_ReadOnly = true, Type = FieldType.Alert });
                return list;
            }
            Properties = IniFileUtils.Load(containerConfigFile);

            string tmpKey;
            tmpKey = "bind-address";
            if (Properties.ContainsKey(tmpKey))
                list.Add(new FieldForGet()
                {
                    Id = tmpKey,
                    Name = "绑定地址",
                    Type = FieldType.InputText,
                    Input_ReadOnly = isReadOnly,
                    Value = request == null ? Properties[tmpKey] : request.GetFieldValue(tmpKey),
                    Input_AllowBlank = false,
                    Description = "MySQL的绑定地址，默认为0.0.0.0"
                });
            tmpKey = "port";
            if (Properties.ContainsKey(tmpKey))
                list.Add(new FieldForGet()
                {
                    Id = tmpKey,
                    Name = "端口",
                    Type = FieldType.InputNumber,
                    Input_ReadOnly = isReadOnly,
                    Value = request == null ? Properties[tmpKey] : request.GetFieldValue(tmpKey),
                    Input_AllowBlank = false,
                    Description = "MySQL的监听端口，默认为3306"
                });
            tmpKey = "innodb_file_per_table";
            if (Properties.ContainsKey(tmpKey))
                list.Add(new FieldForGet()
                {
                    Id = tmpKey,
                    Name = "存储方式",
                    Type = FieldType.InputSelect,
                    Input_ReadOnly = isReadOnly,
                    InputSelect_Options = new Dictionary<string, string>()
                    {
                        ["0"] = "集中存储",
                        ["1"] = "分表存储"
                    },
                    Description = @"集中存储: 全部数据库全部表的数据保存在一个ibdata1文件中。
分表存储: 每个表的数据单独分一个文件存储。",
                    Value = request == null ? Properties[tmpKey] : request.GetFieldValue(tmpKey),
                    Input_AllowBlank = false
                });
            return list;
        }

        public override FieldForGet[] Get()
        {
            var isReadOnly = Agent.Instance.ContainerInfo.AutoStart;
            var list = innerGet(null, isReadOnly);
            if (!isReadOnly)
                addSaveButton(list);
            return list.ToArray();
        }

        public override FieldForGet[] Post(FunctionRequest request)
        {
            var list = innerGet(request);
            if (request.IsFieldIdsMatch("Save"))
            {
                try
                {
                    Save(request.Fields);
                    list.Add(new FieldForGet()
                    {
                        Name = "保存成功",
                        Description = $"配置文件[{CONFIG_FILE}]保存成功！",
                        Type = FieldType.MessageBox
                    });
                }
                catch (Exception ex)
                {
                    list.Add(new FieldForGet()
                    {
                        Name = "错误",
                        Description = ex.Message,
                        Type = FieldType.Alert,
                        Input_ReadOnly = true
                    });
                }
                addSaveButton(list);
            }
            return list.ToArray();
        }

        public void Save(FieldForPost[] fields)
        {
            if (!File.Exists(containerConfigFile))
                throw new IOException($"配置文件[{CONFIG_FILE}]不存在！");
            IniFileUtils.Save(containerConfigFile, fields);
            //保存成功后重新加载配置文件
            Properties = IniFileUtils.Load(containerConfigFile);
        }

        private void addSaveButton(List<FieldForGet> list)
        {
            list.Add(new FieldForGet() { Id = "Save", Name = "保存", Type = FieldType.Button });
        }
    }
}
