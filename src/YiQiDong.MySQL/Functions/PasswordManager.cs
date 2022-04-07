using MySql.Data.MySqlClient;
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
    class PasswordManager : AbstractFunction
    {
        public const string CONFIG_FILE = "my_password.ini";
        public static PasswordManager Instance { get; private set; }
        private string containerFolder;
        
        public override string Name => "密码管理";
        public Dictionary<string, string> Properties = new Dictionary<string, string>();
        private string containerConfigFile;

        public void RefreshProperties()
        {
            if (File.Exists(containerConfigFile))
                Properties = IniFileUtils.Load(containerConfigFile);
        }

        public PasswordManager(string imageFolder, string containerFolder)
        {
            Instance = this;
            this.containerFolder = containerFolder;
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

        private List<FieldForGet> innerGet(FunctionRequest request)
        {
            List<FieldForGet> list = new List<FieldForGet>();
            string tmpKey;
            tmpKey = "password";
            if (Properties.ContainsKey(tmpKey))
                list.Add(new FieldForGet()
                {
                    Id = tmpKey,
                    Name = "密码",
                    Type = FieldType.InputText,
                    Value = request == null ? Properties[tmpKey] : request.GetFieldValue(tmpKey),
                    Input_AllowBlank = false,
                    Description = "root用户的密码，默认为:123456"
                });
            return list;
        }

        public override FieldForGet[] Get()
        {
            //当容器未启动时，此功能不可用
            if (!Agent.Instance.ContainerInfo.AutoStart)
            {
                return new FieldForGet[]
                {
                    new FieldForGet() { Name = "当前功能不可用", Description = $"容器尚未启动，当前功能不可用。", Input_ReadOnly = true, Type = FieldType.Alert }
                };
            }

            var list = innerGet(null);
            addSaveButton(list);
            return list.ToArray();
        }

        public override FieldForGet[] Post(FunctionRequest request)
        {
            var list = innerGet(request);
            if (request.IsFieldIdsMatch("Save"))
            {
                var oldPassword = Properties["password"];
                var newPassword = request.GetFieldValue("password");

                var connectionString = $"Server={Config.Instance.GetConnectHost()};Port={Config.Instance.Properties["port"]};Database=mysql;Uid=root;Pwd={oldPassword};";
                var sql = string.Empty;
                var server_version = new Version(Agent.Instance.ContainerInfo.Image.Version);
                if (server_version >= new Version(8, 0, 0))
                    sql = $"alter user 'root'@'%' identified with mysql_native_password by '{newPassword}';flush privileges;";
                else
                    sql = $"update user set authentication_string=password('{newPassword}') where user='root';flush privileges;";

                //先连接数据库修改密码
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new MySqlCommand(sql, connection))
                        cmd.ExecuteNonQuery();
                }

                //然后再保存到配置文件
                try
                {
                    Save(request.Fields);
                    list.Add(new FieldForGet()
                    {
                        Name = "修改成功",
                        Description = $"修改root账号密码成功！",
                        Type = FieldType.MessageBox
                    });
                }
                catch(Exception ex)
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
            list.Add(new FieldForGet() { Id = "Save", Name = "修改", Type = FieldType.Button });
        }
    }
}
