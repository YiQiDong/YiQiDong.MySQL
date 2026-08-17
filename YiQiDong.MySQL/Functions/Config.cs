using Quick.Fields;
using YiQiDong.MySQL.Utils;
using YiQiDong.Protocol.V1.Model;
using YiQiDong.Core;
using YiQiDong.Agent;
using Quick.Utils;

namespace YiQiDong.MySQL.Functions
{
    class Config : AbstractFunction
    {
        public const string CONFIG_FILE = "my.ini";
        public const string DATA_FOLDER_CONFIG_FILE = "DataFolder.conf";
        public static Config Instance { get; private set; }

        private string imageFolder;
        private string containerFolder;

        public override string Name => "数据库配置";
        public Dictionary<string, string> Properties = null;
        public string MySqlConfigFile { get; private set; }

        public Config() : this(AgentContext.Container.ImageFolder, AgentContext.Container.ContainerFolder) { }

        public Config(string imageFolder, string containerFolder)
        {
            Instance = this;
            this.containerFolder = containerFolder;
            this.imageFolder = imageFolder;
        }

        public void RefreshProperties(string dataFolder)
        {
            if (!Directory.Exists(dataFolder))
                return;

            MySqlConfigFile = Path.Combine(dataFolder, CONFIG_FILE);
            if (!File.Exists(MySqlConfigFile))
            {
                var folder = Path.GetDirectoryName(MySqlConfigFile);
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                var imageConfigFile = Path.Combine(imageFolder, CONFIG_FILE);
                if (File.Exists(imageConfigFile))
                {
                    File.Copy(imageConfigFile, MySqlConfigFile, true);
                    AgentContext.LogInfo($"配置文件[{MySqlConfigFile}]不存在，已从镜像目录默认配置文件[{imageConfigFile}]复制。");
                }
            }
            if (File.Exists(MySqlConfigFile))
            {
                Properties = IniFileUtils.Load(MySqlConfigFile);
            }
            else
            {
                AgentContext.LogWarn($"配置文件[{MySqlConfigFile}]不存在！");
            }
        }

        private string GetDataFolder_ForConfig()
        {
            var dataFolderFile = Path.Combine(containerFolder, DATA_FOLDER_CONFIG_FILE);
            string dataFolder = null;
            if (File.Exists(dataFolderFile))
            {
                var tmpFolder = File.ReadAllText(dataFolderFile);
                if (!string.IsNullOrEmpty(tmpFolder) && Directory.Exists(tmpFolder))
                    dataFolder = tmpFolder;
            }
            return dataFolder;
        }

        public string GetDataFolder()
        {
            var dataFolder = GetDataFolder_ForConfig();
            if (string.IsNullOrEmpty(dataFolder))
                dataFolder = containerFolder;
            return dataFolder;
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

        public string GetPassword()
        {
            if (Properties.ContainsKey("password"))
                return Properties["password"];
            return string.Empty;
        }

        public void UpdatePassword(string password)
        {
            Properties["password"] = password;
            IniFileUtils.Save(MySqlConfigFile, Properties);
        }

        private List<FieldForGet> innerGet(FunctionRequest request, bool isReadOnly = false)
        {
            List<FieldForGet> list = new List<FieldForGet>();

            string tmpKey;
            tmpKey = "DataFolder";
            var dataFolder = request == null ? GetDataFolder_ForConfig() : request.GetFieldValue(tmpKey);
            var isDataFolderExists = string.IsNullOrEmpty(dataFolder) || Directory.Exists(dataFolder);
            list.Add(new FieldForGet()
            {
                Id = tmpKey,
                Name = "数据目录",
                Type = FieldType.InputText,
                PostOnChanged = true,
                Input_ReadOnly = isReadOnly,
                Value = dataFolder,
                Input_AllowBlank = true,
                Description = "默认数据库的数据目录为空，代表容器目录。"
            });
            if (string.IsNullOrEmpty(dataFolder))
            {
                list.Add(new FieldForGet()
                {
                    Type = FieldType.Alert,
                    Html_Class = "alert-warning",
                    Description = $"风险提示：删除容器时会删除数据库文件，建议将数据目录修改到其他目录!",
                });
            }
            if (!isDataFolderExists)
            {
                list.Add(new FieldForGet()
                {
                    Type = FieldType.Alert,
                    Html_Class = "alert-danger",
                    Description = $"配置的数据目录[{dataFolder}]不存在！"
                });
            }
            else
            {
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
                tmpKey = "ssl";
                if (Properties.ContainsKey(tmpKey))
                    list.Add(new FieldForGet()
                    {
                        Id = tmpKey,
                        Name = "SSL功能",
                        Type = FieldType.InputSelect,
                        Input_ReadOnly = isReadOnly,
                        InputSelect_Options = new Dictionary<string, string>()
                        {
                            ["0"] = "关闭",
                            ["1"] = "开启"
                        },
                        Value = request == null ? Properties[tmpKey] : request.GetFieldValue(tmpKey),
                        Input_AllowBlank = false
                    });
                tmpKey = "character-set-server";
                if (Properties.ContainsKey(tmpKey))
                    list.Add(new FieldForGet()
                    {
                        Id = tmpKey,
                        Name = "服务端字符集",
                        Type = FieldType.InputSelect,
                        Input_ReadOnly = isReadOnly,
                        InputSelect_Options = new Dictionary<string, string>()
                        {
                            ["gb2312"] = "gb2312",
                            ["gbk"] = "gbk",
                            ["gb18030"] = "gb18030",
                            ["utf8"] = "utf8",
                            ["utf8mb4"] = "utf8mb4"
                        },
                        Value = request == null ? Properties[tmpKey] : request.GetFieldValue(tmpKey),
                        Input_AllowBlank = false
                    });
            }
            return list;
        }

        public override List<FieldForGet> Execute(FunctionRequest request)
        {
            if(request==null)
                return Get();
            return Post(request);
        }

        private List<FieldForGet> Get()
        {
            var isReadOnly = AgentContext.Container.AutoStart;
            RefreshProperties(GetDataFolder());
            var list = innerGet(null, isReadOnly);
            if (!isReadOnly)
                addSaveButton(list);
            return list;
        }

        private List<FieldForGet> Post(FunctionRequest request)
        {
            if (request.IsFieldIdsMatch("DataFolder"))
            {
                var dataFolder = request.GetFieldValue("DataFolder");
                if (string.IsNullOrEmpty(dataFolder))
                    RefreshProperties(containerFolder);
                else
                    RefreshProperties(dataFolder);
                request.Fields = innerGet(null).Select(t => t.ToPost()).ToList();
                request.Fields[0].Value = dataFolder;
            }
            var list = innerGet(request);
            if (request.IsFieldIdsMatch("Save"))
            {
                try
                {
                    if (File.Exists(MySqlConfigFile))
                    {
                        var dataFolder = request.GetFieldValue("DataFolder");
                        var dataFolderConfigFile = Path.Combine(containerFolder, DATA_FOLDER_CONFIG_FILE);
                        if (string.IsNullOrEmpty(dataFolder))
                        {
                            if (File.Exists(dataFolderConfigFile))
                                File.Delete(dataFolderConfigFile);
                        }
                        else
                        {
                            File.WriteAllText(dataFolderConfigFile, dataFolder);
                        }
                        IniFileUtils.Save(MySqlConfigFile, request.Fields);
                        //保存成功后重新加载配置文件
                        if (string.IsNullOrEmpty(dataFolder))
                            dataFolder = containerFolder;
                        RefreshProperties(dataFolder);
                        list.Add(new FieldForGet()
                        {
                            Name = "保存成功",
                            Description = $"配置文件[{MySqlConfigFile}]保存成功！",
                            Type = FieldType.MessageBox
                        });
                    }
                    else
                    {
                        list.Add(new FieldForGet()
                        {
                            Name = "错误",
                            Description = $"配置文件[{MySqlConfigFile}]不存在！",
                            Type = FieldType.Alert
                        });
                    }
                }
                catch (Exception ex)
                {
                    list.Add(new FieldForGet()
                    {
                        Name = "错误",
                        Description = ExceptionUtils.GetExceptionMessage(ex),
                        Type = FieldType.Alert,
                        Input_ReadOnly = true
                    });
                }
            }
            addSaveButton(list);
            return list;
        }

        private void addSaveButton(List<FieldForGet> list)
        {
            list.Add(new FieldForGet() { Id = "Save", Name = "保存", Type = FieldType.Button });
        }
    }
}
