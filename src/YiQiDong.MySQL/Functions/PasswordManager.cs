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
        public override string Name => "密码管理";

        private List<FieldForGet> innerGet(FunctionRequest request)
        {
            List<FieldForGet> list = new List<FieldForGet>();
            string tmpKey;
            tmpKey = "password";
            list.Add(new FieldForGet()
            {
                Id = tmpKey,
                Name = "密码",
                Type = FieldType.InputText,
                Value = request == null ? Config.Instance.GetPassword() : request.GetFieldValue(tmpKey),
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
                var oldPassword = Config.Instance.GetPassword();
                var newPassword = request.GetFieldValue("password");

                var connectionString = $"Server={Config.Instance.GetConnectHost()};Port={Config.Instance.Properties["port"]};Database=mysql;Uid=root;Pwd={oldPassword};";
                var sql = string.Empty;
                var server_version_string = Agent.Instance.ContainerInfo.Image.Version;
                if (server_version_string.Contains("_"))
                    server_version_string = server_version_string.Substring(0, server_version_string.IndexOf("_"));
                var server_version = new Version(server_version_string);
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
                    Config.Instance.UpdatePassword(newPassword);
                    list.Add(new FieldForGet()
                    {
                        Name = "修改成功",
                        Description = $"修改root账号密码成功！",
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

        private void addSaveButton(List<FieldForGet> list)
        {
            list.Add(new FieldForGet() { Id = "Save", Name = "修改", Type = FieldType.Button });
        }
    }
}
